using System;
using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// Verify the 12 remaining Update/Delete cash guards against NONEXISTENT
/// random records. An error such as "record not found" does not count.
/// Only the exact F5G001 guard rejection proves the protective step ran.
/// No financial record is ever modified or deleted.
/// </summary>
internal static class CashFinancialDirectCrudNegativeProbe
{
    private static readonly string[] Tables = {
        "fmg_cashreceipt", "fmg_cashtransfer", "fmg_cashposting",
        "fmg_accountopening", "fmg_bankstatement", "fmg_bankreconciliation"
    };
    private static void EnsureAbsent(ServiceClient service, string table, Guid id)
    {
        var query = new QueryExpression(table) {
            ColumnSet = new ColumnSet(false), TopCount = 1
        };
        query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
        var result = service.RetrieveMultiple(query);
        if (result == null || result.MoreRecords || result.Entities.Count != 0)
            throw new InvalidOperationException("Unsafe direct-CRUD negative probe record ID.");
    }
    public static int Run(ServiceClient service, bool requireDeletes)
    {
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashGrantRequestGuardDeployment.Steps(service, false, true);
        CashSchemaReadiness.Verify(service);
        var denials = 0;
        foreach (var table in Tables)
        {
            foreach (var verb in (requireDeletes
                ? new[] { "Update", "Delete" } : new[] { "Update" }))
            {
                var id = Guid.NewGuid();
                EnsureAbsent(service, table, id);
                bool exactGuardDenial = false;
                try
                {
                    if (verb == "Update")
                    {
                        var target = new Entity(table, id);
                        target["fmg_name"] = "F5 direct unauthorized update probe";
                        service.Update(target);
                    }
                    else service.Delete(table, id);
                }
                catch (FaultException<OrganizationServiceFault> e)
                {
                    var message = e.Detail?.Message ?? e.Message;
                    exactGuardDenial = message.Contains("F5G001",
                        StringComparison.OrdinalIgnoreCase);
                }
                if (!exactGuardDenial)
                    throw new InvalidOperationException(
                        "Expected F5G001 PreValidation rejection missing: " +
                        table + "/" + verb);
                EnsureAbsent(service, table, id);
                denials++;
                Console.WriteLine("F5_GUARD_DIRECT_" + verb.ToUpperInvariant() +
                    "_DENIED_F5G001 " + table);
            }
        }
        CashSchemaReadiness.Verify(service);
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashGrantRequestGuardDeployment.Steps(service, false, true);
        var expectedCount = requireDeletes ? 12 : 6;
        if (denials != expectedCount)
            throw new InvalidOperationException("Incomplete direct-CRUD security verification.");
        Console.WriteLine(requireDeletes
            ? "F5_ALL_12_DIRECT_UPDATE_DELETE_DENIED_NO_FINANCIAL_ROWS"
            : "F5_ALL_SIX_DIRECT_UPDATE_DENIED_NO_FINANCIAL_ROWS_DELETE_REQUIRES_EXISTING_RECORD");
        return 0;
    }
}
