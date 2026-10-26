using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static class CashSchemaReadiness
{
    private static readonly Dictionary<string, string[]> Expected = new()
    {
        ["fmg_cashreceipt"] = new[] { "fmg_name", "fmg_externalrequestid", "fmg_amount", "fmg_receivedon",
            "fmg_status", "fmg_scope", "fmg_cashaccount", "fmg_branch" },
        ["fmg_cashtransfer"] = new[] { "fmg_name", "fmg_externalrequestid", "fmg_amount", "fmg_transferredon",
            "fmg_status", "fmg_sourceaccount", "fmg_destinationaccount" },
        ["fmg_cashposting"] = new[] { "fmg_name", "fmg_externalrequestid", "fmg_operationid",
            "fmg_kind", "fmg_signedamount", "fmg_postedon", "fmg_cashaccount" },
        ["fmg_accountopening"] = new[] { "fmg_name", "fmg_externalrequestid",
            "fmg_effectiveon", "fmg_amount", "fmg_status", "fmg_cashaccount" },
        ["fmg_bankstatement"] = new[] { "fmg_name", "fmg_externalrequestid",
            "fmg_asof", "fmg_closingbalance", "fmg_status", "fmg_cashaccount" },
        ["fmg_bankreconciliation"] = new[] { "fmg_name", "fmg_externalrequestid",
            "fmg_asof", "fmg_bookbalance", "fmg_statementbalance", "fmg_difference",
            "fmg_status", "fmg_cashaccount", "fmg_bankstatement" }
    };

    public static int Verify(ServiceClient svc)
    {
        foreach (var pair in Expected)
        {
            var response = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
            {
                LogicalName = pair.Key, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true
            });
            var meta = response.EntityMetadata;
            if (meta.OwnershipType != OwnershipTypes.UserOwned ||
                meta.IsAuditEnabled?.Value != true ||
                meta.IsOptimisticConcurrencyEnabled != true)
                throw new InvalidOperationException("F5 table owner/audit/concurrency unexpected: " + pair.Key);
            var columns = meta.Attributes.Select(a => a.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = pair.Value.Where(x => !columns.Contains(x)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException(pair.Key + " missing columns: " + String.Join(",", missing));
            Console.WriteLine("F5_VERIFIED " + pair.Key + " columnCount=" + pair.Value.Length +
                              " concurrency=" + meta.IsOptimisticConcurrencyEnabled);
            var existingRows = svc.RetrieveMultiple(new QueryExpression(pair.Key)
            {
                ColumnSet = new ColumnSet(false), TopCount = 1
            });
            Console.WriteLine("F5_HAS_ROWS " + pair.Key + "=" + (existingRows.Entities.Count > 0));
        }
        Console.WriteLine("F5_SCHEMA_VERIFIED; POSTING_AUTHORIZATION_NOT_ACCEPTED");
        return 0;
    }
}
