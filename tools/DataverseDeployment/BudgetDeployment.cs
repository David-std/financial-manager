using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Crm.Sdk.Messages;
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
        if(mode=="budget-backup")
        {
            DownloadAndValidateExistingPackage(svc);
            return 0;
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
            if(pkg.Count==1) Console.WriteLine("BUDGET_PLUGIN_PACKAGE_ID="+pkg[0].Id.ToString("D"));
            foreach(var fullName in new[]{
                "FinancialManager.BudgetIntegrity.BAC_BlockDirectClaimWrites",
                "FinancialManager.BudgetIntegrity.BG_ApproveBudget"})
            {
                var rows=QueryByName(svc,"plugintype","typename",fullName,"plugintypeid");
                Console.WriteLine("PLUGIN_TYPE_"+fullName.Split('.').Last()+"="+rows.Count);
            }
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
        const string claim="FinancialManager.BudgetIntegrity.BAC_BlockDirectClaimWrites";
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
            new Plan("AE_BlockDelete","fmg_activityevent","Delete",e),
            new Plan("BAC_BlockClaimCreate","fmg_budgetapprovalclaim","Create",claim),
            new Plan("BAC_BlockClaimUpdate","fmg_budgetapprovalclaim","Update",claim),
            new Plan("BAC_BlockClaimDelete","fmg_budgetapprovalclaim","Delete",claim)
        };
    }

    /// <summary>
    /// Download the real deployed NuGet from Dataverse file storage. Querying
    /// pluginpackage.content is not a reliable way to recover this binary.
    /// Store in local TEMP only; never commit this archive to source control.
    /// </summary>
    private static string DownloadAndValidateExistingPackage(ServiceClient svc)
    {
        var rows=QueryByName(svc,"pluginpackage","name",PackageName,
            "pluginpackageid","name","version","package_name");
        if(rows.Count!=1 || rows[0].GetAttributeValue<string>("version")!="1.0.0")
            throw new InvalidOperationException("Expected one package at registered version 1.0.0.");
        var init=(InitializeFileBlocksDownloadResponse)svc.Execute(
            new InitializeFileBlocksDownloadRequest
            {
                Target=new EntityReference("pluginpackage",rows[0].Id),
                FileAttributeName="package"
            });
        if(init.FileSizeInBytes<200 || init.FileSizeInBytes>10_000_000)
            throw new InvalidOperationException("Unexpected deployed NuGet backup size.");
        var fileBytes=new List<byte>((int)init.FileSizeInBytes);
        long offset=0;
        while(offset<init.FileSizeInBytes)
        {
            var size=init.IsChunkingSupported ?
                Math.Min(4*1024*1024,init.FileSizeInBytes-offset) : init.FileSizeInBytes;
            var block=(DownloadBlockResponse)svc.Execute(new DownloadBlockRequest
            {
                FileContinuationToken=init.FileContinuationToken,
                BlockLength=size,
                Offset=offset
            });
            if(block.Data==null || block.Data.Length==0 ||
                block.Data.Length>size)
                throw new InvalidOperationException("Incomplete NuGet file download.");
            fileBytes.AddRange(block.Data);
            offset+=block.Data.Length;
        }
        if(fileBytes.Count!=init.FileSizeInBytes)
            throw new InvalidOperationException("Deployed NuGet length mismatch.");
        var bytes=fileBytes.ToArray();
        using(var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read))
        {
            if(!zip.Entries.Any(e=>e.FullName.EndsWith(".nuspec",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Downloaded plugin package has no NuGet manifest.");
        }
        var backup=Path.Combine(Path.GetTempPath(),
            "fmg_BudgetIntegrity-live-backup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".nupkg");
        File.WriteAllBytes(backup,bytes);
        if(!File.ReadAllBytes(backup).SequenceEqual(bytes))
            throw new InvalidOperationException("Cannot verify backed-up package.");
        Console.WriteLine("LIVE_NUGET_BACKUP="+backup);
        Console.WriteLine("BACKUP_BYTES="+bytes.Length);
        Console.WriteLine("BACKUP_SHA256="+Convert.ToHexString(SHA256.HashData(bytes)));
        return backup;
    }

    private static void UpgradePackage(ServiceClient svc)
    {
        // The legacy UpdateRequest against pluginpackage.content does not update
        // the binary held in Dataverse file storage. Never revive that path.
        // Use budget-backup, verify its SHA-256 locally, and then the supported
        // pac plugin push --pluginId <existing-id> --pluginFile <nupkg> operation.
        throw new InvalidOperationException(
            "Use budget-backup followed by the reviewed PAC plugin push command. " +
            "Do not update pluginpackage.content through the legacy SDK path.");
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
