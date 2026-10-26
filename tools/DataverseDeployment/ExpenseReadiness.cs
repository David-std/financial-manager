using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

internal static class ExpenseReadiness
{
    // Read-only metadata inspection. No records, roles or solution settings are changed.
    public static int Inspect(ServiceClient client)
    {
        var metadata = (RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
        {
            LogicalName = "fmg_financialtransaction",
            EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
            RetrieveAsIfPublished = false
        });
        var entity = metadata.EntityMetadata;
        if (!String.Equals(entity.LogicalName, "fmg_financialtransaction", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected financial table metadata.");
        Console.WriteLine("EXPENSE_ENTITY_SET=" + entity.EntitySetName);
        Console.WriteLine("EXPENSE_OPTIMISTIC_CONCURRENCY=" + (entity.IsOptimisticConcurrencyEnabled == true));
        Console.WriteLine("EXPENSE_NATIVE_AUDIT=" + (entity.IsAuditEnabled?.Value == true));
        Console.WriteLine("EXPENSE_OWNERSHIP=" + entity.OwnershipType);
        foreach (var key in new[] { "fmg_status", "fmg_scope", "fmg_branch", "fmg_cashaccount", "fmg_category" })
        {
            var column = entity.Attributes.FirstOrDefault(a => a.LogicalName == key);
            if (column == null) throw new InvalidOperationException("Missing critical expense column: " + key);
            Console.WriteLine("EXPENSE_COLUMN=" + key + ";TYPE=" + column.AttributeType);
        }
        if (entity.IsOptimisticConcurrencyEnabled != true)
        {
            Console.Error.WriteLine("NOT READY: Dataverse table does not support ETag optimistic concurrency.");
            return 6;
        }
        return 0;
    }
}
