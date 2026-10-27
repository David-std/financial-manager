using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// Controlled metadata-only registration. No financial records, grants or
/// existing guard steps are changed. The 18 F5G001 steps still deny every
/// cash mutation after these APIs are created.
/// </summary>
internal static class CashFinancialApiDeployment
{
    private const string Solution = "FinancialManager";
    private const string Plugin = "FinancialManager.CashOperations.F5_CashOperationEndpoint";
    private const string Package = "fmg_CashExecution";

    private sealed record Parameter(string Name, int Type, string? Entity = null);
    private sealed record Api(string Message, string Privilege, Parameter[] Inputs)
    {
        public Parameter[] Outputs => new[] {
            new Parameter("OperationId", 12),
            new Parameter("WasReplay", 0)
        };
    }

    private static readonly Api[] Endpoints = {
        new Api("fmg_SettleCashReceipt", "prvCreatefmg_CashReceipt",
            new[] {
                new Parameter("RequestKey", 10),
                new Parameter("SourceAccount", 5, "fmg_cashaccount"),
                new Parameter("Amount", 8),
                new Parameter("ValueDate", 1),
                new Parameter("EvidenceReference", 10)
            }),
        new Api("fmg_PostCashTransfer", "prvCreatefmg_CashTransfer",
            new[] {
                new Parameter("RequestKey", 10),
                new Parameter("SourceAccount", 5, "fmg_cashaccount"),
                new Parameter("DestinationAccount", 5, "fmg_cashaccount"),
                new Parameter("Amount", 8),
                new Parameter("ValueDate", 1),
                new Parameter("EvidenceReference", 10)
            })
    };

    private static Entity[] Query(ServiceClient svc, string name,
        string key, object value, params string[] fields)
    {
        var q = new QueryExpression(name) {
            ColumnSet = new ColumnSet(fields), TopCount = 50
        };
        q.Criteria.AddCondition(key, ConditionOperator.Equal, value);
        var records = svc.RetrieveMultiple(q);
        if (records == null || records.MoreRecords)
            throw new InvalidOperationException("Incomplete Custom API metadata: " + name);
        return records.Entities.ToArray();
    }

    private static Guid GetEndpointType(ServiceClient svc, bool required)
    {
        var pkg = Query(svc, "pluginpackage", "name", Package,
            "pluginpackageid", "name", "version");
        if (pkg.Length > 1) throw new InvalidOperationException("Ambiguous CashOperations package.");
        var list = Query(svc, "plugintype", "typename", Plugin,
            "plugintypeid", "typename", "pluginassemblyid");
        if (list.Length > 1) throw new InvalidOperationException("Ambiguous CashOperations endpoint class.");
        if (required && (pkg.Length != 1 || list.Length != 1 ||
            pkg[0].Id == Guid.Empty || list[0].Id == Guid.Empty))
            throw new InvalidOperationException("Verified isolated CashExecution package/type missing.");
        if (pkg.Length == 1 && list.Length == 1)
            CashFinancialExecutionPackageDeployment.RequireRegistered(svc, true);
        return list.Length == 1 ? list[0].Id : Guid.Empty;
    }

    private static void AssertApi(Entity row, Api expected, Guid typeId)
    {
        if (typeId == Guid.Empty || row.Id == Guid.Empty ||
            row.GetAttributeValue<string>("uniquename") != expected.Message ||
            row.GetAttributeValue<EntityReference>("plugintypeid")?.Id != typeId ||
            row.GetAttributeValue<string>("executeprivilegename") != expected.Privilege ||
            !row.GetAttributeValue<bool>("isprivate") ||
            row.GetAttributeValue<bool>("isfunction") ||
            row.GetAttributeValue<bool>("workflowsdkstepenabled") ||
            row.GetAttributeValue<OptionSetValue>("bindingtype")?.Value != 0 ||
            row.GetAttributeValue<OptionSetValue>("allowedcustomprocessingsteptype")?.Value != 0)
            throw new InvalidOperationException(
                "Financial API identity, handler, privilege or isolation drift: " + expected.Message);
    }

    private static Entity[] Entries(ServiceClient svc, string logical, Guid apiId)
        => logical == "customapirequestparameter"
            ? Query(svc, logical, "customapiid", apiId,
                logical + "id", "customapiid", "uniquename", "type",
                "isoptional", "logicalentityname")
            : Query(svc, logical, "customapiid", apiId,
                logical + "id", "customapiid", "uniquename", "type",
                "logicalentityname");

