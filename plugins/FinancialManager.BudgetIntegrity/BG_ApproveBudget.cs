using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.BudgetIntegrity
{
    /// <summary>
    /// Dedicated, bound fmg_ApproveBudget Custom API main-operation plug-in.
    /// The restricted bound Custom API is registered in development, but no
    /// independent approver is assigned and no approval acceptance is complete.
    /// </summary>
    public sealed class BG_ApproveBudget : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new InvalidPluginExecutionException("BAP001: Missing service context.");

            var context = serviceProvider.GetService(typeof(IPluginExecutionContext))
                as IPluginExecutionContext;
            var factory = serviceProvider.GetService(typeof(IOrganizationServiceFactory))
                as IOrganizationServiceFactory;

            if (context == null || factory == null ||
                context.MessageName != "fmg_ApproveBudget" ||
                !context.IsInTransaction || context.Mode != 0)
                throw new InvalidPluginExecutionException(
                    "BAP010: A synchronous transactional approval API is required.");

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is EntityReference reference) ||
                reference.LogicalName != "fmg_budget" ||
                reference.Id == Guid.Empty)
                throw new InvalidPluginExecutionException(
                    "BAP001: A bound budget Target is required.");

            // SDK context, not a caller-provided request parameter, authenticates
            // the initiating human user. The same user owns the service context.
            var actor = context.InitiatingUserId;
            var service = factory.CreateOrganizationService(actor);

            // Only a nested budget Update from this API can carry the marker.
            context.SharedVariables["fmg_ApprovalBudgetId"] = reference.Id.ToString("D");
            new BudgetApprovalOperation(service).Execute(
                reference.Id, actor, context.IsInTransaction, DateTime.UtcNow);
        }
    }
}
