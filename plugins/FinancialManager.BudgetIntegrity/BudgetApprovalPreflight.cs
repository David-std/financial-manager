using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.BudgetIntegrity
{
    /// <summary>
    /// A standalone preflight for the FUTURE dedicated approval command.
    /// It is deliberately not registered as a plug-in step or called by BudgetGuard.
    /// Preflight validation is NOT a substitute for atomic effective-version arbitration.
    /// The live BGV009 approval block must stay in place until ADR 0004 is complete.
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

            if (creator.Id == initiator || owner.Id == initiator)
                Fail("BAP005", "A budget creator or owner may not approve their own budget.");

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
