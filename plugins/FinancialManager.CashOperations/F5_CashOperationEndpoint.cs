using System;
using System.Linq;
using FinancialManager.CashDomain;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// SOURCE ONLY: NOT REGISTERED to any financial Custom API.
    /// Once the F5 guard has a reviewed platform-only nested context,
    /// this is the typed synchronous MainOperation implementation.
    /// There is no alternate administrator or client-claimed authorization.
    /// </summary>
    public sealed class F5_CashOperationEndpoint : IPlugin
    {
        public const string ReceiptMessage = "fmg_SettleCashReceipt";
        public const string TransferMessage = "fmg_PostCashTransfer";

        public F5_CashOperationEndpoint(string unsecureConfiguration, string secureConfiguration) { }

        public void Execute(IServiceProvider provider)
        {
            if (provider == null)
                throw new InvalidPluginExecutionException("F5E001: Plugin services unavailable.");
            var context = provider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext;
            var factory = provider.GetService(typeof(IOrganizationServiceFactory)) as IOrganizationServiceFactory;
            if (context == null || factory == null)
                throw new InvalidPluginExecutionException("F5E001: Verified Dataverse context unavailable.");

            var runtime = new CashRuntimeEvidence(context.Stage, context.Mode,
                context.IsInTransaction, context.InitiatingUserId, context.UserId);
            try
            {
                // This runs before parsing parameters, checking account IDs or
                // invoking the SDK, so an untrusted execution never writes.
                CashRuntimePolicy.Require(runtime);
                var request = ParseRequest(context.MessageName, context.InputParameters);
                var service = factory.CreateOrganizationService(context.UserId);
                if (service == null)
                    throw new CashPlanningException("F5E001", "Authorized service unavailable.");
                var currencyId = FindExactlyOnePenCurrency(service);
                var coordinator = new CashExecutionCoordinator(
                    new DataverseCashAuthorization(service),
                    new DataverseCashLedgerTransaction(service, runtime.InitiatingUserId,
                        currencyId, context));
                var outcome = coordinator.Execute(runtime, request);
                context.OutputParameters["OperationId"] = outcome.OperationId;
                context.OutputParameters["WasReplay"] = outcome.WasReplay;
            }
            catch (CashPlanningException error)
            {
                // The event pipeline rolls back all writes on this failure;
                // expose stable validation codes, never server metadata.
                throw new InvalidPluginExecutionException(error.Code + ": Cash operation was rejected.");
            }
        }

        public static CashOperationRequest ParseRequest(string message,
            ParameterCollection inputs)
        {
            if (inputs == null)
                throw new CashPlanningException("F5E001", "Missing cash API arguments.");
            CashOperationKind kind;
            if (message == ReceiptMessage) kind = CashOperationKind.SettleReceipt;
            else if (message == TransferMessage) kind = CashOperationKind.InternalTransfer;
            else throw new CashPlanningException("F5E001", "Unexpected cash API message.");

            var allowed = kind == CashOperationKind.SettleReceipt
                ? new[] { "RequestKey", "SourceAccount", "Amount", "ValueDate", "EvidenceReference" }
                : new[] { "RequestKey", "SourceAccount", "DestinationAccount", "Amount",
                    "ValueDate", "EvidenceReference" };
            if (inputs.Keys.Any(k => !allowed.Contains(k, StringComparer.Ordinal)))
                throw new CashPlanningException("F5E001", "Unexpected cash API arguments.");

            // Custom API parameter names and SDK types are fixed. The caller
            // never supplies principal, branch, operation kind, currency id
            // or an authorization override.
            var key = Required<string>(inputs, "RequestKey");
            var source = Required<EntityReference>(inputs, "SourceAccount");
            var money = Required<Money>(inputs, "Amount");
            var date = Required<DateTime>(inputs, "ValueDate");
            var evidence = Required<string>(inputs, "EvidenceReference");
            if (source.LogicalName != "fmg_cashaccount" || source.Id == Guid.Empty)
                throw new CashPlanningException("F5E001", "Invalid source cash account.");
            Guid? destinationId = null;
            if (kind == CashOperationKind.InternalTransfer)
            {
                var destination = Required<EntityReference>(inputs, "DestinationAccount");
                if (destination.LogicalName != "fmg_cashaccount" || destination.Id == Guid.Empty)
                    throw new CashPlanningException("F5E001", "Invalid transfer destination account.");
                destinationId = destination.Id;
            }
            else if (inputs.ContainsKey("DestinationAccount"))
                throw new CashPlanningException("F5E001", "Receipts cannot choose a destination.");
            return new CashOperationRequest(kind, key, source.Id, destinationId,
                money.Value, date, "PEN", evidence);
        }

        private static T Required<T>(ParameterCollection inputs, string name)
        {
            if (!inputs.ContainsKey(name) || !(inputs[name] is T value))
                throw new CashPlanningException("F5E001", "Missing or invalid cash API parameter " + name);
            return value;
        }

        public static Guid FindExactlyOnePenCurrency(IOrganizationService service)
        {
            if (service == null)
                throw new CashPlanningException("F5E001", "Missing Dataverse currency provider.");
            var query = new QueryExpression("transactioncurrency")
            {
                ColumnSet = new ColumnSet("transactioncurrencyid", "isocurrencycode"),
                TopCount = 2
            };
            query.Criteria.AddCondition("isocurrencycode", ConditionOperator.Equal, "PEN");
            var matches = service.RetrieveMultiple(query);
            if (matches == null || matches.MoreRecords || matches.Entities.Count != 1 ||
                matches.Entities[0].Id == Guid.Empty ||
                matches.Entities[0].GetAttributeValue<string>("isocurrencycode") != "PEN")
                throw new CashPlanningException("F5E001", "PEN currency cannot be independently verified.");
            return matches.Entities[0].Id;
        }
    }
}
