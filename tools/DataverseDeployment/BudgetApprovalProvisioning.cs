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
        if (mode == "budget-approval-api")
        {
            ProvisionGuardedApi(service);
            return 0;
        }
        if (mode == "budget-approval-role-permissions")
            return ApprovalRolePrivileges.Apply(service);
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

    private static void ProvisionGuardedApi(ServiceClient svc)
    {
        // Read-only safety preflight. No role assignment or budget mutation.
        var claim = ReadTable(svc) ??
            throw new InvalidOperationException("Approval claim table missing.");
        if (claim.Keys?.Count(k =>
                k.SchemaName == KeySchema &&
                k.EntityKeyIndexStatus.ToString() == "Active") != 1)
            throw new InvalidOperationException("Active unique claim key required.");

        foreach (var name in new[]
        {
            "BAC_BlockClaimCreate", "BAC_BlockClaimUpdate", "BAC_BlockClaimDelete"
        })
        {
            var check = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("stage", "mode", "statecode"), TopCount = 2
            };
            check.Criteria.AddCondition("name", ConditionOperator.Equal, name);
            var steps = svc.RetrieveMultiple(check).Entities;
            if (steps.Count != 1 ||
                steps[0].GetAttributeValue<OptionSetValue>("stage")?.Value != 10 ||
                steps[0].GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                steps[0].GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                throw new InvalidOperationException("Active synchronous claim guard missing: " + name);
        }

        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"), TopCount = 2
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, Role);
        var roles = svc.RetrieveMultiple(query).Entities;
        if (roles.Count != 1)
            throw new InvalidOperationException("Expected exactly one independent approver role.");
        var userLink = query.AddLink("systemuserroles", "roleid", "roleid");
        if (svc.RetrieveMultiple(query).Entities.Count > 0)
            throw new InvalidOperationException("Approver already assigned; independent test acceptance required.");
        query.LinkEntities.Clear();
        query.AddLink("teamroles", "roleid", "roleid");
        if (svc.RetrieveMultiple(query).Entities.Count > 0)
            throw new InvalidOperationException("Approver role assigned to a team; refusing API registration.");

        const string privilegeName = "prvCreatefmg_BudgetApprovalClaim";
        var permissions = new QueryExpression("privilege")
        {
            ColumnSet = new ColumnSet("privilegeid"), TopCount = 2
        };
        permissions.Criteria.AddCondition("name", ConditionOperator.Equal, privilegeName);
        if (svc.RetrieveMultiple(permissions).Entities.Count != 1)
            throw new InvalidOperationException("Required API execution privilege not found.");

        var typeQuery = new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet("plugintypeid"), TopCount = 2
        };
        typeQuery.Criteria.AddCondition("typename", ConditionOperator.Equal,
            "FinancialManager.BudgetIntegrity.BG_ApproveBudget");
        var types = svc.RetrieveMultiple(typeQuery).Entities;
        if (types.Count != 1)
            throw new InvalidOperationException("Budget API main operation type must be registered.");

        var apiQuery = new QueryExpression("customapi")
        {
            ColumnSet = new ColumnSet("customapiid", "plugintypeid",
                "bindingtype", "boundentitylogicalname", "executeprivilegename"),
            TopCount = 2
        };
        apiQuery.Criteria.AddCondition("uniquename", ConditionOperator.Equal, "fmg_ApproveBudget");
        var existing = svc.RetrieveMultiple(apiQuery).Entities;
        if (existing.Count > 1)
            throw new InvalidOperationException("Duplicate approval custom APIs.");
        if (existing.Count == 1)
        {
            var current = existing[0];
            var actual = current.GetAttributeValue<EntityReference>("plugintypeid");
            if (actual?.Id != types[0].Id ||
                current.GetAttributeValue<OptionSetValue>("bindingtype")?.Value != 1 ||
                current.GetAttributeValue<string>("boundentitylogicalname") != "fmg_budget" ||
                current.GetAttributeValue<string>("executeprivilegename") != privilegeName)
                throw new InvalidOperationException("Existing approval API differs from reviewed contract.");
            Console.WriteLine("EXISTING_SECURED_APPROVAL_API_NOT_CALLED");
            return;
        }

        var api = new Entity("customapi");
        api["name"] = "fmg_ApproveBudget";
        api["uniquename"] = "fmg_ApproveBudget";
        api["displayname"] = "Approve Budget (server controlled)";
        api["description"] = "Independent approval with unique annual scope and server verification.";
        api["bindingtype"] = new OptionSetValue(1);
        api["boundentitylogicalname"] = "fmg_budget";
        api["allowedcustomprocessingsteptype"] = new OptionSetValue(0);
        api["executeprivilegename"] = privilegeName;
        api["plugintypeid"] = new EntityReference("plugintype", types[0].Id);
        api["isfunction"] = false;
        api["isprivate"] = true;
        api["workflowsdkstepenabled"] = false;
        api["iscustomizable"] = new BooleanManagedProperty(false);
        svc.Execute(new CreateRequest
        {
            Target = api,
            Parameters = { ["SolutionUniqueName"] = Solution }
        });
        Console.WriteLine("CREATED_PRIVILEGE_GATED_BOUND_API_NO_APPROVER_ASSIGNED");
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
