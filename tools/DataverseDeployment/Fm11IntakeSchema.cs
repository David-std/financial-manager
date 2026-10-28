using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;

/// <summary>
/// Untrusted inbound email storage. Never creates a financial transaction.
/// A unique request ID is enforced by Dataverse, not a pre-create query.
/// </summary>
internal static class Fm11IntakeSchema
{
    private const string Table = "fmg_mailintake";
    private const string EntitySchema = "fmg_MailIntake";
    private const string KeyName = "fmg_MI_RequestId";
    private const string Solution = "FinancialManager";
    private static Label L(string s) => new Label(s, 3082);

    private static EntityMetadata Read(ServiceClient service)
    {
        var response = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest {
            LogicalName = Table,
            EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true,
        });
        return response.EntityMetadata;
    }

    public static int ProbeSyntheticDeduplication(ServiceClient service)
    {
        var md = Read(service);
        var key = md.Keys?.SingleOrDefault(k =>
            string.Equals(k.SchemaName, KeyName, StringComparison.OrdinalIgnoreCase));
        if (key?.EntityKeyIndexStatus != EntityKeyIndexStatus.Active ||
            md.EntitySetName != "fmg_mailintakes")
            throw new InvalidOperationException("FM11 unique request key not Active.");

        var requestId = "FM11-SYNTHETIC-" + Guid.NewGuid().ToString("N");
        var row = new Entity(Table) {
            ["fmg_name"] = "Synthetic intake deduplication test",
            ["fmg_requestid"] = requestId,
            ["fmg_sender"] = "test@example.invalid",
            ["fmg_mailmessageid"] = "test-only",
            ["fmg_reviewstate"] = "Unverified",
            ["fmg_rawbody"] = "{\"synthetic\":true}",
        };
        Guid first = Guid.Empty;
        try
        {
            first = service.Create(row);
            if (first == Guid.Empty)
                throw new InvalidOperationException("Synthetic intake insert failed.");

            bool rejectedAsDuplicate = false;
            Guid unexpected = Guid.Empty;
            try
            {
                unexpected = service.Create(row);
                if (unexpected == Guid.Empty)
                {
                    var failure = service.LastError ?? "";
                    rejectedAsDuplicate =
                        failure.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                        failure.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                        failure.Contains("duplicad", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) when (
                ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("duplicad", StringComparison.OrdinalIgnoreCase))
            {
                rejectedAsDuplicate = true;
            }
            if (unexpected != Guid.Empty)
            {
                service.Delete(Table, unexpected);
                throw new InvalidOperationException("CRITICAL: duplicate request was inserted.");
            }
            if (!rejectedAsDuplicate)
                throw new InvalidOperationException("Duplicate rejection not independently confirmed.");
            Console.WriteLine("FM11_MAIL_INTAKE_DUPLICATE_REJECTED=true");
        }
        finally
        {
            if (first != Guid.Empty) service.Delete(Table, first);
        }

        var q = new Microsoft.Xrm.Sdk.Query.QueryExpression(Table) {
            ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
            TopCount = 2
        };
        q.Criteria.AddCondition("fmg_requestid",
            Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, requestId);
        if (service.RetrieveMultiple(q).Entities.Count != 0)
            throw new InvalidOperationException("Synthetic intake cleanup not confirmed.");
        Console.WriteLine("FM11_MAIL_INTAKE_SYNTHETIC_CLEANUP_VERIFIED=true");
        Console.WriteLine("FM11_MAIL_INTAKE_FINANCIAL_WRITES=0");
        return 0;
    }

    public static int Run(ServiceClient service, bool apply)
    {
        EntityMetadata? md = null;
        try
        {
            md = Read(service);
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
        {
            var msg = (ex.Detail?.Message ?? ex.Message).ToLowerInvariant();
            if (!(msg.Contains("not found") || msg.Contains("not exist") ||
                  msg.Contains("could not find") || msg.Contains("does not contain") ||
                  msg.Contains("no se encontr")))
                throw;
        }

        if (md == null && !apply)
        {
            Console.WriteLine("FM11_MAIL_INTAKE_TABLE_MISSING=true");
            return 0;
        }
        if (md == null)
        {
            service.Execute(new CreateEntityRequest {
                Entity = new EntityMetadata {
                    SchemaName = EntitySchema,
                    DisplayName = L("Mail Intake"),
                    DisplayCollectionName = L("Mail Intakes"),
                    Description = L("Unverified received email requests; not approved expenses."),
                    OwnershipType = OwnershipTypes.UserOwned,
                    IsActivity = false,
                    IsAuditEnabled = new BooleanManagedProperty(true),
                },
                PrimaryAttribute = new StringAttributeMetadata {
                    SchemaName = "fmg_Name", DisplayName = L("Name"),
                    MaxLength = 160,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(
                        AttributeRequiredLevel.ApplicationRequired),
                },
                SolutionUniqueName = Solution,
            });
            md = Read(service);
            Console.WriteLine("FM11_MAIL_INTAKE_TABLE_CREATED=true");
        }

        if (!string.Equals(md.LogicalName, Table, StringComparison.Ordinal))
            throw new InvalidOperationException("Wrong intake table.");
        if (md.IsAuditEnabled?.Value != true ||
            md.OwnershipType != OwnershipTypes.UserOwned)
            throw new InvalidOperationException("Intake must be audited and user-owned.");

        void Ensure(StringAttributeMetadata attr)
        {
            var col = md.Attributes.SingleOrDefault(
                a => a.LogicalName == attr.SchemaName.ToLowerInvariant());
            if (col != null)
            {
                if (col is not StringAttributeMetadata actual ||
                    actual.MaxLength < attr.MaxLength)
                    throw new InvalidOperationException("Unexpected intake column: " + attr.SchemaName);
                return;
            }
            if (apply) service.Execute(new CreateAttributeRequest {
                EntityName = Table, Attribute = attr, SolutionUniqueName = Solution,
            });
            else Console.WriteLine("FM11_MISSING_COLUMN=" + attr.SchemaName);
        }
        StringAttributeMetadata Text(string schema, int length) => new StringAttributeMetadata {
            SchemaName = schema, DisplayName = L(schema),
            MaxLength = length,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
        };
        Ensure(Text("fmg_RequestId", 80));
        Ensure(Text("fmg_Sender", 320));
        Ensure(Text("fmg_MailMessageId", 500));
        Ensure(Text("fmg_ReviewState", 40));

        var raw = md.Attributes.SingleOrDefault(a => a.LogicalName == "fmg_rawbody");
        if (raw is not MemoAttributeMetadata && raw != null)
            throw new InvalidOperationException("Unexpected raw body type.");
        if (raw == null && apply)
        {
            service.Execute(new CreateAttributeRequest {
                EntityName = Table, SolutionUniqueName = Solution,
                Attribute = new MemoAttributeMetadata {
                    SchemaName = "fmg_RawBody", DisplayName = L("Original email body"),
                    MaxLength = 15000,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
                },
            });
        }
        else if (raw == null)
            Console.WriteLine("FM11_MISSING_COLUMN=fmg_RawBody");

        md = Read(service);
        var key = md.Keys?.SingleOrDefault(k =>
            string.Equals(k.SchemaName, KeyName, StringComparison.OrdinalIgnoreCase));
        if (key == null && apply)
        {
            service.Execute(new CreateEntityKeyRequest {
                EntityName = Table,
                EntityKey = new EntityKeyMetadata {
                    SchemaName = KeyName,
                    DisplayName = L("Unique FM11 request"),
                    KeyAttributes = new [] { "fmg_requestid" }
                }
            });
            md = Read(service);
            key = md.Keys?.SingleOrDefault(k =>
                string.Equals(k.SchemaName, KeyName, StringComparison.OrdinalIgnoreCase));
        }
        if (key != null && (!key.KeyAttributes.SequenceEqual(
                new [] { "fmg_requestid" }, StringComparer.OrdinalIgnoreCase) ||
            key.EntityKeyIndexStatus == EntityKeyIndexStatus.Failed))
            throw new InvalidOperationException("Invalid FM11 idempotency key.");
        Console.WriteLine("FM11_MAIL_INTAKE_ENTITY_SET=" + md.EntitySetName);
        Console.WriteLine("FM11_MAIL_INTAKE_AUDIT_ENABLED=true");
        Console.WriteLine("FM11_MAIL_INTAKE_KEY_STATUS=" +
            (key?.EntityKeyIndexStatus.ToString() ?? "MISSING"));
        Console.WriteLine("FM11_MAIL_INTAKE_FINANCIAL_WRITES=0");
        return 0;
    }
}
