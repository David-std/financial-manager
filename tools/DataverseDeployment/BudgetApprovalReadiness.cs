using System;
using System.Linq;
using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class BudgetApprovalReadiness
{
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

        var api = new QueryExpression("customapi")
        {
            ColumnSet = new ColumnSet("customapiid"),
            TopCount = 2
        };
        api.Criteria.AddCondition(
            "uniquename", ConditionOperator.Equal, "fmg_ApproveBudget");
        Console.WriteLine("REGISTERED_APPROVAL_CUSTOM_APIS=" +
            service.RetrieveMultiple(api).Entities.Count);

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
        Console.WriteLine("APPROVAL_READY=" + (
            foundRoles.Count == 1 && directAssignments > 0 &&
            teamAssignments == 0 && hasClaimTable && hasScopeKey && keyActive &&
            service.RetrieveMultiple(api).Entities.Count == 1
        ));
        // Return success for read-only diagnostics even when intentionally
        // unconfigured. Never create a role, table or API from an inspect command.
        return 0;
    }
}
