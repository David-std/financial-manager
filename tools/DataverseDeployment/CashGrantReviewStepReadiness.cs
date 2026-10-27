using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// Read-only MainOperation pin evidence for the two independent F5 review
/// APIs. An API record is not enough: we require exactly one active, signed-in,
/// synchronous step bound to the correct installed package/type.
/// </summary>
internal static class CashGrantReviewStepReadiness
{
    private const string Package = "fmg_CashReviewExecution";
    private const string Plugin = "FinancialManager.CashOperations.F5_GrantReviewEndpoint";

    private static Entity[] Find(ServiceClient service, string table,
        string key, object value, params string[] columns)
    {
        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(columns), TopCount = 50
        };
        query.Criteria.AddCondition(key, ConditionOperator.Equal, value);
        var rows = service.RetrieveMultiple(query);
        if (rows == null || rows.MoreRecords)
            throw new InvalidOperationException("Unbounded review step readback.");
        return rows.Entities.ToArray();
    }

    public static int Inspect(ServiceClient service)
    {
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashGrantRequestGuardDeployment.Steps(service, false, true);
        var pkg = Find(service, "pluginpackage", "name", Package,
            "pluginpackageid", "name", "version");
        var types = Find(service, "plugintype", "typename", Plugin,
            "plugintypeid", "pluginassemblyid");
        if (pkg.Length != 1 || types.Length != 1 ||
            pkg[0].Id == Guid.Empty || types[0].Id == Guid.Empty ||
            pkg[0].GetAttributeValue<string>("version") != "1.0.0")
            throw new InvalidOperationException("Unverified review package or endpoint.");
        var assembly = types[0].GetAttributeValue<EntityReference>("pluginassemblyid");
        if (assembly?.LogicalName != "pluginassembly" || assembly.Id == Guid.Empty)
            throw new InvalidOperationException("Review type missing assembly linkage.");
        var bound = service.Retrieve("pluginassembly", assembly.Id,
            new ColumnSet("packageid"));
        if (bound?.GetAttributeValue<EntityReference>("packageid")?.Id != pkg[0].Id)
            throw new InvalidOperationException("Review endpoint package mismatch.");
        var ids = new Guid[2];
        var specs = new[] {
            (Api: "fmg_SubmitCashGrantRequest", Privilege: "prvCreatefmg_CashGrantRequest", Name: "SUBMIT"),
            (Api: "fmg_DecideCashGrantRequest", Privilege: "prvWritefmg_CashGrantRequest", Name: "DECIDE")
        };
        for (var i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            var api = Find(service, "customapi", "uniquename", spec.Api,
                "customapiid", "plugintypeid", "executeprivilegename",
                "allowedcustomprocessingsteptype", "isprivate");
            var msg = Find(service, "sdkmessage", "name", spec.Api,
                "sdkmessageid");
            if (api.Length != 1 || msg.Length != 1 ||
                api[0].GetAttributeValue<EntityReference>("plugintypeid")?.Id != types[0].Id ||
                api[0].GetAttributeValue<string>("executeprivilegename") != spec.Privilege ||
                !api[0].GetAttributeValue<bool>("isprivate") ||
                api[0].GetAttributeValue<OptionSetValue>(
                    "allowedcustomprocessingsteptype")?.Value != 0)
                throw new InvalidOperationException("Incorrect review API metadata for " + spec.Name);
            var steps = Find(service, "sdkmessageprocessingstep",
                "sdkmessageid", msg[0].Id,
                "sdkmessageprocessingstepid", "eventhandler",
                "stage", "mode", "statecode", "sdkmessageid");
            var matching = steps.Where(x =>
                x.GetAttributeValue<EntityReference>("eventhandler")?.Id == types[0].Id &&
                x.GetAttributeValue<OptionSetValue>("stage")?.Value == 30 &&
                x.GetAttributeValue<OptionSetValue>("mode")?.Value == 0 &&
                x.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0).ToArray();
            if (matching.Length != 1 || matching[0].Id == Guid.Empty ||
                steps.Any(x => x.Id != matching[0].Id &&
                    x.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0))
                throw new InvalidOperationException("Unexpected review execution step " + spec.Name);
            ids[i] = matching[0].Id;
            Console.WriteLine("F5_REVIEW_MAIN_" + spec.Name + "_EXACTLY_ONE=true");
        }
        if (ids[0] == ids[1])
            throw new InvalidOperationException("F5 review APIs share a MainOperation pin.");
        Console.WriteLine("F5_REVIEW_TWO_DISTINCT_VERIFIED_PARENT_PINS_READ_ONLY");
        return 0;
    }
}
