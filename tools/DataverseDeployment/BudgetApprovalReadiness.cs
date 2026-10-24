using System;
using System.Linq;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class BudgetApprovalReadiness
{
    // Approval acceptance is a separate, evidenced release gate.
    // Never turn these on merely because a finance role was assigned.
    // Change only after recorded, independently authenticated two-user and
    // simultaneous competing approval tests in a disposable development environment.
    private const bool TwoIdentityAcceptanceRecorded = false;
    private const bool ConcurrencyAcceptanceRecorded = false;
    // Negative-only invocation with a random nonexistent budget ID.
    // No financial row can be approved; the current identity must lack the
    // explicit role. A BAP003 fault confirms the main plug-in rejects it.
    public static int ProbeUnauthorized(ServiceClient service)
    {
        var who = (WhoAmIResponse)service.Execute(new WhoAmIRequest());
        var roleQuery = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet(false), TopCount = 1
        };
        roleQuery.Criteria.AddCondition("name", ConditionOperator.Equal,
            "Financial Manager Budget Approver");
        var members = roleQuery.AddLink("systemuserroles", "roleid", "roleid");
        members.LinkCriteria.AddCondition(
            "systemuserid", ConditionOperator.Equal, who.UserId);
        if (service.RetrieveMultiple(roleQuery).Entities.Count != 0)
            throw new InvalidOperationException(
                "Refusing unauthorized probe: current identity has approver role.");

        var request = new OrganizationRequest("fmg_ApproveBudget");
        request["Target"] = new EntityReference("fmg_budget", Guid.NewGuid());
        try
        {
            service.Execute(request);
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            if (ex.ToString().Contains("BAP003"))
            {
                Console.WriteLine("PASS_UNAUTHORIZED_APPROVAL_API_REJECTED_BAP003");
                return 0;
            }
            Console.WriteLine("UNEXPECTED_APPROVAL_API_FAULT=" +
                ex.Detail?.ErrorCode.ToString());
            throw;
        }
        throw new InvalidOperationException(
            "UNSAFE: API invocation unexpectedly succeeded with an unassigned approver.");
    }

    public static int Inspect(ServiceClient service)
    {
        var roles = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"),
            TopCount = 100
        };
        roles.Criteria.AddCondition(
            "name", ConditionOperator.Equal, "Financial Manager Budget Approver");
        var foundRoles = service.RetrieveMultiple(roles).Entities;
        Console.WriteLine("EXPLICIT_APPROVER_ROLES=" + foundRoles.Count);
        bool leastPrivilegeRole = false;
        if (foundRoles.Count == 1)
        {
            var grants = (RetrieveRolePrivilegesRoleResponse)service.Execute(
                new RetrieveRolePrivilegesRoleRequest { RoleId = foundRoles[0].Id });
            var granted = grants.RolePrivileges ?? Array.Empty<RolePrivilege>();
            var ids = granted.Select(p => (object)p.PrivilegeId).ToArray();
            Console.WriteLine("EXPLICIT_APPROVER_ROLE_PRIVILEGE_COUNT=" + ids.Length);
            if (ids.Length > 0)
            {
                var listed = new QueryExpression("privilege")
                {
                    ColumnSet = new ColumnSet("name"), TopCount = ids.Length + 2
                };
                listed.Criteria.AddCondition("privilegeid", ConditionOperator.In, ids);
                var names = service.RetrieveMultiple(listed).Entities
                    .Select(e => e.GetAttributeValue<string>("name")).ToArray();
                Console.WriteLine("APPROVER_ROLE_SHAREPOINT_PRIVILEGES=" +
                    names.Count(n => n.IndexOf("SharePoint", StringComparison.OrdinalIgnoreCase) >= 0));
                Console.WriteLine("APPROVER_ROLE_REQUIRED_API_PRIVILEGE=" +
                    names.Contains("prvCreatefmg_BudgetApprovalClaim"));
                leastPrivilegeRole = names.Length == ApprovalRolePrivileges.Allowed.Length &&
                    !ApprovalRolePrivileges.Allowed.Except(names, StringComparer.Ordinal).Any();
                Console.WriteLine("APPROVER_ROLE_EXACT_MINIMUM_PRIVILEGES=" + leastPrivilegeRole);
            }
        }
        var assignedDirect = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 100
        };
        assignedDirect.Criteria.AddCondition(
            "name", ConditionOperator.Equal, "Financial Manager Budget Approver");
        assignedDirect.AddLink("systemuserroles", "roleid", "roleid");
        var directAssignments = service.RetrieveMultiple(assignedDirect).Entities.Count;
        Console.WriteLine("DIRECT_APPROVER_ROLE_ASSIGNMENTS=" + directAssignments);
        var assignedTeam = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 100
        };
        assignedTeam.Criteria.AddCondition(
            "name", ConditionOperator.Equal, "Financial Manager Budget Approver");
        assignedTeam.AddLink("teamroles", "roleid", "roleid");
        var teamAssignments = service.RetrieveMultiple(assignedTeam).Entities.Count;
        Console.WriteLine("TEAM_APPROVER_ROLE_ASSIGNMENTS=" + teamAssignments);

        var claimPrivileges = new QueryExpression("privilege")
        {
            ColumnSet = new ColumnSet("name"), TopCount = 50
        };
        claimPrivileges.Criteria.AddCondition("name", ConditionOperator.Like,
            "%fmg_BudgetApprovalClaim%");
        foreach (var entry in service.RetrieveMultiple(claimPrivileges).Entities)
            Console.WriteLine("CLAIM_PRIVILEGE=" + entry.GetAttributeValue<string>("name"));

        var api = new QueryExpression("customapi")
        {
            ColumnSet = new ColumnSet("customapiid", "bindingtype",
                "boundentitylogicalname", "executeprivilegename", "plugintypeid",
                "allowedcustomprocessingsteptype", "isfunction", "isprivate"),
            TopCount = 2
        };
        api.Criteria.AddCondition(
            "uniquename", ConditionOperator.Equal, "fmg_ApproveBudget");
        var configuredApis = service.RetrieveMultiple(api).Entities;
        Console.WriteLine("REGISTERED_APPROVAL_CUSTOM_APIS=" + configuredApis.Count);
        var validApi = configuredApis.Count == 1 &&
            configuredApis[0].GetAttributeValue<OptionSetValue>("bindingtype")?.Value == 1 &&
            configuredApis[0].GetAttributeValue<string>("boundentitylogicalname") == "fmg_budget" &&
            configuredApis[0].GetAttributeValue<string>("executeprivilegename") ==
                "prvCreatefmg_BudgetApprovalClaim" &&
            configuredApis[0].GetAttributeValue<OptionSetValue>(
                "allowedcustomprocessingsteptype")?.Value == 0 &&
            configuredApis[0].GetAttributeValue<EntityReference>("plugintypeid") != null &&
            !configuredApis[0].GetAttributeValue<bool>("isfunction") &&
            configuredApis[0].GetAttributeValue<bool>("isprivate");
        Console.WriteLine("APPROVAL_CUSTOM_API_SECURED_AND_BOUND=" + validApi);

        bool hasClaimTable;
        bool hasScopeKey;
        bool keyActive;
        try
        {
            var result = (RetrieveEntityResponse)service.Execute(
                new RetrieveEntityRequest
                {
                    LogicalName = "fmg_budgetapprovalclaim",
                    EntityFilters = EntityFilters.Attributes | EntityFilters.Entity,
                    RetrieveAsIfPublished = true
                });
            hasClaimTable = result.EntityMetadata != null;
            hasScopeKey = result.EntityMetadata?.Attributes?.Any(
                a => a.LogicalName == "fmg_scopekey") == true;
            var keyResult = (RetrieveEntityResponse)service.Execute(
                new RetrieveEntityRequest
                {
                    LogicalName = "fmg_budgetapprovalclaim",
                    EntityFilters = EntityFilters.All,
                    RetrieveAsIfPublished = true
                });
            keyActive = keyResult.EntityMetadata?.Keys?.Any(
                k => k.KeyAttributes.Contains("fmg_scopekey") &&
                     k.EntityKeyIndexStatus.ToString() == "Active") == true;
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            var detail = (ex.Detail?.Message ?? ex.Message).ToLowerInvariant();
            if (ex.Detail?.ErrorCode != unchecked((int)0x80040217) &&
                !detail.Contains("does not exist") && !detail.Contains("not found") &&
                !detail.Contains("could not find"))
                throw;
            hasClaimTable = false;
            hasScopeKey = false;
            keyActive = false;
        }

        Console.WriteLine("APPROVAL_CLAIM_TABLE=" + hasClaimTable);
        Console.WriteLine("APPROVAL_CLAIM_KEY_COLUMN=" + hasScopeKey);
        Console.WriteLine("APPROVAL_CLAIM_ALT_KEY_ACTIVE=" + keyActive);
        var infrastructureReady = foundRoles.Count == 1 && hasClaimTable &&
            hasScopeKey && keyActive && validApi;
        var roleAssignmentReady = directAssignments > 0 && teamAssignments == 0;
        Console.WriteLine("APPROVAL_INFRASTRUCTURE_READY=" + infrastructureReady);
        Console.WriteLine("APPROVAL_ROLE_ASSIGNMENT_PRESENT=" + roleAssignmentReady);
        Console.WriteLine("APPROVAL_TWO_IDENTITY_ACCEPTED=" + TwoIdentityAcceptanceRecorded);
        Console.WriteLine("APPROVAL_CONCURRENCY_ACCEPTED=" + ConcurrencyAcceptanceRecorded);
        Console.WriteLine("APPROVAL_READY=" + (infrastructureReady && leastPrivilegeRole &&
            roleAssignmentReady && TwoIdentityAcceptanceRecorded && ConcurrencyAcceptanceRecorded));
        // Return success for read-only diagnostics even when intentionally
        // unconfigured. Never create a role, table or API from an inspect command.
        return 0;
    }
}
