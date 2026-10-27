using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// F5 functional readiness from read-only, aggregated org data.
/// No user IDs, grant records, account identifiers or sensitive fields
/// are returned. Never mutates Dataverse.
/// </summary>
internal static class CashFunctionalPrerequisites
{
    private static int Count(ServiceClient client, string table,
        string? filterField = null, object? filterValue = null)
    {
        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(false), TopCount = 10
        };
        if (filterField != null)
            query.Criteria.AddCondition(filterField, ConditionOperator.Equal, filterValue);
        var rows = client.RetrieveMultiple(query);
        if (rows == null)
            throw new InvalidOperationException("Dataverse read-only prerequisite query failed.");
        // We intentionally ask only whether none, one, or multiple records
        // exist, not total count or identities.
        return rows.Entities.Count == 0 ? 0 : rows.Entities.Count == 1 ? 1 : 2;
    }

    public static int Inspect(ServiceClient client)
    {
        CashGuardDeployment.Run(client, false, true);
        CashGrantGuardDeployment.Steps(client, false, true);
        CashGrantRequestGuardDeployment.Steps(client, false, true);
        CashFinancialExecutionPackageDeployment.RequireRegistered(client, true);
        CashFinancialApiDeployment.Run(client, false);

        var accounts = Count(client, "fmg_cashaccount", "fmg_isactive", true);
        var grants = Count(client, "fmg_cashaccessgrant", "fmg_isactive", true);
        var reviews = Count(client, "fmg_cashgrantrequest",
            "fmg_reviewstatus", 797283000);
        var branches = Count(client, "fmg_branch", "fmg_isactive", true);
        var posted = Count(client, "fmg_cashtransfer");
        Console.WriteLine("F5_ACTIVE_ACCOUNTS_BUCKET=" + accounts);
        Console.WriteLine("F5_ACTIVE_BRANCHES_BUCKET=" + branches);
        Console.WriteLine("F5_ACTIVE_GRANTS_BUCKET=" + grants);
        Console.WriteLine("F5_PENDING_GRANT_REVIEWS_BUCKET=" + reviews);
        Console.WriteLine("F5_TRANSFER_ROWS_BUCKET=" + posted);
        // >=2 active accounts and at least one appropriate grant are
        // NECESSARY but not SUFFICIENT; both account-operation grants and
        // actor privileges must be live-checked for real posting.
        var ready = accounts == 2 && grants != 0;
        Console.WriteLine("F5_REAL_TRANSFER_PRECONDITIONS_AT_LEAST_POSSIBLE=" + ready);
        if (!ready)
            Console.WriteLine("F5_POSITIVE_FINANCIAL_ACCEPTANCE_BLOCKED_NO_APPROVED_PRECONDITIONS");
        Console.WriteLine("F5_PREREQUISITES_READONLY_DONE");
        return 0;
    }
}
