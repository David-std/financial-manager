using System;
using System.Linq;
using FinancialManager.CashDomain;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// SOURCE ONLY. Not registered. The 24 deny-all guard steps MUST remain
    /// active until the platform-authenticated nested-write trust boundary
    /// and transaction rollback are verified independently.
    /// </summary>
    public sealed class F5_GrantReviewEndpoint : IPlugin
    {
        public const string SubmitMessage = "fmg_SubmitCashGrantRequest";
        public const string DecideMessage = "fmg_DecideCashGrantRequest";

        public F5_GrantReviewEndpoint(string unsecureConfiguration, string secureConfiguration) { }

        public void Execute(IServiceProvider provider)
        {
            if (provider == null)
                throw new InvalidPluginExecutionException("F5Q030: Missing plugin runtime.");
            var ctx = provider.GetService(typeof(IPluginExecutionContext))
                as IPluginExecutionContext;
            var factory = provider.GetService(typeof(IOrganizationServiceFactory))
                as IOrganizationServiceFactory;
            if (ctx == null || factory == null)
                throw new InvalidPluginExecutionException("F5Q030: Missing platform identity.");
            try
            {
                var runtime = new CashRuntimeEvidence(ctx.Stage, ctx.Mode,
                    ctx.IsInTransaction, ctx.InitiatingUserId, ctx.UserId);
                CashRuntimePolicy.Require(runtime);
                // The service identity is exactly the authenticated initiator.
                // Do not request null/SYSTEM or substitute an admin principal.
                var service = factory.CreateOrganizationService(ctx.UserId);
                if (service == null)
                    throw new CashPlanningException("F5Q030", "Unable to obtain scoped service.");
                var workflow = new DataverseGrantReviewWorkflow(service);
                if (ctx.MessageName == SubmitMessage)
                {
                    var proposal = ParseSubmission(ctx.InputParameters);
                    var created = workflow.Submit(runtime, proposal, DateTime.UtcNow);
                    ctx.OutputParameters["RequestId"] = created;
                }
                else if (ctx.MessageName == DecideMessage)
                {
                    var decision = ParseDecision(ctx.InputParameters);
                    workflow.Decide(runtime, decision.Item1, decision.Item2,
                        DateTime.UtcNow);
                    ctx.OutputParameters["Recorded"] = true;
                }
                else
                    throw new CashPlanningException("F5Q030", "Unknown review API.");
            }
            catch (CashPlanningException exception)
            {
                throw new InvalidPluginExecutionException(
                    exception.Code + ": Financial grant review rejected.");
            }
        }

        private static T Need<T>(ParameterCollection input, string name)
        {
            if (!input.ContainsKey(name) || !(input[name] is T typed))
                throw new CashPlanningException("F5Q030", "Missing or mistyped " + name);
            return typed;
        }

        public static CashGrantChangeRequest ParseSubmission(ParameterCollection input)
        {
            if (input == null)
                throw new CashPlanningException("F5Q030", "Missing proposal arguments.");
            var fields = new[] { "RequestId", "Beneficiary", "CashAccount",
                "Operation", "ChangeKind", "AllowShared", "Reason", "ExpiresAtUtc" };
            if (input.Keys.Any(key => !fields.Contains(key, StringComparer.Ordinal)) ||
                input.Count != fields.Length)
                throw new CashPlanningException("F5Q030", "Unexpected proposal arguments.");
            var requestId = Need<Guid>(input, "RequestId");
            var beneficiary = Need<EntityReference>(input, "Beneficiary");
            var account = Need<EntityReference>(input, "CashAccount");
            var operation = Need<OptionSetValue>(input, "Operation");
            var change = Need<OptionSetValue>(input, "ChangeKind");
            var shared = Need<bool>(input, "AllowShared");
            var reason = Need<string>(input, "Reason");
            var expiry = Need<DateTime>(input, "ExpiresAtUtc");
            if (requestId == Guid.Empty ||
                beneficiary.LogicalName != "systemuser" || beneficiary.Id == Guid.Empty ||
                account.LogicalName != "fmg_cashaccount" || account.Id == Guid.Empty ||
                expiry.Kind != DateTimeKind.Utc)
                throw new CashPlanningException("F5Q030", "Invalid identifiers or UTC expiry.");

            CashOperationKind kind;
            switch (operation.Value)
            {
                case 797282000: kind = CashOperationKind.SettleReceipt; break;
                case 797282001: kind = CashOperationKind.InternalTransfer; break;
                case 797282002: kind = CashOperationKind.Reversal; break;
                default: throw new CashPlanningException("F5Q030", "Unsupported cash grant operation.");
            }
            CashGrantChangeKind action;
            switch (change.Value)
            {
                case 797283100: action = CashGrantChangeKind.Grant; break;
                case 797283101: action = CashGrantChangeKind.Revoke; break;
                default: throw new CashPlanningException("F5Q030", "Unsupported grant change.");
            }
            return new CashGrantChangeRequest(requestId, beneficiary.Id,
                account.Id, kind, action, shared, reason, expiry);
        }

        public static Tuple<Guid, bool> ParseDecision(ParameterCollection input)
        {
            if (input == null || input.Count != 2 ||
                input.Keys.Any(key => key != "RequestId" && key != "Approved"))
                throw new CashPlanningException("F5Q030", "Unexpected decision arguments.");
            var id = Need<Guid>(input, "RequestId");
            var approved = Need<bool>(input, "Approved");
            if (id == Guid.Empty)
                throw new CashPlanningException("F5Q030", "Missing review identity.");
            return Tuple.Create(id, approved);
        }
    }
}
