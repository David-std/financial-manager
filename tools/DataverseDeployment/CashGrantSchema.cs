using System;
using System.Linq;
using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static class CashGrantSchema
{
    public const string Table = "fmg_cashaccessgrant";
    private const string Solution = "FinancialManager";
    private const string UniqueKey = "fmg_CAG_PrincipalAccountOperation";
    private static Label Label(string text) => new Label(text, 3082);

    private static EntityMetadata? Read(ServiceClient svc)
    {
        try
        {
            return ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
            {
                LogicalName = Table, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true
            })).EntityMetadata;
        }
        catch (FaultException<OrganizationServiceFault> e) when (
            e.Detail?.ErrorCode == unchecked((int)0x80040217))
        {
            return null;
        }
    }

    private static BooleanAttributeMetadata Flag(string schema, string title) =>
        new BooleanAttributeMetadata
        {
            SchemaName = schema,
            DisplayName = Label(title),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
            DefaultValue = false,
            IsAuditEnabled = new BooleanManagedProperty(true),
            OptionSet = new BooleanOptionSetMetadata(
                new OptionMetadata(Label("Yes"), 1), new OptionMetadata(Label("No"), 0))
        };

    private static void EnsureColumn(ServiceClient svc, AttributeMetadata expected)
    {
        var meta = Read(svc) ?? throw new InvalidOperationException("Missing cash grant table.");
        var name = expected.SchemaName.ToLowerInvariant();
        var existing = meta.Attributes.FirstOrDefault(x => x.LogicalName == name);
        if (existing != null)
        {
            if (existing.AttributeType != expected.AttributeType)
                throw new InvalidOperationException("Cash grant attribute type drift: " + name);
            Console.WriteLine("F5_GRANT_COLUMN_PRESENT " + name);
            return;
        }
        svc.Execute(new CreateAttributeRequest
        {
            EntityName = Table, Attribute = expected, SolutionUniqueName = Solution
        });
        Console.WriteLine("F5_GRANT_COLUMN_CREATED " + name);
    }

    private static void EnsureLookup(ServiceClient svc, string schema, string title,
        string parent, string relationship)
    {
        var name = schema.ToLowerInvariant();
        if ((Read(svc)?.Attributes ?? Array.Empty<AttributeMetadata>())
            .Any(x => x.LogicalName == name))
        {
            Console.WriteLine("F5_GRANT_LOOKUP_PRESENT " + name);
            return;
        }
        svc.Execute(new CreateOneToManyRequest
        {
            SolutionUniqueName = Solution,
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = schema,
                DisplayName = Label(title),
                RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
            },
            OneToManyRelationship = new OneToManyRelationshipMetadata
            {
                SchemaName = relationship, ReferencedEntity = parent, ReferencingEntity = Table,
                CascadeConfiguration = new CascadeConfiguration
                {
                    Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict,
                    Merge = CascadeType.NoCascade, Reparent = CascadeType.NoCascade,
                    Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
                }
            }
        });
        Console.WriteLine("F5_GRANT_LOOKUP_CREATED " + name);
    }

    public static int Run(ServiceClient svc, bool apply)
    {
        Console.WriteLine("F5_GRANT_SCHEMA_" + (apply ? "APPLY" : "INSPECT"));
        var meta = Read(svc);
        if (meta == null)
        {
            Console.WriteLine("F5_GRANT_TABLE_MISSING");
            if (!apply) return 0;
            svc.Execute(new CreateEntityRequest
            {
                SolutionUniqueName = Solution,
                Entity = new EntityMetadata
                {
                    SchemaName = "fmg_CashAccessGrant",
                    DisplayName = Label("Cash Access Grant"),
                    DisplayCollectionName = Label("Cash Access Grants"),
                    OwnershipType = OwnershipTypes.UserOwned,
                    IsActivity = false, IsAuditEnabled = new BooleanManagedProperty(true),
                    Description = Label("Locked until independent dual-control entitlement service is approved")
                },
                PrimaryAttribute = new StringAttributeMetadata
                {
                    SchemaName = "fmg_Name",
                    DisplayName = Label("Reference"),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                    MaxLength = 160
                }
            });
            Console.WriteLine("F5_GRANT_TABLE_CREATED");
            meta = Read(svc) ?? throw new InvalidOperationException("Grant table registration not visible.");
        }
        if (meta.OwnershipType != OwnershipTypes.UserOwned || meta.IsAuditEnabled?.Value != true ||
            meta.IsOptimisticConcurrencyEnabled != true)
            throw new InvalidOperationException("Cash grant table must be audited, user-owned and versioned.");
        if (!apply)
        {
            var keys = meta.Keys?.Where(k => String.Equals(k.SchemaName, UniqueKey,
                StringComparison.OrdinalIgnoreCase)).ToArray() ?? Array.Empty<EntityKeyMetadata>();
            if (keys.Length > 1)
                throw new InvalidOperationException("Duplicate cash grant unique key.");
            if (keys.Length == 1 &&
                !new[] { "fmg_principal", "fmg_cashaccount", "fmg_operation" }
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(keys[0].KeyAttributes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
                        StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected cash grant key columns.");
            Console.WriteLine("F5_GRANT_KEY_STATUS=" +
                (keys.Length == 0 ? "Missing" : keys[0].EntityKeyIndexStatus.ToString()));
            // Dataverse creates alternate-key indexes through a system job.
            // Expose its status without leaking job IDs or requiring mutation.
            if (keys.Length == 1 &&
                Guid.TryParse(keys[0].AsyncJob.ToString(), out var asyncId) &&
                asyncId != Guid.Empty)
            {
                try
                {
                    var job = svc.Retrieve("asyncoperation", asyncId,
                        new ColumnSet("statecode", "statuscode", "errorcode"));
                    Console.WriteLine("F5_GRANT_KEY_JOB_STATE=" +
                        (job.GetAttributeValue<OptionSetValue>("statecode")?.Value.ToString() ?? "Unavailable"));
                    Console.WriteLine("F5_GRANT_KEY_JOB_STATUS=" +
                        (job.GetAttributeValue<OptionSetValue>("statuscode")?.Value.ToString() ?? "Unavailable"));
                    var error = job.GetAttributeValue<int?>("errorcode");
                    Console.WriteLine("F5_GRANT_KEY_JOB_HAS_ERROR=" + (error.HasValue && error.Value != 0));
                }
                catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
                {
                    Console.WriteLine("F5_GRANT_KEY_JOB_NOT_READABLE");
                }
            }
            else if (keys.Length == 1)
                Console.WriteLine("F5_GRANT_KEY_JOB_NOT_ASSIGNED");
            foreach (var name in new[] { "fmg_principal", "fmg_cashaccount",
                "fmg_operation", "fmg_isactive", "fmg_allowshared" })
                Console.WriteLine((meta.Attributes.Any(a => a.LogicalName == name)
                    ? "F5_GRANT_PRESENT " : "F5_GRANT_MISSING ") + name);
            var operation = meta.Attributes.OfType<PicklistAttributeMetadata>()
                .SingleOrDefault(x => x.LogicalName == "fmg_operation");
            if (operation != null)
            {
                var values = operation.OptionSet?.Options
                    .Select(o => o.Value).OrderBy(v => v).ToArray();
                if (values == null || !new int?[] { 797282000, 797282001, 797282002 }
                    .SequenceEqual(values))
                    throw new InvalidOperationException("Unexpected grant operation choice values.");
                Console.WriteLine("F5_GRANT_OPERATION_CHOICES_VERIFIED");
            }
            foreach (var name in new[] { "fmg_isactive", "fmg_allowshared" })
            {
                var flag = meta.Attributes.SingleOrDefault(x => x.LogicalName == name);
                if (flag != null && !(flag is BooleanAttributeMetadata))
                    throw new InvalidOperationException("Grant flag type drift: " + name);
            }
            var any = svc.RetrieveMultiple(new QueryExpression(Table)
            {
                ColumnSet = new ColumnSet(false), TopCount = 1
            }).Entities.Count != 0;
            Console.WriteLine("F5_GRANT_ROWS_EXIST=" + any);
            return 0;
        }
        EnsureColumn(svc, Flag("fmg_IsActive", "Approved and active"));
        EnsureColumn(svc, Flag("fmg_AllowShared", "Explicit shared cash account grant"));
        var choices = new OptionSetMetadata { IsGlobal = false,
            OptionSetType = OptionSetType.Picklist };
        foreach (var entry in new[] { (797282000, "SettleReceipt"), (797282001, "InternalTransfer"),
            (797282002, "Reversal") })
            choices.Options.Add(new OptionMetadata(Label(entry.Item2), entry.Item1));
        EnsureColumn(svc, new PicklistAttributeMetadata
        {
            SchemaName = "fmg_Operation", DisplayName = Label("Permitted cash operation"),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
            OptionSet = choices, IsAuditEnabled = new BooleanManagedProperty(true)
        });
        EnsureLookup(svc, "fmg_Principal", "Allowed System User", "systemuser", "fmg_CAG_User");
        EnsureLookup(svc, "fmg_CashAccount", "Permitted Cash Account", "fmg_cashaccount", "fmg_CAG_Account");
        meta = Read(svc) ?? throw new InvalidOperationException("Missing grant table after schema.");
        var key = meta.Keys?.FirstOrDefault(k =>
            String.Equals(k.SchemaName, UniqueKey, StringComparison.OrdinalIgnoreCase));
        if (key == null)
        {
            svc.Execute(new CreateEntityKeyRequest
            {
                EntityName = Table,
                EntityKey = new EntityKeyMetadata
                {
                    SchemaName = UniqueKey,
                    DisplayName = Label("Unique principal account operation"),
                    KeyAttributes = new[] { "fmg_principal", "fmg_cashaccount", "fmg_operation" }
                }
            });
            Console.WriteLine("F5_GRANT_UNIQUE_KEY_REQUESTED");
        }
        else if (!new[] { "fmg_principal", "fmg_cashaccount", "fmg_operation" }
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(key.KeyAttributes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase) ||
            key.EntityKeyIndexStatus == EntityKeyIndexStatus.Failed)
            throw new InvalidOperationException("Cash grant alternate key differs or failed.");
        Console.WriteLine("F5_GRANT_SCHEMA_CREATED_NO_GRANTS");
        return 0;
    }
}
