using System;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// Prepares an unassigned reviewer role. This is not approval authority:
/// a separate authenticated and transactional Custom API is required.
/// </summary>
internal static class Fm11ReviewReadiness
{
    private const string RoleName = "Financial Manager Intake Reviewer";
    private const string Solution = "FinancialManager";

    public static int Run(ServiceClient client, bool apply)
    {
        var q = new QueryExpression("role") {
            ColumnSet = new ColumnSet("roleid","name","businessunitid"),
            TopCount = 10
        };
        q.Criteria.AddCondition("name", ConditionOperator.Equal, RoleName);
        var roles = client.RetrieveMultiple(q).Entities;
        if (roles.Count > 1)
            throw new InvalidOperationException("FM11 reviewer role is ambiguous.");
        if (roles.Count == 0 && apply)
        {
            var bu = new QueryExpression("businessunit") {
                ColumnSet = new ColumnSet("businessunitid"), TopCount = 2
            };
            bu.Criteria.AddCondition("parentbusinessunitid", ConditionOperator.Null);
            var roots = client.RetrieveMultiple(bu).Entities;
            if (roots.Count != 1)
                throw new InvalidOperationException("Expected exactly one root business unit.");
            var role = new Entity("role") {
                ["name"] = RoleName,
                ["businessunitid"] = new EntityReference("businessunit", roots[0].Id)
            };
            var req = new CreateRequest { Target = role };
            req.Parameters["SolutionUniqueName"] = Solution;
            client.Execute(req);
            roles = client.RetrieveMultiple(q).Entities;
        }
        if (roles.Count != 1)
        {
            Console.WriteLine("FM11_REVIEW_ROLE_PRESENT=false");
            Console.WriteLine("FM11_REVIEW_APPROVAL_ENABLED=false");
            return 0;
        }

        foreach (var target in new [] { "systemuserroles", "teamroles" })
        {
            var check = new QueryExpression("role") {
                ColumnSet = new ColumnSet(false), TopCount = 2
            };
            check.Criteria.AddCondition("roleid", ConditionOperator.Equal, roles[0].Id);
            check.AddLink(target,"roleid","roleid");
            var count = client.RetrieveMultiple(check).Entities.Count;
            Console.WriteLine("FM11_REVIEW_ROLE_" + target.ToUpperInvariant() + "_ASSIGNED=" + (count > 0));
            if (apply && count != 0)
                throw new InvalidOperationException("Do not assign reviewer role automatically.");
        }
        Console.WriteLine("FM11_REVIEW_ROLE_PRESENT=true");
        Console.WriteLine("FM11_REVIEW_APPROVAL_ENABLED=false");
        Console.WriteLine("FM11_REVIEW_FINANCIAL_WRITES=0");
        return 0;
    }
}
