using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static class CashContractReadiness
{
    // Metadata-only inspection. Never retrieves actual users, account balances,
    // cash account rows, bank data, or financial transaction amounts.
    private static readonly string[] Tables =
    {
        "fmg_cashaccount", "fmg_branch", "fmg_financialtransaction",
        "fmg_cashreceipt", "fmg_cashtransfer", "fmg_cashposting",
        "fmg_accountopening", "fmg_bankstatement", "fmg_bankreconciliation"
    };

    private static readonly string[] ApiNames =
    {
        "fmg_SettleCashReceipt", "fmg_PostCashTransfer",
        "fmg_ReverseCashOperation", "fmg_ApproveAccountOpening",
        "fmg_ReconcileBankStatement"
    };

    public static int Inspect(ServiceClient svc)
    {
        Console.WriteLine("F5_CONTRACT_INSPECT_READ_ONLY");
        foreach (var logical in Tables)
        {
            var metadata = ((RetrieveEntityResponse)svc.Execute(
                new RetrieveEntityRequest
                {
                    LogicalName = logical,
                    EntityFilters = EntityFilters.Entity | EntityFilters.Attributes |
                                    EntityFilters.Relationships | EntityFilters.Privileges,
                    RetrieveAsIfPublished = true
                })).EntityMetadata;
            if (metadata == null)
                throw new InvalidOperationException("Missing F5 contract table " + logical);

            Console.WriteLine($"TABLE {logical} owner={metadata.OwnershipType} audit={metadata.IsAuditEnabled?.Value} concurrency={metadata.IsOptimisticConcurrencyEnabled} actype={metadata.IsActivity}");
            foreach (var attribute in metadata.Attributes
                .Where(a => a.LogicalName != null && (
                    a.LogicalName.StartsWith("fmg_", StringComparison.OrdinalIgnoreCase) ||
                    a.LogicalName is "statecode" or "statuscode" or "ownerid" or "transactioncurrencyid"))
                .OrderBy(a => a.LogicalName, StringComparer.Ordinal))
            {
                var extra = attribute switch
                {
                    LookupAttributeMetadata look => " target=" + string.Join(",", look.Targets ?? Array.Empty<string>()),
                    PicklistAttributeMetadata pick => " options=" + string.Join(",", (pick.OptionSet?.Options ?? new OptionMetadataCollection())
                        .Select(o => o.Value?.ToString() ?? "?")),
                    MoneyAttributeMetadata money => $" precision={money.Precision} precisionSource={money.PrecisionSource} min={money.MinValue} max={money.MaxValue}",
                    StringAttributeMetadata str => $" maxLength={str.MaxLength}",
                    _ => ""
                };
                Console.WriteLine($"COLUMN {logical}.{attribute.LogicalName} type={attribute.AttributeType}{extra}");
            }
            var references = metadata.ManyToOneRelationships ?? Array.Empty<OneToManyRelationshipMetadata>();
            foreach (var r in references.Where(r =>
                r.ReferencingAttribute?.StartsWith("fmg_", StringComparison.OrdinalIgnoreCase) == true))
                Console.WriteLine($"RELATION {logical}.{r.ReferencingAttribute}->{r.ReferencedEntity} schema={r.SchemaName} delete={r.CascadeConfiguration?.Delete}");
        }

        foreach (var apiName in ApiNames)
        {
            var query = new QueryExpression("customapi")
            {
                ColumnSet = new ColumnSet("uniquename"), TopCount = 2
            };
            query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, apiName);
            var count = svc.RetrieveMultiple(query).Entities.Count;
            if (count > 1)
                throw new InvalidOperationException("Duplicate cash API " + apiName);
            Console.WriteLine($"F5_CUSTOM_API {apiName} count={count}");
        }
        Console.WriteLine("F5_CONTRACT_INSPECTION_COMPLETE_NO_WRITES");
        return 0;
    }
}
