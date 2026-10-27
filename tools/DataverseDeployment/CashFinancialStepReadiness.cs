using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// Read-only independent identification of the immutable MainOperation
/// step of each registered F5 API; these are the only parent identities
/// a candidate nested-write guard may accept. No records or roles modified.
/// </summary>
internal static class CashFinancialStepReadiness
{
    private static Entity[] Find(ServiceClient service, string table,
        string key, object value, params string[] fields)
    {
        var q = new QueryExpression(table) {
            ColumnSet = new ColumnSet(fields), TopCount = 60
        };
        q.Criteria.AddCondition(key, ConditionOperator.Equal, value);
        var result = service.RetrieveMultiple(q);
        if (result == null || result.MoreRecords)
            throw new InvalidOperationException("Incomplete MainOperation metadata.");
        return result.Entities.ToArray();
    }
    public static int Inspect(ServiceClient service)
    {
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashGrantRequestGuardDeployment.Steps(service, false, true);
        CashFinancialExecutionPackageDeployment.RequireRegistered(service, true);
        var target = Find(service, "plugintype", "typename",
            CashFinancialExecutionPackageDeployment.Endpoint, "plugintypeid");
        if (target.Length != 1 || target[0].Id == Guid.Empty)
            throw new InvalidOperationException("Missing F5 endpoint plugin type.");
        foreach (var spec in new[] {
            (Message:"fmg_SettleCashReceipt", Prefix:"RECEIPT"),
            (Message:"fmg_PostCashTransfer", Prefix:"TRANSFER")
        })
        {
            var api = Find(service, "customapi", "uniquename", spec.Message,
                "customapiid", "plugintypeid", "executeprivilegename",
                "allowedcustomprocessingsteptype");
            var message = Find(service, "sdkmessage", "name", spec.Message,
                "sdkmessageid", "name");
            if (api.Length != 1 || message.Length != 1 ||
                api[0].GetAttributeValue<EntityReference>("plugintypeid")?.Id != target[0].Id ||
                api[0].GetAttributeValue<OptionSetValue>(
                    "allowedcustomprocessingsteptype")?.Value != 0 ||
                string.IsNullOrWhiteSpace(api[0].GetAttributeValue<string>(
                    "executeprivilegename")))
                throw new InvalidOperationException("Unverified F5 financial API identity.");
            var steps = Find(service, "sdkmessageprocessingstep",
                "sdkmessageid", message[0].Id,
                "sdkmessageprocessingstepid", "name", "eventhandler",
                "sdkmessageid", "stage", "mode", "statecode");
            var matching = steps.Where(s =>
                s.GetAttributeValue<EntityReference>("eventhandler")?.Id == target[0].Id &&
                s.GetAttributeValue<OptionSetValue>("stage")?.Value == 30 &&
                s.GetAttributeValue<OptionSetValue>("mode")?.Value == 0 &&
                s.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0).ToArray();
            if (matching.Length != 1 || matching[0].Id == Guid.Empty ||
                steps.Any(s => s.Id != matching[0].Id &&
                    s.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0))
                throw new InvalidOperationException("Unexpected active execution steps on " + spec.Message);
            Console.WriteLine("F5_FINANCIAL_MAIN_STEP_" + spec.Prefix + "_ID=" +
                matching[0].Id.ToString("D"));
            Console.WriteLine("F5_FINANCIAL_MAIN_STEP_" + spec.Prefix + "_EXACTLY_ONE=true");
        }
        Console.WriteLine("F5_FINANCIAL_PARENT_PIN_READINESS_VERIFIED_READ_ONLY");
        return 0;
    }
}
