using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.BudgetIntegrity
{
    /// <summary>
    /// Unregistered server-side approval operation. Its claim table and alternate key
    /// must exist and be ACTIVE before deployment. Do not call from a web client.
    /// A future dedicated custom API must execute this inside a Dataverse transaction.
    /// </summary>
    public sealed class BudgetApprovalOperation
    {
        private const string ApproverRole = "Financial Manager Budget Approver";
        private readonly IOrganizationService service;

        public BudgetApprovalOperation(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public void Execute(Guid budgetId, Guid actor, bool isInTransaction, DateTime utcNow)
        {
            if (!isInTransaction)
                Fail("BAP010", "An active Dataverse transaction is required.");

            if (budgetId == Guid.Empty || actor == Guid.Empty)
                Fail("BAP001", "A budget and initiating actor are required.");

            var budget = service.Retrieve(
                "fmg_budget", budgetId,
                new ColumnSet("fmg_status", "fmg_year", "fmg_version",
                    "fmg_effectivefrommonth", "fmg_scope", "fmg_branch",
                    "createdby", "ownerid"));

            // The explicit approver role is separate from System Administrator.
            bool hasRole = HasDirectApproverRole(actor);
            new BudgetApprovalPreflight().Validate(budget, actor, hasRole);

            if (String.IsNullOrWhiteSpace(budget.RowVersion))
                Fail("BAP011", "Optimistic concurrency row version is required.");

            var lines = new QueryExpression("fmg_budgetallocation")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1
            };
            lines.Criteria.AddCondition("fmg_budget", ConditionOperator.Equal, budgetId);
            if (service.RetrieveMultiple(lines).Entities.Count == 0)
                Fail("BAP012", "Approval requires at least one valid allocation.");

            var year = budget.GetAttributeValue<int>("fmg_year");
            var scope = budget.GetAttributeValue<OptionSetValue>("fmg_scope").Value;
            var branch = budget.GetAttributeValue<EntityReference>("fmg_branch")?.Id;
            var scopeKey = ApprovalScopeKey(year, scope, branch);

            // The UNIQUE ACTIVE alternate key on fmg_scopekey is the cross-record
            // race arbiter. Different submitted versions for the same annual scope
            // attempt to claim the SAME key. The loser fails; no double approval.
            // This must participate in the same Dataverse transaction as the update.
            var claim = new Entity("fmg_budgetapprovalclaim");
            claim["fmg_name"] = scopeKey;
            claim["fmg_scopekey"] = scopeKey;
            claim["fmg_budget"] = new EntityReference("fmg_budget", budgetId);
            service.Create(claim);

            var approved = new Entity("fmg_budget") { Id = budgetId };
            approved.RowVersion = budget.RowVersion;
            approved["fmg_status"] = new OptionSetValue(BudgetOptions.Approved);
            approved["fmg_approvedby"] = new EntityReference("systemuser", actor);
            approved["fmg_approvedon"] = utcNow.Kind == DateTimeKind.Utc
                ? utcNow : utcNow.ToUniversalTime();
            service.Execute(new UpdateRequest
            {
                Target = approved,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            });

            // No exception swallowing: any SDK fault must roll back BOTH writes.
            // This first release intentionally allows ONE approved annual budget
            // per year/scope. Superseding is NOT supported until separately designed.
        }

        public static string ApprovalScopeKey(int year, int scope, Guid? branch)
        {
            if (year < 1 || year > 9999 ||
                (scope != BudgetOptions.Branch && scope != BudgetOptions.Shared) ||
                (scope == BudgetOptions.Branch &&
                    (!branch.HasValue || branch.Value == Guid.Empty)) ||
                (scope == BudgetOptions.Shared && branch.HasValue))
                Fail("BAP006", "Invalid annual approval scope.");

            return year.ToString("D4") + ":" + scope + ":" +
                (branch.HasValue ? branch.Value.ToString("D") : "shared");
        }

        private bool HasDirectApproverRole(Guid actor)
        {
            var roles = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1
            };
            roles.Criteria.AddCondition("name", ConditionOperator.Equal, ApproverRole);
            var members = roles.AddLink("systemuserroles", "roleid", "roleid");
            members.LinkCriteria.AddCondition(
                "systemuserid", ConditionOperator.Equal, actor);
            return service.RetrieveMultiple(roles).Entities.Count > 0;
        }

        private static void Fail(string code, string message)
        {
            throw new InvalidPluginExecutionException(code + ": " + message);
        }
    }
}
