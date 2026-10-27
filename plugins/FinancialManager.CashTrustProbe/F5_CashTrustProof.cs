using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.CashTrustProbe
{
    /// <summary>DEV ONLY. Synthetic isolated test, never touches any F5
    /// cash tables or financial grants. Always throws after two nested creates,
    /// requiring the Dataverse platform transaction to roll back BOTH.</summary>
    public sealed class F5_CashTrustProof : IPlugin
    {
        private const string Message = "fmg_CashNestedTrustProbe";
        private const string Table = "fmg_cashtrustprobe";

        public F5_CashTrustProof(string unsecureConfiguration, string secureConfiguration) { }
        public void Execute(IServiceProvider services)
        {
            var ctx = services?.GetService(typeof(IPluginExecutionContext))
                as IPluginExecutionContext;
            var factory = services?.GetService(typeof(IOrganizationServiceFactory))
                as IOrganizationServiceFactory;
            if (ctx == null || factory == null ||
                ctx.MessageName != Message || ctx.Stage != 30 || ctx.Mode != 0 ||
                !ctx.IsInTransaction || ctx.UserId == Guid.Empty ||
                ctx.UserId != ctx.InitiatingUserId || ctx.CorrelationId == Guid.Empty)
                throw new InvalidPluginExecutionException("F5P000: Incorrect trust-probe runtime.");
            var key = ctx.InputParameters?.ContainsKey("RequestKey") == true
                ? ctx.InputParameters["RequestKey"] as string : null;
            if (string.IsNullOrWhiteSpace(key) ||
                !Guid.TryParseExact(key, "D", out var parsed) || parsed == Guid.Empty ||
                (ctx.InputParameters?.Count ?? 0) != 1)
                throw new InvalidPluginExecutionException("F5P000: Malformed synthetic test request.");
            var svc = factory.CreateOrganizationService(ctx.UserId);
            if (svc == null)
                throw new InvalidPluginExecutionException("F5P000: No scoped Dataverse service.");
            for (var index = 0; index < 2; index++)
            {
                var label = "F5-SYNTHETIC-TRUST-" + key + "-" + index;
                var query = new QueryExpression(Table)
                {
                    ColumnSet = new ColumnSet(false), TopCount = 1
                };
                query.Criteria.AddCondition("fmg_name", ConditionOperator.Equal, label);
                if (svc.RetrieveMultiple(query).Entities.Count != 0)
                    throw new InvalidPluginExecutionException(
                        "F5P000: Synthetic key already existed before proof.");
                var row = new Entity(Table, Guid.NewGuid());
                row["fmg_name"] = label;
                FinancialManager.CashOperations.F5_ServerWriteScope.Issue(
                    ctx, Table, "Create", row.Id);
                if (svc.Create(row) != row.Id)
                    throw new InvalidPluginExecutionException(
                        "F5P000: Unexpected test record identity.");
            }
            // Intentionally fault *after* both guarded creations. The caller
            // must check that neither is visible outside the rolled-back API.
            throw new InvalidPluginExecutionException(
                "F5P_ROLLBACK_EXPECTED: Two guarded synthetic writes must be rolled back.");
        }
    }

    public sealed class F5_CashTrustGuard : IPlugin
    {
        private readonly Guid pinnedMainStepId;

        public F5_CashTrustGuard(string unsecureConfiguration, string secureConfiguration)
        {
            // An absent or invalid pin is always deny-all, including admin.
            if (!Guid.TryParseExact(unsecureConfiguration, "D", out pinnedMainStepId) ||
                pinnedMainStepId == Guid.Empty)
                throw new InvalidPluginExecutionException(
                    "F5S005: Synthetic guard missing verified MainOperation step pin.");
        }

        public void Execute(IServiceProvider services)
        {
            var ctx = services?.GetService(typeof(IPluginExecutionContext))
                as IPluginExecutionContext;
            if (ctx == null || ctx.PrimaryEntityName != "fmg_cashtrustprobe" ||
                ctx.Stage != 10 || ctx.Mode != 0)
                throw new InvalidPluginExecutionException("F5P001: Unexpected diagnostic guard.");
            if (ctx.MessageName != "Create")
                throw new InvalidPluginExecutionException(
                    "F5P002: Synthetic table Update/Delete are always denied.");
            try
            {
                F5_SyntheticServerOriginPolicy.Require(ctx, pinnedMainStepId);
            }
            catch (InvalidPluginExecutionException)
            {
                // Diagnostic metadata ONLY; no secrets, token, GUID,
                // person, organization or record content may leave the
                // isolated development sandbox.
                if (ctx.ParentContext == null)
                    throw new InvalidPluginExecutionException(
                        "F5S002: Direct synthetic writes are forbidden.");

                var parent = ctx.ParentContext;
                bool hasTicket = false;
                if (parent?.SharedVariables != null)
                    foreach (var key in parent.SharedVariables.Keys)
                        if (key is string name &&
                            name.StartsWith(
                                "fmg.financialmanager.2026.cash.server.write.authorization.",
                                StringComparison.Ordinal))
                            hasTicket = true;
                bool childTicket = false;
                if (ctx.SharedVariables != null)
                    foreach (var key in ctx.SharedVariables.Keys)
                        if (key is string name &&
                            name.StartsWith(
                                "fmg.financialmanager.2026.cash.server.write.authorization.",
                                StringComparison.Ordinal))
                            childTicket = true;
                throw new InvalidPluginExecutionException(
                    "F5P_CONTEXT childTicketPresent=" + childTicket +
                    " parentOwnerKind=" + (parent?.OwningExtension?.LogicalName ?? "none") +
                    " childOwnerKind=" + (ctx.OwningExtension?.LogicalName ?? "none") +
                    " parentNameTrusted=" + (parent?.MessageName == "fmg_CashNestedTrustProbe") +
                    " parent=" + (parent != null) +
                    " parentStage=" + (parent?.Stage ?? -1) +
                    " parentMode=" + (parent?.Mode ?? -1) +
                    " parentTx=" + (parent?.IsInTransaction ?? false) +
                    " childStage=" + ctx.Stage +
                    " childMode=" + ctx.Mode +
                    " childTx=" + ctx.IsInTransaction +
                    " depthLinked=" + (parent != null && ctx.Depth == parent.Depth + 1) +
                    " sameIdentity=" + (parent != null &&
                        parent.UserId == ctx.UserId &&
                        parent.InitiatingUserId == ctx.InitiatingUserId) +
                    " sameCorrelation=" + (parent != null &&
                        parent.CorrelationId == ctx.CorrelationId) +
                    " sameOperation=" + (parent != null &&
                        parent.OperationId == ctx.OperationId) +
                    " serverTicketPresent=" + hasTicket);
            }
        }
    }
}