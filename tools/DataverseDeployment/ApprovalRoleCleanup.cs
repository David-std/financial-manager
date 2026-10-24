using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

// Privilege pruning for an UNASSIGNED development role only.
// It does not grant role memberships, edit financial rows, or invoke approval APIs.
internal static class ApprovalRoleCleanup
{
    private const string Name = "Financial Manager Budget Approver";
    private static readonly string[] PlatformDefaults =
    {
        "prvCreateSharePointData", "prvReadSharePointData",
        "prvReadSharePointDocument", "prvWriteSharePointData"
    };

    public static int Run(ServiceClient service)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"), TopCount = 2
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, Name);
        var roles = service.RetrieveMultiple(query).Entities;
        if (roles.Count != 1) throw new InvalidOperationException("Expected exactly one approver role.");
        var roleId = roles[0].Id;
        EnsureUnassigned(service, roleId);
        var current = (RetrieveRolePrivilegesRoleResponse)service.Execute(
            new RetrieveRolePrivilegesRoleRequest { RoleId = roleId });
        var grants = current.RolePrivileges ?? Array.Empty<RolePrivilege>();
        if (grants.Length != 13)
            throw new InvalidOperationException("Refusing unexpected current role privilege count.");

        var privileges = new QueryExpression("privilege")
        {
            ColumnSet = new ColumnSet("name", "privilegeid"), TopCount = 20
        };
        privileges.Criteria.AddCondition("privilegeid", ConditionOperator.In,
            grants.Select(g => (object)g.PrivilegeId).ToArray());
        var found = service.RetrieveMultiple(privileges).Entities;
        var byId = found.ToDictionary(e => e.Id,
            e => e.GetAttributeValue<string>("name") ?? "");
        if (byId.Count != grants.Length || grants.Any(g => !byId.ContainsKey(g.PrivilegeId)))
            throw new InvalidOperationException("Cannot uniquely identify every current grant.");

        var expected = ApprovalRolePrivileges.Allowed.Concat(PlatformDefaults).ToArray();
        var existing = grants.Select(g => byId[g.PrivilegeId]).ToArray();
        if (existing.Distinct(StringComparer.Ordinal).Count() != expected.Length ||
            expected.Except(existing, StringComparer.Ordinal).Any() ||
            existing.Except(expected, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("Role has unexpected privileges; no changes.");

        // Recoverable local snapshot, kept outside the public git checkout.
        var dir = Path.Combine(Path.GetTempPath(), "FinancialManager", "role-privilege-backups");
        Directory.CreateDirectory(dir);
        var backup = Path.Combine(dir, "approver-role-" + roleId.ToString("N") +
            "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".tsv");
        var lines = new[] { "roleId\tprivilegeId\tdepth\tname" }.Concat(grants.Select(g =>
            roleId + "\t" + g.PrivilegeId + "\t" + g.Depth + "\t" + byId[g.PrivilegeId]));
        File.WriteAllLines(backup, lines);
        if (!File.Exists(backup) || File.ReadAllLines(backup).Length != 14)
            throw new InvalidOperationException("Could not verify local role backup.");
        Console.WriteLine("ROLE_BACKUP_PATH=" + backup);

        foreach (var removedName in PlatformDefaults)
        {
            EnsureUnassigned(service, roleId);
            var grant = grants.Single(g => byId[g.PrivilegeId] == removedName);
            // Never retry a timed-out write without re-inspecting role membership and grants.
            service.Execute(new RemovePrivilegeRoleRequest
            {
                RoleId = roleId, PrivilegeId = grant.PrivilegeId
            });
            var after = (RetrieveRolePrivilegesRoleResponse)service.Execute(
                new RetrieveRolePrivilegesRoleRequest { RoleId = roleId });
            if ((after.RolePrivileges ?? Array.Empty<RolePrivilege>())
                .Any(g => g.PrivilegeId == grant.PrivilegeId))
                throw new InvalidOperationException("Role default still present after removal: " +
                    removedName + "; do not assign the role.");
            Console.WriteLine("ROLE_PRIVILEGE_REMOVED=" + removedName);
        }

        var verified = (RetrieveRolePrivilegesRoleResponse)service.Execute(
            new RetrieveRolePrivilegesRoleRequest { RoleId = roleId });
        var remaining = (verified.RolePrivileges ?? Array.Empty<RolePrivilege>())
            .Select(g => byId.TryGetValue(g.PrivilegeId, out var name) ? name : "")
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        if (!remaining.SequenceEqual(
            ApprovalRolePrivileges.Allowed.OrderBy(s => s, StringComparer.Ordinal),
            StringComparer.Ordinal))
            throw new InvalidOperationException("Post-removal privilege contract mismatch.");
        EnsureUnassigned(service, roleId);
        Console.WriteLine("APPROVER_ROLE_EXACT_MINIMUM_PRIVILEGES=True");
        Console.WriteLine("ROLE_UNASSIGNED_APPROVAL_STILL_DISABLED");
        return 0;
    }

    private static void EnsureUnassigned(ServiceClient service, Guid roleId)
    {
        foreach (var linkName in new[] { "systemuserroles", "teamroles" })
        {
            var query = new QueryExpression("role") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition("roleid", ConditionOperator.Equal, roleId);
            query.AddLink(linkName, "roleid", "roleid");
            if (service.RetrieveMultiple(query).Entities.Count != 0)
                throw new InvalidOperationException("Approver role is assigned through " +
                    linkName + ". Refusing cleanup.");
        }
    }
}
