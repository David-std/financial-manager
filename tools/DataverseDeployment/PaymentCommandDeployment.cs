using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class PaymentCommandDeployment
{
    private const string PackageName = "fmg_PaymentExecution";
    private const string PluginType = "FinancialManager.PaymentExecution.PaymentCommandEndpoint";
    private const string Privilege = "prvWritefmg_financialtransaction";
    private const string Solution = "FinancialManager";
    private sealed record Field(string Name, int Type);
    private sealed record Definition(string Name, Field[] Inputs);
    private static readonly Definition[] Apis = {
        new Definition("fmg_PostFinancialTransaction",
            new[] { new Field("RecordId",12), new Field("ExpectedRowVersion",10) }),
        new Definition("fmg_VoidFinancialTransaction",
            new[] { new Field("RecordId",12), new Field("ExpectedRowVersion",10), new Field("Reason",10) })
    };
    private static Entity[] Find(ServiceClient svc,string table,string key,object value,params string[] fields)
    {
        var q = new QueryExpression(table) { ColumnSet = new ColumnSet(fields), TopCount = 20 };
        q.Criteria.AddCondition(key,ConditionOperator.Equal,value);
        var result = svc.RetrieveMultiple(q);
        if(result.MoreRecords) throw new InvalidOperationException("Incomplete metadata read: "+table);
        return result.Entities.ToArray();
    }
    private static Guid CheckPackage(ServiceClient svc,bool required)
    {
        var pkg = Find(svc,"pluginpackage","name",PackageName,"pluginpackageid","name","version");
        var types = Find(svc,"plugintype","typename",PluginType,
            "plugintypeid","typename","pluginassemblyid");
        if(pkg.Length>1 || types.Length>1)
            throw new InvalidOperationException("Ambiguous PaymentExecution package or type.");
        if(pkg.Length==0 || types.Length==0)
        {
            if(required || pkg.Length!=types.Length)
                throw new InvalidOperationException("PaymentExecution package/type unavailable.");
            Console.WriteLine("F4_PAYMENT_PACKAGE_PRESENT=false");
            return Guid.Empty;
        }
        if(pkg[0].Id==Guid.Empty || types[0].Id==Guid.Empty ||
            pkg[0].GetAttributeValue<string>("version")!="1.0.0")
            throw new InvalidOperationException("PaymentExecution package revision drift.");
        var assembly = types[0].GetAttributeValue<EntityReference>("pluginassemblyid");
        if(assembly==null || assembly.Id==Guid.Empty)
            throw new InvalidOperationException("PaymentExecution assembly missing.");
        var detail=svc.Retrieve("pluginassembly",assembly.Id,new ColumnSet("packageid","name"));
        if(detail.GetAttributeValue<EntityReference>("packageid")?.Id!=pkg[0].Id)
            throw new InvalidOperationException("PaymentExecution type belongs to wrong package.");
        Console.WriteLine("F4_PAYMENT_PACKAGE_PRESENT=true");
        return types[0].Id;
    }
    private static void Fields(ServiceClient svc,Guid api,Definition definition,bool apply,
        bool outputs)
    {
        string table=outputs?"customapiresponseproperty":"customapirequestparameter";
        var expected=outputs?new[]{new Field("Completed",0)}:definition.Inputs;
        var actual=outputs
            ? Find(svc,table,"customapiid",api,table+"id","uniquename","type","customapiid")
            : Find(svc,table,"customapiid",api,table+"id","uniquename","type","isoptional","customapiid");
        if(actual.Length>expected.Length ||
            actual.Any(a=>!expected.Any(e=>e.Name==a.GetAttributeValue<string>("uniquename"))) ||
            actual.GroupBy(a=>a.GetAttributeValue<string>("uniquename")).Any(g=>g.Count()!=1))
            throw new InvalidOperationException("Unexpected or repeated fields: "+definition.Name);
        foreach(var field in expected)
        {
            var found=actual.Where(a=>a.GetAttributeValue<string>("uniquename")==field.Name).ToArray();
            if(found.Length>1 ||
                (found.Length==1 &&
                  (found[0].GetAttributeValue<OptionSetValue>("type")?.Value!=field.Type ||
                  (!outputs && found[0].GetAttributeValue<bool>("isoptional")))))
                throw new InvalidOperationException("API field contract drift: "+definition.Name+"."+field.Name);
            if(found.Length==1) continue;
            Console.WriteLine("F4_PAYMENT_API_FIELD_MISSING "+definition.Name+"."+field.Name);
            if(!apply) continue;
            var row=new Entity(table);
            row["name"]=definition.Name+"."+field.Name;
            row["uniquename"]=field.Name;
            row["type"]=new OptionSetValue(field.Type);
            row["customapiid"]=new EntityReference("customapi",api);
            if(!outputs) row["isoptional"]=false;
            var request=new CreateRequest { Target=row };
            request.Parameters["SolutionUniqueName"]=Solution;
            if(((CreateResponse)svc.Execute(request)).id==Guid.Empty)
                throw new InvalidOperationException("API field creation failed.");
        }
        if(apply && Find(svc,table,"customapiid",api,table+"id").Length!=expected.Length)
            throw new InvalidOperationException("Incomplete API contract.");
    }
    public static int Probe(ServiceClient svc)
    {
        if (ExpenseReadiness.Inspect(svc) != 0)
            throw new InvalidOperationException("Expense concurrency unavailable.");
        var q = new QueryExpression("fmg_financialtransaction")
        {
            ColumnSet = new ColumnSet("fmg_status","versionnumber"),
            TopCount = 4
        };
        q.Criteria.AddCondition("fmg_status",ConditionOperator.Equal,797280000);
        int versions=0;
        foreach(var row in svc.RetrieveMultiple(q).Entities)
        {
            var loaded=svc.Retrieve("fmg_financialtransaction",row.Id,
                new ColumnSet("versionnumber","fmg_status"));
            if(!String.IsNullOrWhiteSpace(loaded.RowVersion) &&
                loaded.Contains("versionnumber") &&
                String.Equals(loaded.RowVersion,
                    Convert.ToString(loaded["versionnumber"]),StringComparison.Ordinal))
                versions++;
        }
        Console.WriteLine("F4_PAYMENT_DRAFT_SDK_ROWVERSION_MATCHES="+versions);
        foreach(var name in new[] { "fmg_PostFinancialTransaction", "fmg_VoidFinancialTransaction" })
        {
            var request=new OrganizationRequest(name);
            request["RecordId"]=Guid.NewGuid();
            request["ExpectedRowVersion"]="invalid-version";
            if(name=="fmg_VoidFinancialTransaction")
                request["Reason"]="Negative-only readless probe";
            try
            {
                svc.Execute(request);
            }
            catch(System.ServiceModel.FaultException<OrganizationServiceFault> fault)
            {
                if(fault.ToString().Contains("FPC002"))
                {
                    Console.WriteLine("F4_PAYMENT_API_REJECTS_INVALID_VERSION="+name);
                    continue;
                }
                throw new InvalidOperationException("Unexpected payment API fault: "+name+
                    " code="+fault.Detail?.ErrorCode,fault);
            }
            throw new InvalidOperationException("Unsafe payment API accepted an invalid row version.");
        }
        Console.WriteLine("F4_PAYMENT_API_NEGATIVE_PROBES_PASSED_NO_WRITES");
        return 0;
    }
    public static int Run(ServiceClient svc,bool apply)
    {
        if(ExpenseReadiness.Inspect(svc)!=0)
            throw new InvalidOperationException("Payment table is not concurrency-ready.");
        var type=CheckPackage(svc,apply);
        var metadata=svc.RetrieveMultiple(new QueryExpression("privilege") {
            ColumnSet=new ColumnSet("name"), TopCount=200,
            Criteria=new FilterExpression { Conditions={new ConditionExpression("name",ConditionOperator.Like,"%FinancialTransaction%")} }
        });
        foreach(var grant in metadata.Entities)
            Console.WriteLine("F4_PAYMENT_AVAILABLE_PRIVILEGE="+grant.GetAttributeValue<string>("name"));
        var p=Find(svc,"privilege","name",Privilege,"privilegeid","name");
        if(p.Length!=1 || !String.Equals(p[0].GetAttributeValue<string>("name"),
                Privilege,StringComparison.Ordinal))
            throw new InvalidOperationException("Exact-case payment Write privilege missing.");
        foreach(var definition in Apis)
        {
            var found=Find(svc,"customapi","uniquename",definition.Name,
                "customapiid","uniquename","plugintypeid","executeprivilegename",
                "bindingtype","allowedcustomprocessingsteptype","isprivate",
                "isfunction","workflowsdkstepenabled");
            if(found.Length>1) throw new InvalidOperationException("Duplicate payment API.");
            if(found.Length==0)
            {
                Console.WriteLine("F4_PAYMENT_API_MISSING "+definition.Name);
                if(!apply) continue;
                var row=new Entity("customapi");
                row["name"]=definition.Name;
                row["uniquename"]=definition.Name;
                row["displayname"]="Financial Manager protected expense "+definition.Name;
                row["description"]="Admin-restricted transactional payment command; FTV007 retained.";
                row["bindingtype"]=new OptionSetValue(0);
                row["allowedcustomprocessingsteptype"]=new OptionSetValue(0);
                row["isfunction"]=false;row["isprivate"]=true;
                row["workflowsdkstepenabled"]=false;
                row["executeprivilegename"]=Privilege;
                row["plugintypeid"]=new EntityReference("plugintype",type);
                var request=new CreateRequest { Target=row };
                request.Parameters["SolutionUniqueName"]=Solution;
                if(((CreateResponse)svc.Execute(request)).id==Guid.Empty)
                    throw new InvalidOperationException("Payment API creation failed.");
                found=Find(svc,"customapi","uniquename",definition.Name,
                    "customapiid","uniquename","plugintypeid","executeprivilegename",
                    "bindingtype","allowedcustomprocessingsteptype","isprivate",
                    "isfunction","workflowsdkstepenabled");
            }
            if(found.Length!=1 || found[0].Id==Guid.Empty ||
                found[0].GetAttributeValue<EntityReference>("plugintypeid")?.Id!=type ||
                found[0].GetAttributeValue<string>("executeprivilegename")!=Privilege ||
                found[0].GetAttributeValue<OptionSetValue>("bindingtype")?.Value!=0 ||
                found[0].GetAttributeValue<OptionSetValue>("allowedcustomprocessingsteptype")?.Value!=0 ||
                !found[0].GetAttributeValue<bool>("isprivate") ||
                found[0].GetAttributeValue<bool>("isfunction") ||
                found[0].GetAttributeValue<bool>("workflowsdkstepenabled"))
                throw new InvalidOperationException("Payment command API identity or privilege drift.");
            Fields(svc,found[0].Id,definition,apply,false);
            Fields(svc,found[0].Id,definition,apply,true);
            Console.WriteLine("F4_PAYMENT_API_"+(apply?"REGISTERED ":"INSPECTED ")+definition.Name);
        }
        Console.WriteLine("F4_PAYMENT_EXISTING_FTV007_GUARDS_NOT_MODIFIED");
        return 0;
    }
}
