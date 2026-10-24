using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.BudgetIntegrity
{
    /// <summary>
    /// The approval command's preflight validation. It is separate from the
    /// regular budget update guard and is not sufficient without atomic
    /// arbitration, role separation and two-identity acceptance. The direct
    /// BGV009 approval block must remain active.
    /// </summary>
    public sealed class BudgetApprovalPreflight
    {
        public void Validate(Entity submittedBudget, Guid initiator, bool authorizedApprover)
        {
            if (submittedBudget == null || submittedBudget.LogicalName != "fmg_budget" ||
                submittedBudget.Id == Guid.Empty)
                Fail("BAP001", "A persisted budget is required.");

            if (submittedBudget.GetAttributeValue<OptionSetValue>("fmg_status")?.Value !=
                BudgetOptions.Submitted)
                Fail("BAP002", "Only a submitted budget may be considered for approval.");

            if (initiator == Guid.Empty || !authorizedApprover)
                Fail("BAP003", "An expressly authorized finance approver is required.");

            var creator = submittedBudget.GetAttributeValue<EntityReference>("createdby");
            var owner = submittedBudget.GetAttributeValue<EntityReference>("ownerid");

            // Team-owned budgets need an explicitly reviewed policy for team memberships;
            // fail closed until the separate role and team authorization work is complete.
            if (creator == null || creator.LogicalName != "systemuser" ||
                owner == null || owner.LogicalName != "systemuser" ||
                creator.Id == Guid.Empty || owner.Id == Guid.Empty)
                Fail("BAP004", "Creator and individual owner are required for segregation.");

            // A Submitted budget is immutable under the budget guard, so its
            // modifiedby metadata identifies the last submission writer.
            // If impersonation is in use, also reject the on-behalf identity.
            var submitter = submittedBudget.GetAttributeValue<EntityReference>("modifiedby");
            var onBehalf = submittedBudget.GetAttributeValue<EntityReference>("modifiedonbehalfby");
            if (submitter == null || submitter.LogicalName != "systemuser" ||
                submitter.Id == Guid.Empty ||
                (onBehalf != null && (onBehalf.LogicalName != "systemuser" ||
                                      onBehalf.Id == Guid.Empty)))
                Fail("BAP004", "Submission author metadata must be verifiable.");
            if (creator.Id == initiator || owner.Id == initiator ||
                submitter.Id == initiator ||
                (onBehalf != null && onBehalf.Id == initiator))
                Fail("BAP005", "Creator, owner or submitter cannot approve their own budget.");

            var year = submittedBudget.GetAttributeValue<int?>("fmg_year");
            var version = submittedBudget.GetAttributeValue<int?>("fmg_version");
            var month = submittedBudget.GetAttributeValue<int?>("fmg_effectivefrommonth");
            var scope = submittedBudget.GetAttributeValue<OptionSetValue>("fmg_scope")?.Value;
            var branch = submittedBudget.GetAttributeValue<EntityReference>("fmg_branch");

            if (year == null || year < 1 || version == null || version < 1 ||
                month == null || month < 1 || month > 12 ||
                (scope != BudgetOptions.Branch && scope != BudgetOptions.Shared) ||
                (scope == BudgetOptions.Branch && (branch == null || branch.Id == Guid.Empty)) ||
                (scope == BudgetOptions.Shared && branch != null))
                Fail("BAP006", "Budget version, effective month, scope and branch are invalid.");
        }

        private static void Fail(string code, string message)
        {
            throw new InvalidPluginExecutionException(code + ": " + message);
        }
    }
}
