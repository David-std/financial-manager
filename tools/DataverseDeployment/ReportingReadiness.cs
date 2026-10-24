using System;
using System.Globalization;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

// Read-only reconciliation companion for Power BI Desktop's Import model.
// No financial rows are created, modified, exported or committed.
internal static class ReportingReadiness
{
    private const int Posted = 797280001;
    private static readonly string[] Dimensions =
    {
        "fmg_category", "fmg_branch", "fmg_cashaccount"
    };

    public static int Run(ServiceClient service)
    {
        foreach (var table in Dimensions)
        {
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet(false)
            };
            var count = CountPages(service, query, out _);
            Console.WriteLine("DATAVERSE_" + table + "_ROWS=" + count);
        }

        var fact = new QueryExpression("fmg_financialtransaction")
        {
            ColumnSet = new ColumnSet("fmg_amount")
        };
        fact.Criteria.AddCondition("fmg_status", ConditionOperator.Equal, Posted);
        var currency = fact.AddLink(
            "transactioncurrency", "transactioncurrencyid",
            "transactioncurrencyid", JoinOperator.Inner);
        currency.LinkCriteria.AddCondition(
            "isocurrencycode", ConditionOperator.Equal, "PEN");

        var rows = CountPages(service, fact, out var sumPEN);
        Console.WriteLine("DATAVERSE_POSTED_PEN_ROWS=" + rows);
        Console.WriteLine("DATAVERSE_POSTED_PEN_SUM=" +
            sumPEN.ToString("0.00", CultureInfo.InvariantCulture));
        Console.WriteLine("REPORTING_READ_ONLY_INSPECTION_COMPLETE");
        return 0;
    }

    private static int CountPages(ServiceClient service, QueryExpression query,
        out decimal amount)
    {
        amount = 0m;
        var count = 0;
        query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
        do
        {
            var batch = service.RetrieveMultiple(query);
            count = checked(count + batch.Entities.Count);
            foreach (var row in batch.Entities)
            {
                var money = row.GetAttributeValue<Money>("fmg_amount");
                if (money != null)
                    amount = checked(amount + money.Value);
            }

            if (!batch.MoreRecords)
                return count;
            if (string.IsNullOrEmpty(batch.PagingCookie))
                throw new InvalidOperationException("Dataverse paging cookie missing.");
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = batch.PagingCookie;
        } while (true);
    }
}
