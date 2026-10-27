using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static class CashGrantPrivilegeReadiness
{
    private static readonly string[] Expected =
    {
        "prvCreatefmg_CashGrantRequest",
        "prvWritefmg_CashGrantRequest",
        "prvReadfmg_CashGrantRequest",
        "prvCreatefmg_CashAccessGrant",
        "prvWritefmg_CashAccessGrant"
    };

    /// <summary>Inspect generated Dataverse privileges without role
    /// assignment, people lookup, or financial record mutation.</summary>
    public static int Inspect(ServiceClient service)
    {
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashGrantRequestGuardDeployment.Steps(service, false, true);
        var q = new QueryExpression("privilege")
        {
            ColumnSet = new ColumnSet("name", "privilegeid"), TopCount = 20
        };
        q.Criteria.AddCondition("name", ConditionOperator.In, Expected.Cast<object>().ToArray());
        var response = service.RetrieveMultiple(q);
        if (response == null || response.MoreRecords)
            throw new InvalidOperationException("Incomplete financial privilege metadata.");
        var names = response.Entities
            .Select(e => e.GetAttributeValue<string>("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        foreach (var name in Expected)
        {
            if (names.Count(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)) != 1)
                throw new InvalidOperationException("Missing/ambiguous generated privilege " + name);
            var actual = names.Single(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
            Console.WriteLine("F5_REVIEW_PRIVILEGE_PRESENT " + actual + " exact_case_match=" + string.Equals(actual, name, StringComparison.Ordinal));
        }
        Console.WriteLine("F5_REVIEW_PRIVILEGE_METADATA_VERIFIED_NO_ROLE_ASSIGNMENTS");
        return 0;
    }
}
