using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.ServiceModel;

internal static class CashSchemaDeployment
{
    private const string Solution = "FinancialManager";
    private const int Language = 3082;
    private record TableSpec(string Logical, string Schema, string Title, string Plural);

    private static readonly TableSpec[] Tables =
    {
        new("fmg_cashreceipt", "fmg_CashReceipt", "Cash Receipt", "Cash Receipts"),
        new("fmg_cashtransfer", "fmg_CashTransfer", "Cash Transfer", "Cash Transfers"),
        new("fmg_cashposting", "fmg_CashPosting", "Cash Posting", "Cash Postings"),
        new("fmg_accountopening", "fmg_AccountOpening", "Account Opening", "Account Openings"),
        new("fmg_bankstatement", "fmg_BankStatement", "Bank Statement", "Bank Statements"),
        new("fmg_bankreconciliation", "fmg_BankReconciliation", "Bank Reconciliation", "Bank Reconciliations")
    };
    private static Label L(string value) => new(value, Language);
    private static AttributeRequiredLevelManagedProperty Optional() =>
        new(AttributeRequiredLevel.None);

    private static EntityMetadata? Find(ServiceClient svc, string table)
    {
        try
        {
            return ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
            {
                LogicalName = table,
                EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
                RetrieveAsIfPublished = true
            })).EntityMetadata;
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            var message = (ex.Detail?.Message ?? ex.Message).ToLowerInvariant();
            if (ex.Detail?.ErrorCode == unchecked((int)0x80040217) ||
                message.Contains("not found") || message.Contains("could not find") ||
                message.Contains("does not exist") || message.Contains("cannot find") ||
                message.Contains("does not contain") || message.Contains("no se encontró"))
                return null;
            throw;
        }
    }

    private static void EnsureTable(ServiceClient svc, TableSpec table, bool apply)
    {
        var existing = Find(svc, table.Logical);
        if (existing != null)
        {
            if (existing.OwnershipType != OwnershipTypes.UserOwned)
                throw new InvalidOperationException("Ownership unexpected for " + table.Logical);
            if (existing.IsAuditEnabled?.Value != true)
                throw new InvalidOperationException("Audit must be enabled for " + table.Logical);
            Console.WriteLine("VERIFIED_TABLE " + table.Logical);
            return;
        }
        Console.WriteLine("MISSING_TABLE " + table.Logical);
        if (!apply) return;
        var metadata = new EntityMetadata
        {
            SchemaName = table.Schema,
            DisplayName = L(table.Title),
            DisplayCollectionName = L(table.Plural),
            Description = L("F5 cash-domain metadata; Draft-only until protected transaction APIs and roles are accepted."),
            OwnershipType = OwnershipTypes.UserOwned,
            IsActivity = false,
            IsAuditEnabled = new BooleanManagedProperty(true)
        };
        var primary = new StringAttributeMetadata
        {
            SchemaName = "fmg_Name",
            DisplayName = L("Reference"),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
            MaxLength = 160
        };
        svc.Execute(new CreateEntityRequest { Entity = metadata, PrimaryAttribute = primary, SolutionUniqueName = Solution });
        Console.WriteLine("CREATED_TABLE " + table.Logical);
    }

    private static StringAttributeMetadata Text(string schema, string display, int max = 150) =>
        new() { SchemaName = schema, DisplayName = L(display), MaxLength = max, RequiredLevel = Optional() };
    private static DateTimeAttributeMetadata Date(string schema, string display) =>
        new() { SchemaName = schema, DisplayName = L(display), Format = DateTimeFormat.DateOnly, RequiredLevel = Optional() };
    private static MoneyAttributeMetadata Money(string schema, string display, bool signed = false) =>
        new()
        {
            SchemaName = schema, DisplayName = L(display), RequiredLevel = Optional(),
            MinValue = signed ? -1000000000 : 0, MaxValue = 1000000000,
            Precision = 2, PrecisionSource = 0
        };
    private static PicklistAttributeMetadata Choice(string schema, string display, int initial, params string[] names)
    {
        var options = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
        for (var i = 0; i < names.Length; ++i)
            options.Options.Add(new OptionMetadata(L(names[i]), initial + i));
        return new PicklistAttributeMetadata
        {
            SchemaName = schema, DisplayName = L(display), OptionSet = options,
            DefaultFormValue = initial, RequiredLevel = Optional()
        };
    }
    private static void EnsureAttributes(ServiceClient svc, string table, bool apply, params AttributeMetadata[] attrs)
    {
        if (!apply) return;
        var existing = Find(svc, table) ?? throw new InvalidOperationException("Missing table " + table);
        var names = new HashSet<string>(existing.Attributes.Select(a => a.LogicalName), StringComparer.OrdinalIgnoreCase);
        foreach (var attr in attrs)
        {
            var logical = attr.SchemaName.ToLowerInvariant();
            if (names.Contains(logical))
            {
                var current = existing.Attributes.First(a => String.Equals(a.LogicalName, logical, StringComparison.OrdinalIgnoreCase));
                if (attr.AttributeType != current.AttributeType)
                    throw new InvalidOperationException("Attribute type drift at " + table + "." + logical);
                continue;
            }
            svc.Execute(new CreateAttributeRequest { EntityName = table, Attribute = attr, SolutionUniqueName = Solution });
            Console.WriteLine("CREATED_COLUMN " + table + "." + logical);
            names.Add(logical);
        }
    }

    private static void Lookup(ServiceClient svc, string child, string parent, string schema,
                               string caption, string relation, bool apply)
    {
        if (!apply) return;
        var existing = Find(svc, child) ?? throw new InvalidOperationException("Missing child table");
        if (existing.Attributes.Any(a => String.Equals(a.LogicalName, schema.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("EXISTS_LOOKUP " + child + "." + schema.ToLowerInvariant());
            return;
        }
        var rel = new OneToManyRelationshipMetadata
        {
            SchemaName = relation,
            ReferencedEntity = parent,
            ReferencingEntity = child,
            CascadeConfiguration = new CascadeConfiguration
            {
                Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict,
                Merge = CascadeType.NoCascade, Reparent = CascadeType.NoCascade,
                Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
            }
        };
        var attr = new LookupAttributeMetadata
        {
            SchemaName = schema, DisplayName = L(caption), RequiredLevel = Optional()
        };
        svc.Execute(new CreateOneToManyRequest { OneToManyRelationship = rel, Lookup = attr, SolutionUniqueName = Solution });
        Console.WriteLine("CREATED_LOOKUP " + child + "." + schema.ToLowerInvariant());
    }

    public static int Run(ServiceClient svc, bool apply)
    {
        Console.WriteLine("F5_SCHEMA " + (apply ? "APPLY" : "INSPECT"));
        foreach (var table in Tables) EnsureTable(svc, table, apply);
        if (!apply)
        {
            Console.WriteLine("F5_SCHEMA_READONLY_INSPECTION_COMPLETE");
            return 0;
        }

        EnsureAttributes(svc, "fmg_cashreceipt",
            true,
            Text("fmg_ExternalRequestId", "Idempotency Reference", 100),
            Money("fmg_Amount", "Received Amount PEN"),
            Date("fmg_ReceivedOn", "Receipt Value Date"),
            Choice("fmg_Status", "Receipt Status", 797281000, "Draft", "Settled", "Reversed"),
            Choice("fmg_Scope", "Scope", 797281010, "Branch", "Shared"),
            Text("fmg_Description", "Description", 500),
            Text("fmg_DocumentReference", "Document Reference", 180),
            Text("fmg_ReversalReason", "Reversal Reason", 300));
        EnsureAttributes(svc, "fmg_cashtransfer", true,
            Text("fmg_ExternalRequestId", "Idempotency Reference", 100),
            Money("fmg_Amount", "Transfer Amount PEN"),
            Date("fmg_TransferredOn", "Transfer Value Date"),
            Choice("fmg_Status", "Transfer Status", 797281100, "Draft", "Posted", "Reversed"),
            Text("fmg_Description", "Description", 500),
            Text("fmg_ReversalReason", "Reversal Reason", 300));
        EnsureAttributes(svc, "fmg_cashposting", true,
            Text("fmg_ExternalRequestId", "Immutable Posting Key", 100),
            Text("fmg_OperationId", "Source Operation Id", 36),
            Choice("fmg_Kind", "Posting Kind", 797281200, "Receipt", "Payment", "Transfer", "Reversal"),
            Money("fmg_SignedAmount", "Signed Amount PEN", true),
            Date("fmg_PostedOn", "Posting Date"),
            Text("fmg_ReversesPostingId", "Reverses Posting Id", 36),
            Text("fmg_Description", "Posting Description", 250));
        EnsureAttributes(svc, "fmg_accountopening", true,
            Text("fmg_ExternalRequestId", "Idempotency Reference", 100),
            Date("fmg_EffectiveOn", "Opening Effective Date"),
            Money("fmg_Amount", "Opening Amount PEN", true),
            Choice("fmg_Status", "Opening Status", 797281300, "Draft", "Approved", "Reversed"),
            Text("fmg_Explanation", "Explanation", 500));
        EnsureAttributes(svc, "fmg_bankstatement", true,
            Text("fmg_ExternalRequestId", "Statement Import Key", 100),
            Date("fmg_AsOf", "Statement Closing Date"),
            Money("fmg_ClosingBalance", "Bank Closing Balance PEN", true),
            Choice("fmg_Status", "Statement Status", 797281400, "Imported", "Verified"),
            Text("fmg_SourceReference", "External Statement Reference", 200));
        EnsureAttributes(svc, "fmg_bankreconciliation", true,
            Text("fmg_ExternalRequestId", "Reconciliation Key", 100),
            Date("fmg_AsOf", "Reconciliation Date"),
            Money("fmg_BookBalance", "Book Balance PEN", true),
            Money("fmg_StatementBalance", "Statement Balance PEN", true),
            Money("fmg_Difference", "Unresolved Difference PEN", true),
            Choice("fmg_Status", "Reconciliation Status", 797281500, "Draft", "Approved"),
            Text("fmg_Explanation", "Reconciliation Notes", 500));

        Lookup(svc, "fmg_cashreceipt", "fmg_cashaccount", "fmg_CashAccount", "Cash Account", "fmg_CR_Account", true);
        Lookup(svc, "fmg_cashreceipt", "fmg_branch", "fmg_Branch", "Branch", "fmg_CR_Branch", true);
        Lookup(svc, "fmg_cashtransfer", "fmg_cashaccount", "fmg_SourceAccount", "Source Cash Account", "fmg_CT_SourceAccount", true);
        Lookup(svc, "fmg_cashtransfer", "fmg_cashaccount", "fmg_DestinationAccount", "Destination Cash Account", "fmg_CT_DestinationAccount", true);
        Lookup(svc, "fmg_cashposting", "fmg_cashaccount", "fmg_CashAccount", "Cash Account", "fmg_CP_Account", true);
        Lookup(svc, "fmg_accountopening", "fmg_cashaccount", "fmg_CashAccount", "Cash Account", "fmg_AO_Account", true);
        Lookup(svc, "fmg_bankstatement", "fmg_cashaccount", "fmg_CashAccount", "Cash Account", "fmg_BS_Account", true);
        Lookup(svc, "fmg_bankreconciliation", "fmg_cashaccount", "fmg_CashAccount", "Cash Account", "fmg_BR_Account", true);
        Lookup(svc, "fmg_bankreconciliation", "fmg_bankstatement", "fmg_BankStatement", "Bank Statement", "fmg_BR_Statement", true);
        Console.WriteLine("F5_SCHEMA_CREATED_DRAFT_ONLY_NOT_TRANSACTIONALLY_ACTIVE");
        return 0;
    }
}
