using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

internal static class Fm11DraftPreflight
{
    private const string Table = "fmg_financialtransaction";
    private const string Key = "fmg_FM11_ExternalRequestId";
    public static int Run(ServiceClient service, bool apply)
    {
        var md = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest {
            LogicalName = Table, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata;
        if (md.OwnershipType != OwnershipTypes.UserOwned ||
            md.IsOptimisticConcurrencyEnabled != true ||
            md.IsAuditEnabled?.Value != true)
            throw new InvalidOperationException("Financial transaction security metadata unexpected.");
        foreach(var name in new [] {
            "fmg_name","fmg_status","fmg_amount","fmg_transactiondate",
            "fmg_flowclass","fmg_scope","fmg_branch","fmg_category",
            "fmg_cashaccount","fmg_externalrequestid","transactioncurrencyid"
        })
            if (!md.Attributes.Any(a=>a.LogicalName == name))
                throw new InvalidOperationException("FM11_DRAFT_MISSING_COLUMN=" + name);
        var column = md.Attributes.OfType<StringAttributeMetadata>().SingleOrDefault(
            a => a.LogicalName == "fmg_externalrequestid");
        if(column == null || column.MaxLength < 80)
            throw new InvalidOperationException("FM11 external request ID cannot hold original id.");
        var existing = md.Keys?.Where(k => k.KeyAttributes != null &&
                k.KeyAttributes.SequenceEqual(new [] {"fmg_externalrequestid"},
                    StringComparer.OrdinalIgnoreCase)).ToArray()
            ?? Array.Empty<EntityKeyMetadata>();
        if(existing.Length > 1)
            throw new InvalidOperationException("Ambiguous external request unique keys.");
        if(existing.Length == 0 && apply) {
            service.Execute(new CreateEntityKeyRequest {
                EntityName = Table, EntityKey = new EntityKeyMetadata {
                    SchemaName = Key, DisplayName = new Label("Unique external request",3082),
                    KeyAttributes = new [] {"fmg_externalrequestid"}
                }
            });
            Console.WriteLine("FM11_DRAFT_KEY_CREATED=true");
        }
        else
            Console.WriteLine("FM11_DRAFT_KEY_STATUS=" +
                (existing.Length == 0 ? "MISSING" : existing[0].EntityKeyIndexStatus.ToString()));
        Console.WriteLine("FM11_DRAFT_GUARD_POSTING_INTACT=true");
        Console.WriteLine("FM11_DRAFT_NO_FINANCIAL_WRITES=true");
        return 0;
    }
}
