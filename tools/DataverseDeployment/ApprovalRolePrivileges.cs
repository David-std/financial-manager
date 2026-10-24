using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static class ApprovalRolePrivileges
{
    private const string RoleName = "Financial Manager Budget Approver";

    // Creation of the approval claim is also the API execute privilege.
    // No Delete, BudgetAllocation write, or ActivityEvent write is granted.
    private static readonly string[] Allowed =
    {
        "prvReadfmg_Budget",
        "prvWritefmg_Budget",
        "prvAppendTofmg_Budget",
        "prvReadfmg_BudgetAllocation",
        "prvCreatefmg_BudgetApprovalClaim",
        "prvAppendfmg_BudgetApprovalClaim",
        "prvReadfmg_BudgetApprovalClaim",
        "prvReadRole",
        "prvReadUser"
    };

    public static int Apply(ServiceClient service)
    {
        var roles = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"), TopCount = 2
        };
        roles.Criteria.AddCondition("name", ConditionOperator.Equal, RoleName);
        var result = service.RetrieveMultiple(roles).Entities;
        if (result.Count != 1)
            throw new InvalidOperationException("Expected a single independent approver role.");
        var roleId = result[0].Id;

        foreach (var relation in new[] { "systemuserroles", "teamroles" })
        {
            var assigned = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet(false), TopCount = 1
            };
            assigned.Criteria.AddCondition("roleid", ConditionOperator.Equal, roleId);
            assigned.AddLink(relation, "roleid", "roleid");
            if (service.RetrieveMultiple(assigned).Entities.Count != 0)
                throw new InvalidOperationException("Refusing to change a role already assigned via " + relation);
        }

        var query = new QueryExpression("privilege")
        {
            ColumnSet = new ColumnSet("privilegeid", "name"),
            TopCount = Allowed.Length + 5
        };
        query.Criteria.AddCondition("name", ConditionOperator.In,
            Allowed.Cast<object>().ToArray());
        var found = service.RetrieveMultiple(query).Entities;
        var names = found.Select(e => e.GetAttributeValue<string>("name")).ToArray();
        var missing = Allowed.Where(n => !names.Contains(n)).ToArray();
        if (found.Count != Allowed.Length || missing.Length > 0)
            throw new InvalidOperationException("Required approver privileges missing: " +
                String.Join(", ", missing));
        var privileges = found.Select(p => new RolePrivilege(
            (int)PrivilegeDepth.Global, p.GetAttributeValue<Guid>("privilegeid")))
            .ToArray();

        service.Execute(new ReplacePrivilegesRoleRequest
        {
            RoleId = roleId,
            Privileges = privileges
        });
        Console.WriteLine("APPROVER_ROLE_EXPLICIT_PRIVILEGES_APPLIED=" + privileges.Length);
        Console.WriteLine("PLATFORM_DEFAULT_PRIVILEGES_REQUIRE_POST_EXPORT_REVIEW");
        Console.WriteLine("NO_USERS_OR_TEAMS_ASSIGNED");
        return 0;
    }
}
