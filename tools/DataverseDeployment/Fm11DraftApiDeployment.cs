using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Metadata;

internal static class Fm11DraftApiDeployment
{
    private const string Package = "fmg_MailDraftExecution";
    private const string TypeName = "FinancialManager.MailDraftExecution.MailDraftEndpoint";
    private const string Api = "fmg_CreateDraftFromMailIntake";
    private const string Solution = "FinancialManager";
    private const string Privilege = "prvCreatefmg_financialtransaction";
    private static Entity[] Find(ServiceClient service, string table, string field, object value,
                                  params string[] columns)
    {
        var query = new QueryExpression(table) {
            ColumnSet = new ColumnSet(columns), TopCount = 10
        };
        query.Criteria.AddCondition(field, ConditionOperator.Equal, value);
        var result = service.RetrieveMultiple(query);
        if (result.MoreRecords) throw new InvalidOperationException("Metadata result truncated.");
        return result.Entities.ToArray();
    }

    private static void CheckActiveKey(ServiceClient service)
    {
        var metadata = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest {
            LogicalName = "fmg_financialtransaction",
            EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true
        })).EntityMetadata;
        if (metadata.Keys == null || !metadata.Keys.Any(k =>
            k.EntityKeyIndexStatus == EntityKeyIndexStatus.Active &&
            k.KeyAttributes != null &&
            k.KeyAttributes.SequenceEqual(new [] {"fmg_externalrequestid"},
                StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("FM11 financial request idempotency key is not Active.");
    }

    public static int PackageRun(ServiceClient service, bool apply)
    {
        var pkg = Find(service,"pluginpackage","name",Package,
                       "pluginpackageid","name","version");
        var plugin = Find(service,"plugintype","typename",TypeName,
                          "plugintypeid","typename","pluginassemblyid");
        if(pkg.Length > 1 || plugin.Length > 1)
            throw new InvalidOperationException("Ambiguous FM11 package or plugin.");
        if(pkg.Length == 1 && plugin.Length == 1)
        {
            var assembly = plugin[0].GetAttributeValue<EntityReference>("pluginassemblyid");
            if(pkg[0].GetAttributeValue<string>("version") != "1.0.0" ||
               assembly == null)
                throw new InvalidOperationException("FM11 package version or identity drift.");
            var loaded = service.Retrieve("pluginassembly",assembly.Id,
                new ColumnSet("packageid"));
            if(loaded.GetAttributeValue<EntityReference>("packageid")?.Id != pkg[0].Id)
                throw new InvalidOperationException("FM11 plug-in belongs to a different package.");
            Console.WriteLine("FM11_DRAFT_PLUGIN_PACKAGE_PRESENT=true");
            Console.WriteLine("FM11_DRAFT_PLUGIN_PACKAGE_ID=" + pkg[0].Id.ToString("D"));
            return 0;
        }
        if(pkg.Length != plugin.Length)
            throw new InvalidOperationException("Incomplete FM11 plug-in registration.");
        Console.WriteLine("FM11_DRAFT_PLUGIN_PACKAGE_PRESENT=false");
        if(!apply) return 0;
        CheckActiveKey(service);
        var file = Path.Combine(Directory.GetCurrentDirectory(),"plugins",
            "FinancialManager.MailDraftExecution","bin","Debug",
            "fmg_MailDraftExecution.1.0.0.nupkg");
        var bytes = File.ReadAllBytes(file);
        if(bytes.Length < 5000 || bytes.Length > 800000)
            throw new InvalidOperationException("Unexpected FM11 package size.");
        using(var zip = new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read))
        {
            var dlls = zip.Entries.Where(e=>
                e.FullName.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(dlls.Length != 1 ||
                dlls[0].FullName != "lib/net48/FinancialManager.MailDraftExecution.dll")
                throw new InvalidOperationException("Unexpected FM11 NuGet assembly contents.");
        }
        var row = new Entity("pluginpackage") {
            ["name"] = Package, ["uniquename"] = Package, ["version"] = "1.0.0",
            ["content"] = Convert.ToBase64String(bytes)
        };
        var req = new CreateRequest { Target = row };
        req.Parameters["SolutionUniqueName"] = Solution;
        if(((CreateResponse)service.Execute(req)).id == Guid.Empty)
            throw new InvalidOperationException("FM11 package create returned empty ID.");
        var types = Find(service,"plugintype","typename",TypeName,"plugintypeid");
        if(types.Length != 1)
            throw new InvalidOperationException("FM11 handler was not registered exactly once.");
        Console.WriteLine("FM11_DRAFT_PLUGIN_PACKAGE_INSTALLED=true");
        return 0;
    }

    private static void EnsureFields(ServiceClient svc, Guid api, string table,
                                     (string Name, int Type)[] expected, bool apply)
    {
        var columns = table == "customapirequestparameter"
            ? new [] { "uniquename", "type", "isoptional" }
            : new [] { "uniquename", "type" };
        var existing = Find(svc, table, "customapiid", api, columns);
        if(existing.Length > expected.Length ||
           existing.Any(f => !expected.Any(e =>
               e.Name == f.GetAttributeValue<string>("uniquename"))))
            throw new InvalidOperationException("Unexpected FM11 API parameters.");
        foreach(var (name,type) in expected)
        {
            var rows = existing.Where(e=>e.GetAttributeValue<string>("uniquename") == name).ToArray();
            if(rows.Length > 1 ||
               rows.Length == 1 && (
               rows[0].GetAttributeValue<OptionSetValue>("type")?.Value != type ||
               table == "customapirequestparameter" &&
               rows[0].GetAttributeValue<bool>("isoptional")))
                throw new InvalidOperationException("Invalid FM11 API field " + name);
            if(rows.Length == 1) continue;
            Console.WriteLine("FM11_DRAFT_API_FIELD_MISSING=" + name);
            if(!apply) continue;
            var item = new Entity(table) {
                ["name"] = Api + "." + name,
                ["uniquename"] = name,
                ["type"] = new OptionSetValue(type),
                ["customapiid"] = new EntityReference("customapi",api)
            };
            if(table == "customapirequestparameter") item["isoptional"] = false;
            var request = new CreateRequest { Target = item };
            request.Parameters["SolutionUniqueName"] = Solution;
            if(((CreateResponse)svc.Execute(request)).id == Guid.Empty)
                throw new InvalidOperationException("FM11 API field creation failed.");
        }
    }

    public static int ApiRun(ServiceClient service, bool apply)
    {
        CheckActiveKey(service);
        var types = Find(service,"plugintype","typename",TypeName,"plugintypeid");
        if(types.Length != 1)
            throw new InvalidOperationException("FM11 handler missing or ambiguous.");
        var privileges = Find(service,"privilege","name",Privilege,"privilegeid","name");
        if(privileges.Length != 1)
            throw new InvalidOperationException("FM11 Create Financial Transaction privilege missing.");
        var found = Find(service,"customapi","uniquename",Api,
            "customapiid","uniquename","plugintypeid","executeprivilegename",
            "bindingtype","allowedcustomprocessingsteptype","isprivate",
            "isfunction","workflowsdkstepenabled");
        if(found.Length > 1) throw new InvalidOperationException("Duplicate FM11 Draft APIs.");
        if(found.Length == 0)
        {
            Console.WriteLine("FM11_DRAFT_API_MISSING=true");
            if(!apply) return 0;
            var row = new Entity("customapi") {
                ["name"] = Api, ["uniquename"] = Api,
                ["displayname"] = "Financial Manager convert reviewed intake to Draft",
                ["description"] = "Authenticated private transactional intake-to-draft command.",
                ["bindingtype"] = new OptionSetValue(0),
                ["allowedcustomprocessingsteptype"] = new OptionSetValue(0),
                ["isfunction"] = false, ["isprivate"] = true,
                ["workflowsdkstepenabled"] = false,
                ["executeprivilegename"] = Privilege,
                ["plugintypeid"] = new EntityReference("plugintype", types[0].Id)
            };
            var req = new CreateRequest { Target = row };
            req.Parameters["SolutionUniqueName"] = Solution;
            if(((CreateResponse)service.Execute(req)).id == Guid.Empty)
                throw new InvalidOperationException("FM11 Custom API creation failed.");
            found = Find(service,"customapi","uniquename",Api,
                "customapiid","uniquename","plugintypeid","executeprivilegename",
                "bindingtype","allowedcustomprocessingsteptype","isprivate",
                "isfunction","workflowsdkstepenabled");
        }
        if(found.Length != 1 || found[0].GetAttributeValue<EntityReference>("plugintypeid")?.Id != types[0].Id ||
           found[0].GetAttributeValue<string>("executeprivilegename") != Privilege ||
           found[0].GetAttributeValue<OptionSetValue>("bindingtype")?.Value != 0 ||
           found[0].GetAttributeValue<OptionSetValue>("allowedcustomprocessingsteptype")?.Value != 0 ||
           !found[0].GetAttributeValue<bool>("isprivate") ||
           found[0].GetAttributeValue<bool>("isfunction") ||
           found[0].GetAttributeValue<bool>("workflowsdkstepenabled"))
            throw new InvalidOperationException("FM11 Custom API security or handler mismatch.");
        EnsureFields(service, found[0].Id,"customapirequestparameter",
            new [] { ("IntakeId",12), ("ExpectedRowVersion",10) }, apply);
        EnsureFields(service, found[0].Id,"customapiresponseproperty",
            new [] { ("DraftId",12), ("Created",0) }, apply);
        Console.WriteLine("FM11_DRAFT_API_AUTHENTICATED_TRANSACTIONAL=true");
        Console.WriteLine("FM11_DRAFT_API_NO_AUTOMATIC_MAIL_CALL=true");
        return 0;
    }
}