    private static void EnsureFields(ServiceClient svc, Api spec, Guid apiId,
        bool apply, bool outputs)
    {
        var name = outputs ? "customapiresponseproperty" : "customapirequestparameter";
        var requested = outputs ? spec.Outputs : spec.Inputs;
        var actual = Entries(svc, name, apiId);
        if (actual.Length > requested.Length ||
            actual.GroupBy(x => x.GetAttributeValue<string>("uniquename"),
                StringComparer.Ordinal).Any(g => g.Count() != 1) ||
            actual.Any(x => !requested.Any(e =>
                e.Name == x.GetAttributeValue<string>("uniquename"))))
            throw new InvalidOperationException("Unapproved financial API field found: " + spec.Message);
        foreach (var expected in requested)
        {
            var found = actual.Where(x =>
                x.GetAttributeValue<string>("uniquename") == expected.Name).ToArray();
            if (found.Length > 1) throw new InvalidOperationException("Duplicated API field.");
            if (found.Length == 1)
            {
                var row = found[0];
                if (row.GetAttributeValue<OptionSetValue>("type")?.Value != expected.Type ||
                    (!outputs && row.GetAttributeValue<bool>("isoptional")) ||
                    (expected.Entity != null &&
                     row.GetAttributeValue<string>("logicalentityname") != expected.Entity))
                    throw new InvalidOperationException("Wrong financial API field: " +
                        spec.Message + "." + expected.Name);
                continue;
            }
            if (!apply)
            {
                Console.WriteLine("F5_FINANCIAL_API_FIELD_MISSING " + spec.Message + "." + expected.Name);
                continue;
            }
            var entity = new Entity(name);
            entity["name"] = spec.Message + "." + expected.Name;
            entity["uniquename"] = expected.Name;
            entity["type"] = new OptionSetValue(expected.Type);
            entity["customapiid"] = new EntityReference("customapi", apiId);
            if (!outputs)
                entity["isoptional"] = false;
            if (expected.Entity != null)
                entity["logicalentityname"] = expected.Entity;
            var create = new CreateRequest { Target = entity };
            create.Parameters["SolutionUniqueName"] = Solution;
            if (((CreateResponse)svc.Execute(create)).id == Guid.Empty)
                throw new InvalidOperationException("Financial API field create unacknowledged.");
            Console.WriteLine("F5_FINANCIAL_API_FIELD_CREATED " + spec.Message + "." + expected.Name);
        }
        if (apply && Entries(svc, name, apiId).Length != requested.Length)
            throw new InvalidOperationException("Incomplete financial API field mapping.");
    }

    public static int Run(ServiceClient svc, bool apply)
    {
        CashGuardDeployment.Run(svc, false, true);
        CashGrantGuardDeployment.Steps(svc, false, true);
        CashGrantRequestGuardDeployment.Steps(svc, false, true);
        if (apply)
        {
            CashLedgerContractReadiness.Inspect(svc);
            CashSchemaReadiness.Verify(svc);
        }
        var type = GetEndpointType(svc, apply);
        foreach (var spec in Endpoints)
        {
            var privileges = Query(svc, "privilege", "name", spec.Privilege,
                "privilegeid", "name");
            if (privileges.Length != 1 ||
                !string.Equals(privileges[0].GetAttributeValue<string>("name"),
                    spec.Privilege, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Generated financial execution privilege missing.");
            var api = Query(svc, "customapi", "uniquename", spec.Message,
                "customapiid", "uniquename", "plugintypeid", "executeprivilegename",
                "isprivate", "isfunction", "workflowsdkstepenabled", "bindingtype",
                "allowedcustomprocessingsteptype");
            if (api.Length > 1) throw new InvalidOperationException("Duplicate cash Custom API.");
            if (api.Length == 0)
            {
                Console.WriteLine("F5_FINANCIAL_API_MISSING " + spec.Message);
                if (!apply) continue;
                var row = new Entity("customapi");
                row["name"] = spec.Message;
                row["uniquename"] = spec.Message;
                row["displayname"] = "Financial Manager guarded " + spec.Message;
                row["description"] = "Protected F5 transaction endpoint; direct CRUD guarded.";
                row["bindingtype"] = new OptionSetValue(0);
                row["allowedcustomprocessingsteptype"] = new OptionSetValue(0);
                row["isfunction"] = false;
                row["isprivate"] = true;
                row["workflowsdkstepenabled"] = false;
                row["executeprivilegename"] = spec.Privilege;
                row["plugintypeid"] = new EntityReference("plugintype", type);
                var request = new CreateRequest { Target = row };
                request.Parameters["SolutionUniqueName"] = Solution;
                if (((CreateResponse)svc.Execute(request)).id == Guid.Empty)
                    throw new InvalidOperationException("Cash API create failed.");
                Console.WriteLine("F5_FINANCIAL_API_CREATED " + spec.Message);
                api = Query(svc, "customapi", "uniquename", spec.Message,
                    "customapiid", "uniquename", "plugintypeid", "executeprivilegename",
                    "isprivate", "isfunction", "workflowsdkstepenabled", "bindingtype",
                    "allowedcustomprocessingsteptype");
            }
            if (api.Length != 1)
                throw new InvalidOperationException("Missing financial API after registration.");
            AssertApi(api[0], spec, type);
            EnsureFields(svc, spec, api[0].Id, apply, false);
            EnsureFields(svc, spec, api[0].Id, apply, true);
            Console.WriteLine("F5_FINANCIAL_API_METADATA_" +
                (apply ? "REGISTERED_GUARDED " : "INSPECTED ") + spec.Message);
        }
        if (apply)
        {
            CashGuardDeployment.Run(svc, false, true);
            CashGrantGuardDeployment.Steps(svc, false, true);
            CashGrantRequestGuardDeployment.Steps(svc, false, true);
        }
        Console.WriteLine("F5_FINANCIAL_GUARDS_UNCHANGED_24");
        return 0;
    }
}
