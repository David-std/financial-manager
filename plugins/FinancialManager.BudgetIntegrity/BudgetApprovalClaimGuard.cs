using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.BudgetIntegrity
{
    /// <summary>
    /// Only a nested transactional fmg_ApproveBudget operation can reserve the
    /// annual scope key. Claims are never mutable after creation.
    /// </summary>
    public static class BudgetApprovalClaimGuard
    {
        public static void Validate(
            string message, Entity target, string parentMessage,
            bool inTransaction, Guid parentBudgetId)
        {
            if (message != "Create")
                throw new InvalidPluginExecutionException(
                    "BAC001: Approval claims are immutable.");

            if (target == null || target.LogicalName != "fmg_budgetapprovalclaim")
                throw new InvalidPluginExecutionException(
                    "BAC002: Invalid approval claim.");

            if (parentMessage != "fmg_ApproveBudget" || !inTransaction ||
                parentBudgetId == Guid.Empty)
                throw new InvalidPluginExecutionException(
                    "BAC003: Claims can only be written by the trusted approval API.");

            var budget = target.GetAttributeValue<EntityReference>("fmg_budget");
            if (budget == null || budget.LogicalName != "fmg_budget" ||
                budget.Id != parentBudgetId)
                throw new InvalidPluginExecutionException(
                    "BAC004: Approval claim does not match the trusted budget.");
        }
    }

    /// <summary>
    /// Server-side PreValidation guard for direct claim Create/Update/Delete.
    /// Register all three synchronous steps, without client bypass permissions.
    /// </summary>
    public sealed class BAC_BlockDirectClaimWrites : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = serviceProvider?.GetService(typeof(IPluginExecutionContext))
                as IPluginExecutionContext;

            if (context == null || context.Mode != 0 || context.Stage != 10 ||
                context.PrimaryEntityName != "fmg_budgetapprovalclaim" ||
                !context.InputParameters.Contains("Target"))
                throw new InvalidPluginExecutionException(
                    "BAC002: Claim write requires synchronous PreValidation.");

            var parent = context.ParentContext;
            Guid parentBudgetId = Guid.Empty;
            if (parent != null && parent.IsInTransaction &&
                context.IsInTransaction && parent.MessageName == "fmg_ApproveBudget" &&
                parent.InitiatingUserId == context.InitiatingUserId &&
                parent.SharedVariables.Contains("fmg_ApprovalBudgetId"))
            {
                var marker = parent.SharedVariables["fmg_ApprovalBudgetId"] as string;
                Guid.TryParse(marker, out parentBudgetId);
            }

            BudgetApprovalClaimGuard.Validate(
                context.MessageName, context.InputParameters["Target"] as Entity,
                parent?.MessageName, parentBudgetId != Guid.Empty,
                parentBudgetId);
        }
    }
}
