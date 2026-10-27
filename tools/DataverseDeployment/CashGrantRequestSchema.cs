using System;
using System.Linq;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static class CashGrantRequestSchema
{
    private const string Table = "fmg_cashgrantrequest";
    private const string KeyName = "fmg_CGR_ExternalRequest";
    private const string Solution = "FinancialManager";
    private static Label Label(string text) => new Label(text, 3082);

    private static EntityMetadata? Read(ServiceClient svc)
    {
        try
        {
            return ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
            {
                LogicalName = Table,
                EntityFilters = EntityFilters.All,
                RetrieveAsIfPublished = true
            })).EntityMetadata;
        }
        catch (FaultException<OrganizationServiceFault> ex) when (
            ex.Detail?.ErrorCode == unchecked((int)0x80040217))
        {
            return null;
        }
    }

    private static StringAttributeMetadata StringField(string schema, string title, int length) =>
        new StringAttributeMetadata
        {
            SchemaName = schema,
            DisplayName = Label(title),
            MaxLength = length,
            IsAuditEnabled = new BooleanManagedProperty(true),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
        };

    private static DateTimeAttributeMetadata Utc(string schema, string title, bool required = true) =>
        new DateTimeAttributeMetadata
        {
            SchemaName = schema,
            DisplayName = Label(title),
            Format = DateTimeFormat.DateAndTime,
            DateTimeBehavior = DateTimeBehavior.TimeZoneIndependent,
            IsAuditEnabled = new BooleanManagedProperty(true),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None)
        };

    private static void EnsureField(ServiceClient svc, AttributeMetadata expected)
    {
        var name = expected.SchemaName.ToLowerInvariant();
        var existing = Read(svc)?.Attributes.SingleOrDefault(x =>
            string.Equals(x.LogicalName, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (existing.AttributeType != expected.AttributeType)
                throw new InvalidOperationException("F5 review column drift: " + name);
            Console.WriteLine("F5_REVIEW_FIELD_PRESENT " + name);
            return;
        }
        svc.Execute(new CreateAttributeRequest
        {
            SolutionUniqueName = Solution, EntityName = Table, Attribute = expected
        });
        Console.WriteLine("F5_REVIEW_FIELD_CREATED " + name);
    }

    private static void EnsureChoice(ServiceClient svc, string schema,
        string title, params (int Option, string Name)[] values)
    {
        var choices = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
        foreach (var value in values)
            choices.Options.Add(new OptionMetadata(Label(value.Name), value.Option));
        EnsureField(svc, new PicklistAttributeMetadata
        {
            SchemaName = schema, DisplayName = Label(title), OptionSet = choices,
            IsAuditEnabled = new BooleanManagedProperty(true),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
        });
    }

    private static void EnsureLookup(ServiceClient svc, string schema, string title,
        string target, string relationship)
    {
        var name = schema.ToLowerInvariant();
        var found = Read(svc)?.Attributes.SingleOrDefault(a =>
            string.Equals(a.LogicalName, name, StringComparison.OrdinalIgnoreCase));
        if (found != null)
        {
            if (!(found is LookupAttributeMetadata lookup) ||
                lookup.Targets == null || lookup.Targets.Length != 1 ||
                !string.Equals(lookup.Targets[0], target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("F5 review lookup drift: " + name);
            Console.WriteLine("F5_REVIEW_LOOKUP_PRESENT " + name);
            return;
        }
        svc.Execute(new CreateOneToManyRequest
        {
            SolutionUniqueName = Solution,
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = schema, DisplayName = Label(title),
                RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
            },
            OneToManyRelationship = new OneToManyRelationshipMetadata
            {
                SchemaName = relationship, ReferencedEntity = target, ReferencingEntity = Table,
                CascadeConfiguration = new CascadeConfiguration
                {
                    Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict,
                    Merge = CascadeType.NoCascade, Reparent = CascadeType.NoCascade,
                    Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
                }
            }
        });
        Console.WriteLine("F5_REVIEW_LOOKUP_CREATED " + name);
    }

    private static int Inspect(ServiceClient svc)
    {
        var meta = Read(svc);
        if (meta == null)
        {
            Console.WriteLine("F5_REVIEW_TABLE_MISSING");
            return 0;
        }
        if (meta.OwnershipType != OwnershipTypes.UserOwned ||
            meta.IsAuditEnabled?.Value != true ||
            meta.IsOptimisticConcurrencyEnabled != true)
            throw new InvalidOperationException("F5 review table audit/ownership/concurrency drift.");
        foreach (var name in new[] { "fmg_externalrequestid", "fmg_requestfingerprint",
            "fmg_reason", "fmg_operation", "fmg_changekind", "fmg_reviewstatus",
            "fmg_allowshared", "fmg_expiresatutc", "fmg_proposer",
            "fmg_beneficiary", "fmg_cashaccount", "fmg_approver" })
            Console.WriteLine((meta.Attributes.Any(a => a.LogicalName == name)
                ? "F5_REVIEW_FIELD_PRESENT " : "F5_REVIEW_FIELD_MISSING ") + name);
        var keys = meta.Keys?.Where(x => string.Equals(x.SchemaName, KeyName,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (keys?.Length > 1 || (keys?.Length == 1 &&
            (keys[0].KeyAttributes.Length != 1 ||
             keys[0].KeyAttributes[0] != "fmg_externalrequestid")))
            throw new InvalidOperationException("F5 review request unique key drift.");
        Console.WriteLine("F5_REVIEW_KEY_STATUS=" + (keys?.Length == 1
            ? keys[0].EntityKeyIndexStatus.ToString() : "Missing"));
        var q = new QueryExpression(Table) { ColumnSet = new ColumnSet(false), TopCount = 1 };
        var list = svc.RetrieveMultiple(q);
        if (list.MoreRecords)
            throw new InvalidOperationException("Unexpected paged review readback.");
        Console.WriteLine("F5_REVIEW_ROWS_EXIST=" + (list.Entities.Count > 0));
        return 0;
    }

    public static int Run(ServiceClient svc, bool apply)
    {
        Console.WriteLine("F5_REVIEW_SCHEMA_" + (apply ? "APPLY" : "INSPECT"));
        if (!apply) return Inspect(svc);

        CashGuardDeployment.Run(svc, false, true);
        CashGrantGuardDeployment.Steps(svc, false, true);
        CashSchemaReadiness.Verify(svc);
        var grants = svc.RetrieveMultiple(new QueryExpression("fmg_cashaccessgrant")
        {
            ColumnSet = new ColumnSet(false), TopCount = 1
        });
        if (grants.MoreRecords || grants.Entities.Count > 0)
            throw new InvalidOperationException("Review schema bootstrap requires zero live grants.");
        var type = svc.RetrieveMultiple(new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet("plugintypeid"), TopCount = 2,
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("typename", ConditionOperator.Equal,
                    "FinancialManager.CashGrantRequestIntegrity.F5_BlockGrantRequestWrites") }
            }
        });
        if (type.MoreRecords || type.Entities.Count != 1)
            throw new InvalidOperationException("Request deny-all package must be registered first.");

        var metadata = Read(svc);
        if (metadata == null)
        {
            svc.Execute(new CreateEntityRequest
            {
                SolutionUniqueName = Solution,
                Entity = new EntityMetadata
                {
                    SchemaName = "fmg_CashGrantRequest",
                    DisplayName = Label("Cash Grant Request"),
                    DisplayCollectionName = Label("Cash Grant Requests"),
                    OwnershipType = OwnershipTypes.UserOwned, IsActivity = false,
                    IsAuditEnabled = new BooleanManagedProperty(true),
                    Description = Label("Immutable approval workflow; all direct mutations blocked until MainOperation accepted")
                },
                PrimaryAttribute = StringField("fmg_Name", "Request reference", 160)
            });
            Console.WriteLine("F5_REVIEW_TABLE_CREATED");
        }

        EnsureField(svc, StringField("fmg_ExternalRequestId", "Immutable request id", 80));
        EnsureField(svc, StringField("fmg_RequestFingerprint", "Immutable SHA256", 64));
        EnsureField(svc, StringField("fmg_Reason", "Business justification", 300));
        EnsureField(svc, Utc("fmg_ExpiresAtUtc", "Request expires UTC"));
        EnsureField(svc, Utc("fmg_DecidedAtUtc", "Review decision UTC", false));
        EnsureField(svc, new BooleanAttributeMetadata
        {
            SchemaName = "fmg_AllowShared", DisplayName = Label("Explicit Shared grant requested"),
            IsAuditEnabled = new BooleanManagedProperty(true), DefaultValue = false,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
            OptionSet = new BooleanOptionSetMetadata(
                new OptionMetadata(Label("Yes"), 1), new OptionMetadata(Label("No"), 0))
        });
        EnsureChoice(svc, "fmg_Operation", "Financial operation",
            (797282000, "SettleReceipt"), (797282001, "InternalTransfer"), (797282002, "Reversal"));
        EnsureChoice(svc, "fmg_ChangeKind", "Grant or revoke",
            (797283100, "Grant"), (797283101, "Revoke"));
        EnsureChoice(svc, "fmg_ReviewStatus", "Review status",
            (797283000, "Pending"), (797283001, "Approved"),
            (797283002, "Rejected"), (797283003, "Expired"));
        EnsureLookup(svc, "fmg_Proposer", "Original human requester", "systemuser", "fmg_CGR_Proposer");
        EnsureLookup(svc, "fmg_Beneficiary", "Cash access beneficiary", "systemuser", "fmg_CGR_Beneficiary");
        EnsureLookup(svc, "fmg_CashAccount", "Cash account under review", "fmg_cashaccount", "fmg_CGR_Account");
        EnsureLookup(svc, "fmg_Approver", "Independent human approver", "systemuser", "fmg_CGR_Approver");

        var entity = Read(svc) ?? throw new InvalidOperationException("F5 review table missing.");
        var key = entity.Keys?.SingleOrDefault(k => string.Equals(k.SchemaName, KeyName,
            StringComparison.OrdinalIgnoreCase));
        if (key == null)
        {
            svc.Execute(new CreateEntityKeyRequest
            {
                EntityName = Table,
                EntityKey = new EntityKeyMetadata
                {
                    SchemaName = KeyName, DisplayName = Label("Unique cash grant request"),
                    KeyAttributes = new[] { "fmg_externalrequestid" }
                }
            });
            Console.WriteLine("F5_REVIEW_KEY_REQUESTED");
        }
        else if (key.KeyAttributes.Length != 1 ||
                 key.KeyAttributes[0] != "fmg_externalrequestid" ||
                 key.EntityKeyIndexStatus == EntityKeyIndexStatus.Failed)
            throw new InvalidOperationException("F5 review key mismatch or failure.");
        svc.Execute(new PublishXmlRequest
        {
            ParameterXml = "<importexportxml><entities><entity>fmg_cashgrantrequest</entity></entities></importexportxml>"
        });
        Console.WriteLine("F5_REVIEW_SCHEMA_PUBLISHED");
        return Inspect(svc);
    }
}
