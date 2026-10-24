using System;
using System.Linq;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class BudgetApprovalProvisioning
{
    private const string Solution = "FinancialManager";
    private const string Table = "fmg_budgetapprovalclaim";
    private const string KeySchema = "fmg_BAC_AnnualScopeUnique";
    private const string Role = "Financial Manager Budget Approver";
    private static Label Label(string value) => new Label(value, 3082);

    public static int Run(ServiceClient service, string mode)
    {
        if (mode == "budget-approval-schema")
        {
            ProvisionTableAndKey(service);
            return 0;
        }
        if (mode == "budget-approval-role")
        {
            ProvisionUnassignedRole(service);
            return 0;
        }
        throw new InvalidOperationException("Unsupported budget approval provisioning mode.");
    }

    private static EntityMetadata? ReadTable(ServiceClient svc)
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
        catch (FaultException<OrganizationServiceFault> ex)
        {
            var message = (ex.Detail?.Message ?? ex.Message).ToLowerInvariant();
            if (ex.Detail?.ErrorCode == unchecked((int)0x80040217) ||
                message.Contains("not found") || message.Contains("does not exist") ||
                message.Contains("could not find") || message.Contains("no se encontró"))
                return null;
            throw;
        }
    }

    private static void ProvisionTableAndKey(ServiceClient svc)
    {
        var existing = ReadTable(svc);
        if (existing == null)
        {
            var entity = new EntityMetadata
            {
                SchemaName = "fmg_BudgetApprovalClaim",
                DisplayName = Label("Budget Approval Claim"),
                DisplayCollectionName = Label("Budget Approval Claims"),
                OwnershipType = OwnershipTypes.OrganizationOwned,
                IsAuditEnabled = new BooleanManagedProperty(true)
            };
            var primary = new StringAttributeMetadata
            {
                SchemaName = "fmg_Name",
                DisplayName = Label("Name"),
                RequiredLevel = new AttributeRequiredLevelManagedProperty(
                    AttributeRequiredLevel.ApplicationRequired),
                MaxLength = 200
            };
            svc.Execute(new CreateEntityRequest
            {
                Entity = entity, PrimaryAttribute = primary,
                SolutionUniqueName = Solution
            });
            Console.WriteLine("CREATED_APPROVAL_CLAIM_TABLE");
        }

        existing = ReadTable(svc);
        if (existing == null)
            throw new InvalidOperationException("Claim table unavailable after create.");
        if (!existing.Attributes.Any(a => a.LogicalName == "fmg_scopekey"))
        {
            svc.Execute(new CreateAttributeRequest
            {
                EntityName = Table,
                Attribute = new StringAttributeMetadata
                {
                    SchemaName = "fmg_ScopeKey",
                    DisplayName = Label("Annual scope key"),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(
                        AttributeRequiredLevel.ApplicationRequired),
                    MaxLength = 100
                },
                SolutionUniqueName = Solution
            });
            Console.WriteLine("CREATED_APPROVAL_SCOPE_KEY_COLUMN");
        }

        existing = ReadTable(svc);
        if (existing == null)
            throw new InvalidOperationException("Claim table metadata missing.");
        if (!existing.Attributes.Any(a => a.LogicalName == "fmg_budget"))
        {
            svc.Execute(new CreateOneToManyRequest
            {
                OneToManyRelationship = new OneToManyRelationshipMetadata
                {
                    SchemaName = "fmg_BAC_Budget",
                    ReferencedEntity = "fmg_budget",
                    ReferencingEntity = Table,
                    CascadeConfiguration = new CascadeConfiguration
                    {
                        Assign = CascadeType.NoCascade,
                        Delete = CascadeType.Restrict,
                        Merge = CascadeType.NoCascade,
                        Reparent = CascadeType.NoCascade,
                        Share = CascadeType.NoCascade,
                        Unshare = CascadeType.NoCascade
                    }
                },
                Lookup = new LookupAttributeMetadata
                {
                    SchemaName = "fmg_Budget",
                    DisplayName = Label("Budget"),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(
                        AttributeRequiredLevel.ApplicationRequired)
                },
                SolutionUniqueName = Solution
            });
            Console.WriteLine("CREATED_APPROVAL_CLAIM_BUDGET_LOOKUP");
        }

        svc.Execute(new PublishXmlRequest
        {
            ParameterXml = "<importexportxml><entities><entity>" + Table +
                           "</entity></entities></importexportxml>"
        });
        existing = ReadTable(svc);
        if (existing == null)
            throw new InvalidOperationException("Claim table metadata missing before key creation.");
        var key = existing.Keys?.SingleOrDefault(k => k.SchemaName == KeySchema);
        if (key == null)
        {
            svc.Execute(new CreateEntityKeyRequest
            {
                EntityName = Table,
                EntityKey = new EntityKeyMetadata
                {
                    SchemaName = KeySchema,
                    DisplayName = Label("One approval per annual scope"),
                    KeyAttributes = new[] { "fmg_scopekey" }
                },
                SolutionUniqueName = Solution
            });
            Console.WriteLine("CREATED_SCOPE_APPROVAL_UNIQUE_KEY");
        }
        else
        {
            Console.WriteLine("SCOPE_APPROVAL_KEY_STATUS=" + key.EntityKeyIndexStatus);
        }
        Console.WriteLine("CLAIM_SCHEMA_PROVISIONED_APPROVAL_STILL_DISABLED");
    }

    private static void ProvisionUnassignedRole(ServiceClient svc)
    {
        var roles = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"),
            TopCount = 100
        };
        roles.Criteria.AddCondition("name", ConditionOperator.Equal, Role);
        var found = svc.RetrieveMultiple(roles).Entities;
        if (found.Count > 0)
        {
            Console.WriteLine("APPROVER_ROLE_EXISTS_NO_ASSIGNMENT_CHANGED");
            return;
        }
        var root = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid"),
            TopCount = 2
        };
        root.Criteria.AddCondition(
            "parentbusinessunitid", ConditionOperator.Null);
        var roots = svc.RetrieveMultiple(root).Entities;
        if (roots.Count != 1)
            throw new InvalidOperationException("Expected one root business unit.");

        var role = new Entity("role");
        role["name"] = Role;
        role["businessunitid"] = new EntityReference("businessunit", roots[0].Id);
        svc.Execute(new CreateRequest
        {
            Target = role,
            Parameters = { ["SolutionUniqueName"] = Solution }
        });
        Console.WriteLine("CREATED_UNASSIGNED_APPROVER_ROLE_PLATFORM_DEFAULT_PRIVILEGES_REQUIRE_REVIEW");
    }
}
