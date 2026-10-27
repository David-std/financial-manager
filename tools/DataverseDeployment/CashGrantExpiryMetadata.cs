using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static class CashGrantExpiryMetadata
{
    private const string Table = "fmg_cashaccessgrant";
    private const string Key = "fmg_CAG_PrincipalAccountOperation";
    private const string Attribute = "fmg_expiresatutc";

    private static EntityMetadata Read(ServiceClient service) =>
        ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = Table, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata ?? throw new InvalidOperationException("Missing cash grant table.");

    private static DateTimeAttributeMetadata? Existing(EntityMetadata metadata)
    {
        var fields = metadata.Attributes.Where(a =>
            string.Equals(a.LogicalName, Attribute, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fields.Length > 1 || (fields.Length == 1 && !(fields[0] is DateTimeAttributeMetadata)))
            throw new InvalidOperationException("Grant expiry attribute schema drift.");
        return fields.Length == 0 ? null : (DateTimeAttributeMetadata)fields[0];
    }

    private static void Verify(DateTimeAttributeMetadata column)
    {
        if (column.Format != DateTimeFormat.DateAndTime ||
            column.DateTimeBehavior?.Value != DateTimeBehavior.TimeZoneIndependent.Value ||
            column.IsAuditEnabled?.Value != true)
            throw new InvalidOperationException("Grant expiry must be audited, timezone-independent UTC clock.");
    }

    internal static int Run(ServiceClient service, bool apply)
    {
        Console.WriteLine("F5_GRANT_EXPIRY_" + (apply ? "APPLY" : "INSPECT"));
        var meta = Read(service);
        if (meta.OwnershipType != OwnershipTypes.UserOwned ||
            meta.IsAuditEnabled?.Value != true ||
            meta.IsOptimisticConcurrencyEnabled != true)
            throw new InvalidOperationException("Unsafe cash grant metadata.");
        var keys = meta.Keys?.Where(k =>
            string.Equals(k.SchemaName, Key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (keys?.Length != 1 || keys[0].EntityKeyIndexStatus != EntityKeyIndexStatus.Active)
            throw new InvalidOperationException("Cash grant unique key must be Active.");
        var column = Existing(meta);
        if (column != null)
        {
            Verify(column);
            Console.WriteLine("F5_GRANT_EXPIRY_COLUMN_PRESENT");
        }
        else Console.WriteLine("F5_GRANT_EXPIRY_COLUMN_MISSING");

        var rows = service.RetrieveMultiple(new QueryExpression(Table)
        {
            ColumnSet = new ColumnSet(false), TopCount = 1
        });
        if (rows.MoreRecords || rows.Entities.Count != 0)
            throw new InvalidOperationException("Expiry rollout requires an empty grant table.");
        Console.WriteLine("F5_GRANT_EXPIRY_ROWS_FALSE");

        if (!apply) return 0;
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashSchemaReadiness.Verify(service);

        if (column == null)
        {
            service.Execute(new CreateAttributeRequest
            {
                EntityName = Table,
                SolutionUniqueName = "FinancialManager",
                Attribute = new DateTimeAttributeMetadata
                {
                    SchemaName = "fmg_ExpiresAtUtc",
                    DisplayName = new Label("Approved grant expiry (UTC)", 3082),
                    Description = new Label("UTC clock in TimeZoneIndependent attribute; approved grants cannot remain valid indefinitely.", 3082),
                    Format = DateTimeFormat.DateAndTime,
                    DateTimeBehavior = DateTimeBehavior.TimeZoneIndependent,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                    IsAuditEnabled = new BooleanManagedProperty(true)
                }
            });
            Console.WriteLine("F5_GRANT_EXPIRY_COLUMN_CREATED");
        }
        service.Execute(new PublishXmlRequest
        {
            ParameterXml =
                "<importexportxml><entities><entity>fmg_cashaccessgrant</entity></entities></importexportxml>"
        });
        Console.WriteLine("F5_GRANT_EXPIRY_SCHEMA_PUBLISHED");
        var after = Existing(Read(service)) ??
            throw new InvalidOperationException("Grant expiry was not registered.");
        Verify(after);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashGuardDeployment.Run(service, false, true);
        var remaining = service.RetrieveMultiple(new QueryExpression(Table)
        {
            ColumnSet = new ColumnSet(false), TopCount = 1
        });
        if (remaining.MoreRecords || remaining.Entities.Count != 0)
            throw new InvalidOperationException("Unexpected grant rows after expiry metadata change.");
        Console.WriteLine("F5_GRANT_EXPIRY_ADDITIVE_MIGRATION_COMPLETE_NO_GRANTS");
        return 0;
    }
}
