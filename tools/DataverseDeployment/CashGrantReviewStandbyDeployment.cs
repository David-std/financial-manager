using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// Inert-by-order F5 review guard overlap. The existing 6 deny steps have
/// rank 1, and standby steps are rank 2; no existing step is changed.
/// The candidate may not replace deny-all until separately connected-tested.
/// </summary>
internal static class CashGrantReviewStandbyDeployment
{
    private const string Solution = "FinancialManager";
    private const string Package = "fmg_CashReviewExecution";
    private const string Candidate =
        "FinancialManager.CashOperations.F5_ValidatedNestedReviewGuard";
    private const string Endpoint =
        "FinancialManager.CashOperations.F5_GrantReviewEndpoint";

    private static readonly (string Table, string Prefix)[] Tables =
    {
        ("fmg_cashgrantrequest", "F5_DenyGrantRequest_"),
        ("fmg_cashaccessgrant", "F5_DenyGrant_")
    };
    private static readonly string[] Verbs = { "Create", "Update", "Delete" };

    private static Entity[] Query(ServiceClient svc, string table,
        string field, object value, params string[] cols)
    {
        var q = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(cols), TopCount = 50
        };
        q.Criteria.AddCondition(field, ConditionOperator.Equal, value);
        var res = svc.RetrieveMultiple(q);
        if (res == null || res.MoreRecords)
            throw new InvalidOperationException("Unbounded review step metadata.");
        return res.Entities.ToArray();
    }

    private static Entity ExactlyOne(ServiceClient svc, string table,
        string field, object value, params string[] cols)
    {
        var values = Query(svc, table, field, value, cols);
        if (values.Length != 1 || values[0].Id == Guid.Empty)
            throw new InvalidOperationException("Missing or ambiguous " + table + "." + field);
        return values[0];
    }

    private static void CheckHandlerPackage(ServiceClient svc, Entity type, Guid packageId)
    {
        var owner = type.GetAttributeValue<EntityReference>("pluginassemblyid");
        if (owner?.LogicalName != "pluginassembly" || owner.Id == Guid.Empty)
            throw new InvalidOperationException("Unverified review assembly.");
        var assembly = svc.Retrieve("pluginassembly", owner.Id,
            new ColumnSet("packageid"));
        if (assembly == null || assembly.Id != owner.Id ||
            assembly.GetAttributeValue<EntityReference>("packageid")?.Id != packageId)
            throw new InvalidOperationException("Review guard belongs to wrong package.");
    }

    private static string Pins(ServiceClient svc, Guid endpointType)
    {
        var ids = new Guid[2];
        var apiSpecs = new[]
        {
            (Name:"fmg_SubmitCashGrantRequest", Priv:"prvCreatefmg_CashGrantRequest"),
            (Name:"fmg_DecideCashGrantRequest", Priv:"prvWritefmg_CashGrantRequest")
        };
        for (var i = 0; i < apiSpecs.Length; i++)
        {
            var spec = apiSpecs[i];
            var api = ExactlyOne(svc, "customapi", "uniquename", spec.Name,
                "plugintypeid", "executeprivilegename",
                "isprivate", "allowedcustomprocessingsteptype");
            if (api.GetAttributeValue<EntityReference>("plugintypeid")?.Id != endpointType ||
                api.GetAttributeValue<string>("executeprivilegename") != spec.Priv ||
                !api.GetAttributeValue<bool>("isprivate") ||
                api.GetAttributeValue<OptionSetValue>(
                    "allowedcustomprocessingsteptype")?.Value != 0)
                throw new InvalidOperationException("Review API contract drift.");
            var message = ExactlyOne(svc, "sdkmessage", "name",
                spec.Name, "sdkmessageid");
            var steps = Query(svc, "sdkmessageprocessingstep", "sdkmessageid",
                message.Id, "eventhandler", "stage", "mode", "statecode");
            var matching = steps.Where(s =>
                s.GetAttributeValue<EntityReference>("eventhandler")?.Id == endpointType &&
                s.GetAttributeValue<OptionSetValue>("stage")?.Value == 30 &&
                s.GetAttributeValue<OptionSetValue>("mode")?.Value == 0 &&
                s.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0).ToArray();
            if (matching.Length != 1 || matching[0].Id == Guid.Empty ||
                steps.Any(s => s.Id != matching[0].Id &&
                    s.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0))
                throw new InvalidOperationException("Invalid pinned MainOperation.");
            ids[i] = matching[0].Id;
        }
        if (ids[0] == ids[1])
            throw new InvalidOperationException("Review endpoints share identical pins.");
        // Pin IDs are platform identifiers; do not log or version them.
        return ids[0].ToString("D") + "|" + ids[1].ToString("D");
    }

    public static int Run(ServiceClient svc, bool apply, string? targetStep = null)
    {
        if (targetStep != null &&
            !Tables.Any(t => Verbs.Any(v => targetStep == t.Table + "_" + v)))
            throw new InvalidOperationException("Invalid single-step selection.");
        CashGuardDeployment.Run(svc, false, true);
        CashGrantGuardDeployment.Steps(svc, false, true);
        CashGrantRequestGuardDeployment.Steps(svc, false, true);

        var pkg = ExactlyOne(svc, "pluginpackage", "name", Package,
            "pluginpackageid", "version");
        if (pkg.GetAttributeValue<string>("version") != "1.0.0")
            throw new InvalidOperationException("Unexpected grant review package version.");
        var endpoint = ExactlyOne(svc, "plugintype", "typename", Endpoint,
            "pluginassemblyid");
        var guard = ExactlyOne(svc, "plugintype", "typename", Candidate,
            "pluginassemblyid");
        CheckHandlerPackage(svc, endpoint, pkg.Id);
        CheckHandlerPackage(svc, guard, pkg.Id);
        var configuration = Pins(svc, endpoint.Id);
        var created = 0;

        foreach (var table in Tables)
        {
            foreach (var verb in Verbs)
            {
                if (targetStep != null && targetStep != table.Table + "_" + verb)
                    continue;
                var sdk = ExactlyOne(svc, "sdkmessage", "name", verb, "sdkmessageid");
                var filterQuery = new QueryExpression("sdkmessagefilter")
                {
                    ColumnSet = new ColumnSet("sdkmessagefilterid"),
                    TopCount = 3
                };
                filterQuery.Criteria.AddCondition("sdkmessageid",
                    ConditionOperator.Equal, sdk.Id);
                filterQuery.Criteria.AddCondition("primaryobjecttypecode",
                    ConditionOperator.Equal, table.Table);
                var filters = svc.RetrieveMultiple(filterQuery);
                if (filters == null || filters.MoreRecords ||
                    filters.Entities.Count != 1 || filters.Entities[0].Id == Guid.Empty)
                    throw new InvalidOperationException("Missing exact table/message filter.");
                var filter = filters.Entities[0];

                var original = ExactlyOne(svc, "sdkmessageprocessingstep",
                    "name", table.Prefix + verb, "eventhandler", "sdkmessageid",
                    "sdkmessagefilterid", "stage", "mode", "rank", "statecode");
                if (original.GetAttributeValue<OptionSetValue>("stage")?.Value != 10 ||
                    original.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                    original.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    original.GetAttributeValue<int>("rank") != 1 ||
                    original.GetAttributeValue<EntityReference>("sdkmessageid")?.Id != sdk.Id ||
                    original.GetAttributeValue<EntityReference>("sdkmessagefilterid")?.Id != filter.Id ||
                    original.GetAttributeValue<EntityReference>("eventhandler")?.Id == guard.Id)
                    throw new InvalidOperationException(
                        "Original deny step is not intact, refusing standby registration.");

                var label = "F5_ReviewStandby_" + table.Table + "_" + verb;
                var steps = Query(svc, "sdkmessageprocessingstep", "name", label,
                    "eventhandler", "sdkmessageid", "sdkmessagefilterid", "stage",
                    "mode", "rank", "statecode", "configuration", "impersonatinguserid");
                if (steps.Length == 0 && apply)
                {
                    var step = new Entity("sdkmessageprocessingstep");
                    step["name"] = label;
                    step["description"] =
                        "Standby rank-2 review validator. Legacy rank-1 deny remains enabled.";
                    step["eventhandler"] = new EntityReference("plugintype", guard.Id);
                    step["sdkmessageid"] = new EntityReference("sdkmessage", sdk.Id);
                    step["sdkmessagefilterid"] =
                        new EntityReference("sdkmessagefilter", filter.Id);
                    step["stage"] = new OptionSetValue(10);
                    step["mode"] = new OptionSetValue(0);
                    step["rank"] = 2;
                    step["supporteddeployment"] = new OptionSetValue(0);
                    step["configuration"] = configuration;
                    var register = new CreateRequest { Target = step };
                    register.Parameters["SolutionUniqueName"] = Solution;
                    var added = (CreateResponse)svc.Execute(register);
                    if (added.id == Guid.Empty)
                        throw new InvalidOperationException("Standby registration returned no ID.");
                    steps = Query(svc, "sdkmessageprocessingstep", "name", label,
                        "eventhandler", "sdkmessageid", "sdkmessagefilterid", "stage",
                        "mode", "rank", "statecode", "configuration", "impersonatinguserid");
                    created++;
                }
                if (steps.Length == 0)
                {
                    Console.WriteLine("F5_REVIEW_STANDBY_MISSING " + table.Table + "_" + verb);
                    continue;
                }
                if (steps.Length != 1 || steps[0].Id == Guid.Empty ||
                    steps[0].GetAttributeValue<EntityReference>("eventhandler")?.Id != guard.Id ||
                    steps[0].GetAttributeValue<EntityReference>("sdkmessageid")?.Id != sdk.Id ||
                    steps[0].GetAttributeValue<EntityReference>("sdkmessagefilterid")?.Id != filter.Id ||
                    steps[0].GetAttributeValue<OptionSetValue>("stage")?.Value != 10 ||
                    steps[0].GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                    steps[0].GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    steps[0].GetAttributeValue<int>("rank") != 2 ||
                    steps[0].GetAttributeValue<string>("configuration") != configuration ||
                    steps[0].GetAttributeValue<EntityReference>("impersonatinguserid") != null)
                    throw new InvalidOperationException("Untrusted review standby step " + label);
                Console.WriteLine("F5_REVIEW_STANDBY_PROTECTED " + table.Table + "_" + verb);
            }
        }
        CashGuardDeployment.Run(svc, false, true);
        CashGrantGuardDeployment.Steps(svc, false, true);
        CashGrantRequestGuardDeployment.Steps(svc, false, true);
        Console.WriteLine("F5_REVIEW_STANDBY_NEW=" + created);
        Console.WriteLine("F5_REVIEW_24_LEGACY_GUARDS_ACTIVE_TRUE");
        Console.WriteLine("F5_REVIEW_NO_CUTOVER_OR_GRANT_ISSUED");
        return 0;
    }
}
