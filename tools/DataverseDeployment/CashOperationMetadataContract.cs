using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static class CashOperationMetadataContract
{
    private const string Solution = "FinancialManager";
    private static readonly string[] Headers = { "fmg_cashreceipt", "fmg_cashtransfer" };
    private static readonly (string Schema, string Display, int Length)[] Fields =
    {
        ("fmg_RequestFingerprint", "Canonical Server Request Fingerprint", 64),
        ("fmg_InitiatingUserId", "Verified Dataverse Initiating User Id", 36)
    };

    private static EntityMetadata Read(ServiceClient service, string table) =>
        ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = table,
            EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata ?? throw new InvalidOperationException("Missing table: " + table);

    public static int Run(ServiceClient service, bool apply)
    {
        Console.WriteLine(apply ? "F5_OPERATION_METADATA_APPLY" : "F5_OPERATION_METADATA_READONLY");
        var missing = 0;
        if (apply)
        {
            // Stop if this deployment is no longer a schema-only development
            // environment or its Active alternate keys are not intact.
            foreach (var (table, keyName) in new[]
            {
                ("fmg_cashreceipt", "fmg_CR_Request"),
                ("fmg_cashtransfer", "fmg_CT_Request"),
                ("fmg_cashposting", "fmg_CP_Leg")
            })
            {
                var meta = Read(service, table);
                if (meta.Keys?.Count(k =>
                    string.Equals(k.SchemaName, keyName, StringComparison.OrdinalIgnoreCase) &&
                    k.EntityKeyIndexStatus == EntityKeyIndexStatus.Active) != 1)
                    throw new InvalidOperationException("Required ACTIVE alternate key missing: " + keyName);
                var rows = service.RetrieveMultiple(new QueryExpression(table)
                {
                    ColumnSet = new ColumnSet(false), TopCount = 1
                });
                if (rows.Entities.Count != 0)
                    throw new InvalidOperationException("Schema-only change requires empty F5 table: " + table);
            }
        }
        foreach (var table in Headers)
        {
            var existing = Read(service, table);
            if (existing.OwnershipType != OwnershipTypes.UserOwned ||
                existing.IsAuditEnabled?.Value != true ||
                existing.IsOptimisticConcurrencyEnabled != true)
                throw new InvalidOperationException("Unsafe table metadata for " + table);
            foreach (var spec in Fields)
            {
                var logical = spec.Schema.ToLowerInvariant();
                var attr = existing.Attributes.FirstOrDefault(x =>
                    string.Equals(x.LogicalName, logical, StringComparison.OrdinalIgnoreCase));
                if (attr != null)
                {
                    if (attr is not StringAttributeMetadata text ||
                        text.MaxLength != spec.Length)
                        throw new InvalidOperationException("Unsafe column drift " + table + "." + logical);
                    Console.WriteLine("F5_OPERATION_METADATA_PRESENT " + table + "." + logical);
                    continue;
                }
                missing++;
                Console.WriteLine("F5_OPERATION_METADATA_MISSING " + table + "." + logical);
                if (!apply) continue;
                service.Execute(new CreateAttributeRequest
                {
                    EntityName = table,
                    SolutionUniqueName = Solution,
                    Attribute = new StringAttributeMetadata
                    {
                        SchemaName = spec.Schema,
                        DisplayName = new Label(spec.Display, 3082),
                        Description = new Label("Server-only immutable cash operation provenance; direct writes protected by F5G001.", 3082),
                        MaxLength = spec.Length,
                        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
                        IsAuditEnabled = new BooleanManagedProperty(true)
                    }
                });
                Console.WriteLine("F5_OPERATION_METADATA_CREATED " + table + "." + logical);
            }
        }
        if (apply)
        {
            // Explicit re-read; no creation of cash rows or role grants.
            foreach (var table in Headers)
                foreach (var spec in Fields)
                    if (Read(service, table).Attributes.OfType<StringAttributeMetadata>()
                        .Count(x => x.LogicalName == spec.Schema.ToLowerInvariant() &&
                            x.MaxLength == spec.Length) != 1)
                        throw new InvalidOperationException("Column not visible after registration: " + table);
            Console.WriteLine("F5_OPERATION_METADATA_CREATED_FIELDS=" + missing);
        }
        else Console.WriteLine("F5_OPERATION_METADATA_MISSING_COUNT=" + missing);
        return 0;
    }
}
