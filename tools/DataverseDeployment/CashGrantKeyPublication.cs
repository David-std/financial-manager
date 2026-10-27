using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static class CashGrantKeyPublication
{
    private const string Table = "fmg_cashaccessgrant";
    private const string KeyName = "fmg_CAG_PrincipalAccountOperation";
    private static readonly string[] Attributes =
        { "fmg_principal", "fmg_cashaccount", "fmg_operation" };

    internal static int Run(ServiceClient service)
    {
        // Only publish existing grant metadata. NEVER delete, recreate or
        // reactivate a Pending key, or create a grant in this operation.
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashSchemaReadiness.Verify(service);

        var meta = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = Table, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata ?? throw new InvalidOperationException("Missing grant schema.");
        if (meta.OwnershipType != OwnershipTypes.UserOwned ||
            meta.IsAuditEnabled?.Value != true ||
            meta.IsOptimisticConcurrencyEnabled != true)
            throw new InvalidOperationException("Grant safety metadata unexpected.");
        var keys = meta.Keys?.Where(k => string.Equals(k.SchemaName, KeyName,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (keys?.Length != 1 ||
            !keys[0].KeyAttributes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(Attributes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Expected exact cash grant unique key missing.");
        var existingRows = service.RetrieveMultiple(new QueryExpression(Table)
        {
            ColumnSet = new ColumnSet(false), TopCount = 1
        });
        if (existingRows.MoreRecords || existingRows.Entities.Count > 0)
            throw new InvalidOperationException("Cash grant publication only allowed with zero grants.");

        Console.WriteLine("F5_GRANT_KEY_BEFORE=" + keys[0].EntityKeyIndexStatus);
        if (keys[0].EntityKeyIndexStatus == EntityKeyIndexStatus.Failed)
            throw new InvalidOperationException("Failed unique key requires error-specific remediation.");
        if (keys[0].EntityKeyIndexStatus == EntityKeyIndexStatus.Active)
        {
            Console.WriteLine("F5_GRANT_KEY_ALREADY_ACTIVE_NO_CHANGES");
            return 0;
        }
        if (keys[0].EntityKeyIndexStatus != EntityKeyIndexStatus.Pending)
            throw new InvalidOperationException("Cannot publish during an in-progress build.");

        service.Execute(new PublishXmlRequest
        {
            ParameterXml =
                "<importexportxml><entities><entity>fmg_cashaccessgrant</entity></entities></importexportxml>"
        });
        Console.WriteLine("F5_GRANT_SCHEMA_ONLY_PUBLISHED");
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);

        var after = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = Table, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata ?? throw new InvalidOperationException("Grant table absent after publication");
        var key = after.Keys?.SingleOrDefault(k =>
            string.Equals(k.SchemaName, KeyName, StringComparison.OrdinalIgnoreCase));
        if (key == null || key.EntityKeyIndexStatus == EntityKeyIndexStatus.Failed)
            throw new InvalidOperationException("Unique grant key missing or failed after publish");
        if (service.RetrieveMultiple(new QueryExpression(Table)
        {
            ColumnSet = new ColumnSet(false), TopCount = 1
        }).Entities.Count > 0)
            throw new InvalidOperationException("UNSAFE grant created during publication");
        Console.WriteLine("F5_GRANT_KEY_AFTER=" + key.EntityKeyIndexStatus);
        Console.WriteLine("F5_GRANT_PUBLICATION_NO_GRANTS_OR_MONEY_WRITES");
        return 0;
    }
}
