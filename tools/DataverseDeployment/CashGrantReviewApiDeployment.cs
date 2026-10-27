using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class CashGrantReviewApiDeployment
{
    private const string Solution = "FinancialManager";
    private const string Package = "fmg_CashReviewExecution";
    private const string Plugin = "FinancialManager.CashOperations.F5_GrantReviewEndpoint";
    private sealed record Field(string Name, int Type, string? Table = null);
    private sealed record Api(string Name, string Privilege, Field[] Inputs, Field[] Outputs);
    private static readonly Api[] Definitions = {
        new Api("fmg_SubmitCashGrantRequest", "prvCreatefmg_CashGrantRequest",
            new[] {
                new Field("RequestId",12),
                new Field("Beneficiary",5,"systemuser"),
                new Field("CashAccount",5,"fmg_cashaccount"),
                new Field("Operation",9),
                new Field("ChangeKind",9),
                new Field("AllowShared",0),
                new Field("Reason",10),
                new Field("ExpiresAtUtc",1)
            },new[] {new Field("RequestId",12)}),
        new Api("fmg_DecideCashGrantRequest","prvWritefmg_CashGrantRequest",
            new[] {new Field("RequestId",12),new Field("Approved",0)},
            new[] {new Field("Recorded",0)})
    };
    private static Entity[] Find(ServiceClient service,string table,string field,object value,
        params string[] columns)
    {
        var q=new QueryExpression(table) { ColumnSet=new ColumnSet(columns), TopCount=50 };
        q.Criteria.AddCondition(field,ConditionOperator.Equal,value);
        var result=service.RetrieveMultiple(q);
        if(result==null || result.MoreRecords) throw new InvalidOperationException("Incomplete F5 review metadata read.");
        return result.Entities.ToArray();
    }
    private static Guid PackageId(ServiceClient service)
    {
        var p=Find(service,"pluginpackage","name",Package,"pluginpackageid","name","version");
        if(p.Length!=1 || p[0].Id==Guid.Empty || p[0].GetAttributeValue<string>("version")!="1.0.0")
            throw new InvalidOperationException("Missing exact independent grant review plugin package.");
        return p[0].Id;
    }
    private static Guid TypeId(ServiceClient service)
    {
        var pkg=PackageId(service);
        var types=Find(service,"plugintype","typename",Plugin,"plugintypeid","pluginassemblyid");
        if(types.Length!=1 || types[0].Id==Guid.Empty)
            throw new InvalidOperationException("Missing exact grant review plugin type.");
        var assy=types[0].GetAttributeValue<EntityReference>("pluginassemblyid");
        if(assy==null || assy.Id==Guid.Empty) throw new InvalidOperationException("Grant review assembly absent.");
        var record=service.Retrieve("pluginassembly",assy.Id,new ColumnSet("packageid"));
        if(record==null || record.Id!=assy.Id ||
           record.GetAttributeValue<EntityReference>("packageid")?.Id!=pkg)
            throw new InvalidOperationException("Grant review type/package identity mismatch.");
        return types[0].Id;
    }
    public static int PackageRun(ServiceClient service,bool apply)
    {
        CashGuardDeployment.Run(service,false,true);
        CashGrantGuardDeployment.Steps(service,false,true);
        CashGrantRequestGuardDeployment.Steps(service,false,true);
        var existing=Find(service,"pluginpackage","name",Package,"pluginpackageid");
        if(existing.Length>1) throw new InvalidOperationException("Ambiguous grant review package.");
        if(!apply)
        {
            Console.WriteLine("F5_GRANT_REVIEW_PACKAGE_EXISTS="+(existing.Length==1));
            if(existing.Length==1)
            {
                TypeId(service);
                Console.WriteLine("F5_GRANT_REVIEW_PACKAGE_ID="+existing[0].Id.ToString("D"));
            }
            return 0;
        }
        if(existing.Length!=0) throw new InvalidOperationException("Grant review package already installed; no replacement permitted.");
        if(Find(service,"plugintype","typename",Plugin,"plugintypeid").Length!=0)
            throw new InvalidOperationException("Grant review type already registered in another package.");
        var file=Environment.GetEnvironmentVariable("FM_GRANT_REVIEW_PACKAGE_PATH");
        var hash=Environment.GetEnvironmentVariable("FM_GRANT_REVIEW_PACKAGE_SHA256");
        if(string.IsNullOrWhiteSpace(file) || !Path.IsPathRooted(file) ||
           !File.Exists(file) || Path.GetFileName(file)!="fmg_CashReviewExecution.1.0.0.nupkg" ||
           string.IsNullOrWhiteSpace(hash) || hash.Length!=64)
            throw new InvalidOperationException("Exact grant review NuGet + expected SHA256 required.");
        var bytes=File.ReadAllBytes(file);
        if(bytes.Length<5000 || bytes.Length>1000000) throw new InvalidOperationException("Unexpected grant review package size.");
        using(var sha=SHA256.Create())
        {
            var actual=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
            if(!actual.Equals(hash,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Grant review package SHA256 mismatch.");
        }
        using(var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read))
        {
            var entries=zip.Entries.Select(e=>e.FullName).ToArray();
            if(entries.Count(e=>e=="lib/net48/FinancialManager.CashReviewExecution.dll")!=1 ||
               entries.Count(e=>e=="lib/net48/FinancialManager.CashDomain.dll")!=1 ||
               entries.Any(e=>e.EndsWith(".dll",StringComparison.OrdinalIgnoreCase) &&
                    e!="lib/net48/FinancialManager.CashReviewExecution.dll" &&
                    e!="lib/net48/FinancialManager.CashDomain.dll"))
                throw new InvalidOperationException("Invalid grant review package dependencies.");
        }
        var entity=new Entity("pluginpackage");
        entity["name"]=Package;entity["uniquename"]=Package;
        entity["version"]="1.0.0";entity["content"]=Convert.ToBase64String(bytes);
        var create=new CreateRequest { Target=entity };
        create.Parameters["SolutionUniqueName"]=Solution;
        var result=(CreateResponse)service.Execute(create);
        if(result.id==Guid.Empty || PackageId(service)!=result.id || TypeId(service)==Guid.Empty)
            throw new InvalidOperationException("Grant review plugin installation not verified.");
        CashGuardDeployment.Run(service,false,true);
        CashGrantGuardDeployment.Steps(service,false,true);
        CashGrantRequestGuardDeployment.Steps(service,false,true);
        Console.WriteLine("F5_GRANT_REVIEW_PACKAGE_INSTALLED_WITH_24_GUARDS_UNCHANGED");
        return 0;
    }
    private static void Fields(ServiceClient service,Api spec,Guid apiId,bool apply,bool output)
    {
        var table=output?"customapiresponseproperty":"customapirequestparameter";
        var expected=output?spec.Outputs:spec.Inputs;
        var fields=output?new[]{"uniquename","type","logicalentityname"}:
            new[]{"uniquename","type","isoptional","logicalentityname"};
        var current=Find(service,table,"customapiid",apiId,fields);
        if(current.Length>expected.Length || current.Any(x=>
            expected.Count(f=>f.Name==x.GetAttributeValue<string>("uniquename"))!=1))
            throw new InvalidOperationException("Unexpected extra/duplicate F5 review API parameter.");
        foreach(var f in expected)
        {
            var rows=current.Where(x=>x.GetAttributeValue<string>("uniquename")==f.Name).ToArray();
            if(rows.Length>1) throw new InvalidOperationException("Ambiguous review API field.");
            if(rows.Length==1)
            {
                if(rows[0].GetAttributeValue<OptionSetValue>("type")?.Value!=f.Type ||
                   (!output && rows[0].GetAttributeValue<bool>("isoptional")) ||
                   (f.Table!=null && rows[0].GetAttributeValue<string>("logicalentityname")!=f.Table))
                    throw new InvalidOperationException("Review API field metadata mismatch: "+spec.Name+"."+f.Name);
                continue;
            }
            if(!apply) {Console.WriteLine("F5_REVIEW_API_FIELD_MISSING "+spec.Name+"."+f.Name);continue;}
            var entity=new Entity(table);
            entity["name"]=spec.Name+"."+f.Name;
            entity["uniquename"]=f.Name;
            entity["type"]=new OptionSetValue(f.Type);
            entity["customapiid"]=new EntityReference("customapi",apiId);
            if(!output) entity["isoptional"]=false;
            if(f.Table!=null) entity["logicalentityname"]=f.Table;
            var req=new CreateRequest { Target=entity };
            req.Parameters["SolutionUniqueName"]=Solution;
            if(((CreateResponse)service.Execute(req)).id==Guid.Empty)
                throw new InvalidOperationException("Review API parameter creation failed.");
            Console.WriteLine("F5_REVIEW_API_FIELD_CREATED "+spec.Name+"."+f.Name);
        }
        if(apply && Find(service,table,"customapiid",apiId,"uniquename").Length!=expected.Length)
            throw new InvalidOperationException("Incomplete protected review API contract.");
    }
    public static int ApiRun(ServiceClient service,bool apply)
    {
        CashGuardDeployment.Run(service,false,true);
        CashGrantGuardDeployment.Steps(service,false,true);
        CashGrantRequestGuardDeployment.Steps(service,false,true);
        CashGrantPrivilegeReadiness.Inspect(service);
        var type=TypeId(service);
        foreach(var spec in Definitions)
        {
            var p=Find(service,"privilege","name",spec.Privilege,"privilegeid","name");
            if(p.Length!=1 || p[0].Id==Guid.Empty ||
               !string.Equals(p[0].GetAttributeValue<string>("name"),
                    spec.Privilege,StringComparison.Ordinal))
                throw new InvalidOperationException("Exact case-sensitive review execution privilege unavailable.");
            var apis=Find(service,"customapi","uniquename",spec.Name,
                "customapiid","uniquename","plugintypeid","executeprivilegename",
                "isprivate","isfunction","workflowsdkstepenabled","bindingtype",
                "allowedcustomprocessingsteptype");
            if(apis.Length>1) throw new InvalidOperationException("Duplicate cash review API.");
            if(apis.Length==0)
            {
                Console.WriteLine("F5_REVIEW_API_MISSING "+spec.Name);
                if(!apply)continue;
                var entity=new Entity("customapi");
                entity["name"]=spec.Name;
                entity["uniquename"]=spec.Name;
                entity["displayname"]="Financial Manager protected review "+spec.Name;
                entity["description"]="Cash grant review guarded by dual control; no direct CRUD.";
                entity["bindingtype"]=new OptionSetValue(0);
                entity["allowedcustomprocessingsteptype"]=new OptionSetValue(0);
                entity["isfunction"]=false;entity["isprivate"]=true;
                entity["workflowsdkstepenabled"]=false;
                entity["executeprivilegename"]=spec.Privilege;
                entity["plugintypeid"]=new EntityReference("plugintype",type);
                var req=new CreateRequest { Target=entity };
                req.Parameters["SolutionUniqueName"]=Solution;
                if(((CreateResponse)service.Execute(req)).id==Guid.Empty)
                    throw new InvalidOperationException("Grant review API create rejected.");
                apis=Find(service,"customapi","uniquename",spec.Name,
                    "customapiid","uniquename","plugintypeid","executeprivilegename",
                    "isprivate","isfunction","workflowsdkstepenabled","bindingtype",
                    "allowedcustomprocessingsteptype");
            }
            if(apis.Length!=1 || apis[0].Id==Guid.Empty ||
               apis[0].GetAttributeValue<EntityReference>("plugintypeid")?.Id!=type ||
               apis[0].GetAttributeValue<string>("executeprivilegename")!=spec.Privilege ||
               !apis[0].GetAttributeValue<bool>("isprivate") ||
               apis[0].GetAttributeValue<bool>("isfunction") ||
               apis[0].GetAttributeValue<bool>("workflowsdkstepenabled") ||
               apis[0].GetAttributeValue<OptionSetValue>("bindingtype")?.Value!=0 ||
               apis[0].GetAttributeValue<OptionSetValue>("allowedcustomprocessingsteptype")?.Value!=0)
                throw new InvalidOperationException("Review API identity or privilege drift.");
            Fields(service,spec,apis[0].Id,apply,false);
            Fields(service,spec,apis[0].Id,apply,true);
            Console.WriteLine("F5_REVIEW_API_"+(apply?"REGISTERED_GUARDED ":"INSPECTED ")+spec.Name);
        }
        CashGuardDeployment.Run(service,false,true);
        CashGrantGuardDeployment.Steps(service,false,true);
        CashGrantRequestGuardDeployment.Steps(service,false,true);
        Console.WriteLine("F5_REVIEW_24_DIRECT_MUTATION_GUARDS_UNCHANGED");
        return 0;
    }
}
