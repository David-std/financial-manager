using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class BudgetDeployment
{
    private const string Solution="FinancialManager";
    private const string KeyName="fmg_BA_UniqueMonthlyCategory";
    private const string PackageName="fmg_BudgetIntegrity";

    public static int Run(ServiceClient svc,string mode)
    {
        if(mode=="budget-verify")
        {
            if(ReadKey(svc)?.EntityKeyIndexStatus.ToString()!="Active")
                throw new InvalidOperationException("Allocation alternate key is not Active.");
            foreach(var plan in GetPlannedSteps())
            {
                var rows=QueryByName(svc,"sdkmessageprocessingstep","name",plan.Name,
                    "sdkmessageprocessingstepid","stage","mode","statecode");
                if(rows.Count!=1 || rows[0].GetAttributeValue<OptionSetValue>("stage")?.Value!=10 ||
                    rows[0].GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
                    rows[0].GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
                    throw new InvalidOperationException("Missing/invalid synchronous guard: "+plan.Name);
                if(plan.ImageColumns!=null)
                {
                    var iq=new QueryExpression("sdkmessageprocessingstepimage")
                        {ColumnSet=new ColumnSet("entityalias","imagetype"),TopCount=2};
                    iq.Criteria.AddCondition("sdkmessageprocessingstepid",ConditionOperator.Equal,rows[0].Id);
                    var images=svc.RetrieveMultiple(iq).Entities;
                    if(images.Count!=1 || images[0].GetAttributeValue<string>("entityalias")!="Before" ||
                       images[0].GetAttributeValue<OptionSetValue>("imagetype")?.Value!=0)
                        throw new InvalidOperationException("Missing/invalid Before image: "+plan.Name);
                }
            }
            Console.WriteLine("ALL_BUDGET_STEPS_VERIFIED");
            var passed=BudgetLiveVerifier.Run(svc);
            Console.WriteLine("VERIFICATION_COMPLETE="+passed);
            return 0;
        }
        if(mode=="budget-key")
        {
            EnsureKey(svc);
            return 0;
        }
        if(mode=="budget-key-status")
        {
            var key=ReadKey(svc);
            Console.WriteLine(key==null ? "KEY_MISSING" : ("KEY_STATUS="+key.EntityKeyIndexStatus));
            return key!=null && key.EntityKeyIndexStatus.ToString()=="Active" ? 0 : 8;
        }
        if(mode=="budget-upgrade")
        {
            UpgradePackage(svc);
            return 0;
        }
        if(mode=="budget-register")
        {
            if(ReadKey(svc)?.EntityKeyIndexStatus.ToString()!="Active")
                throw new InvalidOperationException("Cannot register budget guards until the unique allocation key is Active.");
            RegisterPackage(svc);
            return 0;
        }
        if(mode=="budget-steps")
        {
            if(ReadKey(svc)?.EntityKeyIndexStatus.ToString()!="Active")
                throw new InvalidOperationException("Allocation key must be Active before creating budget steps.");
            RegisterSteps(svc);
            return 0;
        }
        if(mode=="budget-inspect")
        {
            var key=ReadKey(svc);
            Console.WriteLine(key==null ? "KEY_MISSING" : "KEY_STATUS="+key.EntityKeyIndexStatus);
            var pkg=QueryByName(svc,"pluginpackage","name",PackageName,"pluginpackageid");
            Console.WriteLine("BUDGET_PLUGIN_PACKAGES="+pkg.Count);
            var all=GetPlannedSteps();
            foreach(var plan in all)
            {
                var rows=QueryByName(svc,"sdkmessageprocessingstep","name",plan.Name,
                    "sdkmessageprocessingstepid","stage","mode","statecode");
                Console.WriteLine(plan.Name+"="+rows.Count+(rows.Count==1 ? " stage="+
                    rows[0].GetAttributeValue<OptionSetValue>("stage")?.Value : ""));
            }
            return 0;
        }
        throw new InvalidOperationException("Unsupported budget deployment action.");
    }

    private static EntityKeyMetadata? ReadKey(ServiceClient svc)
    {
        var m=(RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest{
            LogicalName="fmg_budgetallocation",EntityFilters=EntityFilters.All,RetrieveAsIfPublished=true
        });
        return m.EntityMetadata.Keys?.SingleOrDefault(k=>k.SchemaName==KeyName);
    }

    private static void EnsureKey(ServiceClient svc)
    {
        var existing=ReadKey(svc);
        if(existing!=null)
        {
            Console.WriteLine("Existing unique allocation key status="+existing.EntityKeyIndexStatus);
            return;
        }
        var existingRows=new QueryExpression("fmg_budgetallocation"){
            ColumnSet=new ColumnSet(false),TopCount=1
        };
        if(svc.RetrieveMultiple(existingRows).Entities.Count>0)
            throw new InvalidOperationException("Refusing to create a unique key before inspecting existing allocations.");

        svc.Execute(new CreateEntityKeyRequest{
            EntityName="fmg_budgetallocation",
            EntityKey=new EntityKeyMetadata {
                SchemaName=KeyName,
                DisplayName=new Label("Unique monthly category per budget",3082),
                KeyAttributes=new[]{"fmg_budget","fmg_month","fmg_category"}
            },
            SolutionUniqueName=Solution
        });
        Console.WriteLine("CREATED_KEY "+KeyName+"; wait for Dataverse background index status Active.");
    }

    private static List<Entity> QueryByName(ServiceClient svc,string table,string attribute,
                                            string name,params string[] cols)
    {
        var q=new QueryExpression(table){ColumnSet=new ColumnSet(cols),TopCount=10};
        q.Criteria.AddCondition(attribute,ConditionOperator.Equal,name);
        return svc.RetrieveMultiple(q).Entities.ToList();
    }

    private static void RegisterPackage(ServiceClient svc)
    {
        var exists=QueryByName(svc,"pluginpackage","name",PackageName,"pluginpackageid");
        if(exists.Count>0)
        {
            Console.WriteLine("Budget plug-in package already exists; refusing to overwrite.");
            return;
        }
        var path=Environment.GetEnvironmentVariable("FM_BUDGET_PACKAGE_PATH");
        if(String.IsNullOrWhiteSpace(path) || !File.Exists(path) || !path.EndsWith(".nupkg",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FM_BUDGET_PACKAGE_PATH must point to the compiled fmg_BudgetIntegrity NuGet package.");
        var bytes=File.ReadAllBytes(path);
        if(bytes.Length>10_000_000) throw new InvalidOperationException("Unexpected package size.");
        var pkg=new Entity("pluginpackage");
        pkg["name"]=PackageName;
        pkg["uniquename"]=PackageName;
        pkg["version"]="1.0.0";
        pkg["content"]=Convert.ToBase64String(bytes);
        var request=new CreateRequest{Target=pkg};
        request.Parameters["SolutionUniqueName"]=Solution;
        var res=(CreateResponse)svc.Execute(request);
        Console.WriteLine("CREATED_BUDGET_PLUGIN_PACKAGE="+(res.id!=Guid.Empty));
        foreach(var typename in new[]{
            "FinancialManager.BudgetIntegrity.BG_EnforceBudgetIntegrity",
            "FinancialManager.BudgetIntegrity.BA_EnforceAllocationIntegrity",
            "FinancialManager.BudgetIntegrity.AE_BlockDirectEventWrites"})
        {
            var count=QueryByName(svc,"plugintype","typename",typename,"plugintypeid").Count;
            if(count!=1) throw new InvalidOperationException("Plug-in type missing after package install: "+typename);
        }
        Console.WriteLine("THREE_BUDGET_TYPES_REGISTERED");
    }

    private sealed class Plan
    {
        public string Name,Table,Message,Type;
        public string? ImageColumns;
        public Plan(string name,string table,string msg,string type,string? image=null)
        {Name=name;Table=table;Message=msg;Type=type;ImageColumns=image;}
    }
    private static Plan[] GetPlannedSteps()
    {
        const string b="FinancialManager.BudgetIntegrity.BG_EnforceBudgetIntegrity";
        const string a="FinancialManager.BudgetIntegrity.BA_EnforceAllocationIntegrity";
        const string e="FinancialManager.BudgetIntegrity.AE_BlockDirectEventWrites";
        const string budgetFields="fmg_status,fmg_year,fmg_scope,fmg_branch,fmg_version,fmg_effectivefrommonth,fmg_approvedby,fmg_approvedon,ownerid,createdby,fmg_name";
        const string allocationFields="fmg_budget,fmg_month,fmg_category,fmg_amount,transactioncurrencyid,fmg_name";
        return new[]{
            new Plan("BG_ValidateCreate","fmg_budget","Create",b),
            new Plan("BG_ValidateUpdate","fmg_budget","Update",b,budgetFields),
            new Plan("BG_ProtectDeletion","fmg_budget","Delete",b,"fmg_status"),
            new Plan("BA_ValidateCreate","fmg_budgetallocation","Create",a),
            new Plan("BA_ValidateUpdate","fmg_budgetallocation","Update",a,allocationFields),
            new Plan("BA_ProtectDeletion","fmg_budgetallocation","Delete",a,allocationFields),
            new Plan("AE_BlockCreate","fmg_activityevent","Create",e),
            new Plan("AE_BlockUpdate","fmg_activityevent","Update",e),
            new Plan("AE_BlockDelete","fmg_activityevent","Delete",e)
        };
    }

    private static void UpgradePackage(ServiceClient svc)
    {
        var existing=QueryByName(svc,"pluginpackage","name",PackageName,
            "pluginpackageid","name","version");
        if(existing.Count!=1)
            throw new InvalidOperationException("Exactly one existing budget package required for upgrade.");
        var path=Environment.GetEnvironmentVariable("FM_BUDGET_PACKAGE_PATH");
        if(String.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
           !Path.GetFileName(path).StartsWith(PackageName+".1.0.0",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Package content refresh must use the registered version 1.0.0.");
        var bytes=File.ReadAllBytes(path);
        if(bytes.Length>10_000_000) throw new InvalidOperationException("Unexpected package size.");
        var row=new Entity("pluginpackage"){Id=existing[0].Id};
        row["content"]=Convert.ToBase64String(bytes);
        svc.Execute(new UpdateRequest{Target=row});
        Console.WriteLine("REFRESHED_BUDGET_PACKAGE_CONTENT_IN_PLACE");
        foreach(var classname in new[]{
            "FinancialManager.BudgetIntegrity.BG_EnforceBudgetIntegrity",
            "FinancialManager.BudgetIntegrity.BA_EnforceAllocationIntegrity",
            "FinancialManager.BudgetIntegrity.AE_BlockDirectEventWrites"})
        {
            var type=QueryByName(svc,"plugintype","typename",classname,"plugintypeid");
            if(type.Count!=1) throw new InvalidOperationException("Expected registered type after update: "+classname);
        }
        Console.WriteLine("THREE_BUDGET_CLASSES_STILL_REGISTERED");
    }

    private static void RegisterSteps(ServiceClient svc)
    {
        foreach(var plan in GetPlannedSteps())
        {
            var steps=QueryByName(svc,"sdkmessageprocessingstep","name",plan.Name,
                "sdkmessageprocessingstepid","sdkmessageid","sdkmessagefilterid","eventhandler","stage","mode","statecode");
            if(steps.Count>1) throw new InvalidOperationException("Duplicate processing step: "+plan.Name);
            if(steps.Count==1)
            {
                var row=steps[0];
                if(row.GetAttributeValue<OptionSetValue>("stage")?.Value!=10 ||
                    row.GetAttributeValue<OptionSetValue>("mode")?.Value!=0 ||
                    row.GetAttributeValue<OptionSetValue>("statecode")?.Value!=0)
                    throw new InvalidOperationException("Existing step has wrong mode/stage/state: "+plan.Name);
                Console.WriteLine("EXISTS_STEP "+plan.Name);
                continue;
            }
            var type=QueryByName(svc,"plugintype","typename",plan.Type,"plugintypeid");
            var msg=QueryByName(svc,"sdkmessage","name",plan.Message,"sdkmessageid");
            if(type.Count!=1 || msg.Count!=1) throw new InvalidOperationException("Unexpected plug-in type or message: "+plan.Name);
            var filter=new QueryExpression("sdkmessagefilter"){ColumnSet=new ColumnSet("sdkmessagefilterid"),TopCount=2};
            filter.Criteria.AddCondition("sdkmessageid",ConditionOperator.Equal,msg[0].Id);
            filter.Criteria.AddCondition("primaryobjecttypecode",ConditionOperator.Equal,plan.Table);
            var filters=svc.RetrieveMultiple(filter).Entities;
            if(filters.Count!=1) throw new InvalidOperationException("Expected one SDK message filter: "+plan.Name);
            var step=new Entity("sdkmessageprocessingstep");
            step["name"]=plan.Name;
            step["eventhandler"]=new EntityReference("plugintype",type[0].Id);
            step["sdkmessageid"]=new EntityReference("sdkmessage",msg[0].Id);
            step["sdkmessagefilterid"]=new EntityReference("sdkmessagefilter",filters[0].Id);
            step["stage"]=new OptionSetValue(10);
            step["mode"]=new OptionSetValue(0);
            step["rank"]=10;
            step["supporteddeployment"]=new OptionSetValue(0);
            var request=new CreateRequest{Target=step};
            request.Parameters["SolutionUniqueName"]=Solution;
            var id=((CreateResponse)svc.Execute(request)).id;
            Console.WriteLine("REGISTERED_STEP "+plan.Name);
            if(plan.ImageColumns!=null)
            {
                var image=new Entity("sdkmessageprocessingstepimage");
                image["name"]="Before";
                image["entityalias"]="Before";
                image["imagetype"]=new OptionSetValue(0);
                image["messagepropertyname"]="Target";
                image["attributes"]=plan.ImageColumns;
                image["sdkmessageprocessingstepid"]=new EntityReference("sdkmessageprocessingstep",id);
                var imageReq=new CreateRequest{Target=image};
                imageReq.Parameters["SolutionUniqueName"]=Solution;
                svc.Execute(imageReq);
                Console.WriteLine("  REGISTERED_BEFORE_IMAGE "+plan.Name);
            }
        }
    }
}
