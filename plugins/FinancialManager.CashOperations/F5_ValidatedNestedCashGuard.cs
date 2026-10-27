using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// Candidate fail-closed guard for real F5 ledger tables. Not yet
    /// registered as a Dataverse step. Receipt and transfer MUST have
    /// separate MainOperation pins. The 18 F5G001 steps remain active.
    /// </summary>
    public sealed class F5_ValidatedNestedCashGuard : IPlugin
    {
        private readonly Guid receiptStepId;
        private readonly Guid transferStepId;

        public F5_ValidatedNestedCashGuard(
            string unsecureConfiguration, string secureConfiguration)
        {
            var parts = unsecureConfiguration?.Split('|');
            if (parts == null || parts.Length != 2 ||
                !Guid.TryParseExact(parts[0], "D", out receiptStepId) ||
                !Guid.TryParseExact(parts[1], "D", out transferStepId) ||
                receiptStepId == Guid.Empty || transferStepId == Guid.Empty ||
                receiptStepId == transferStepId)
                throw new InvalidPluginExecutionException(
                    "F5S010: Two distinct verified MainOperation pins required.");
        }

        public void Execute(IServiceProvider provider)
        {
            var context = provider?.GetService(typeof(IPluginExecutionContext))
                as IPluginExecutionContext;
            var factory = provider?.GetService(typeof(IOrganizationServiceFactory))
                as IOrganizationServiceFactory;
            if (context == null || factory == null)
                throw new InvalidPluginExecutionException(
                    "F5S010: Missing financial Dataverse guard services.");
            if (context.UserId == Guid.Empty ||
                context.UserId != context.InitiatingUserId)
                throw new InvalidPluginExecutionException(
                    "F5S010: Untrusted delegated cash principal.");
            if (context.PrimaryEntityName != "fmg_cashreceipt" &&
                context.PrimaryEntityName != "fmg_cashtransfer" &&
                context.PrimaryEntityName != "fmg_cashposting")
                throw new InvalidPluginExecutionException(
                    "F5G001: Unimplemented financial table remains read-only.");
            if (context.MessageName == "Delete" ||
                (context.MessageName != "Create" && context.MessageName != "Update"))
                throw new InvalidPluginExecutionException(
                    "F5G001: Cash deletion and unknown writes are forbidden.");
            var scoped = factory.CreateOrganizationService(context.UserId);
            if (scoped == null)
                throw new InvalidPluginExecutionException(
                    "F5S010: Financial grant read service unavailable.");
            RequireOperation(context, receiptStepId, transferStepId, scoped);
        }

        /// <summary>Strict operation-specific parent-step selection;
        /// trusted lineage alone never provides the account grant.</summary>
        public static void RequireOperation(IPluginExecutionContext context,
            Guid verifiedReceiptStep, Guid verifiedTransferStep,
            IOrganizationService service)
        {
            if (context == null || service == null ||
                verifiedReceiptStep == Guid.Empty ||
                verifiedTransferStep == Guid.Empty ||
                verifiedReceiptStep == verifiedTransferStep ||
                context.Stage != 10 || context.Mode != 0 ||
                !context.IsInTransaction)
                throw new InvalidPluginExecutionException(
                    "F5S010: No verified synchronous financial transaction.");
            var parent = context.ParentContext;
            var stepId = parent?.MessageName == F5_CashOperationEndpoint.ReceiptMessage
                ? verifiedReceiptStep :
                parent?.MessageName == F5_CashOperationEndpoint.TransferMessage
                    ? verifiedTransferStep : Guid.Empty;
            if (stepId == Guid.Empty)
                throw new InvalidPluginExecutionException(
                    "F5S010: Unknown cash parent operation.");
            if (context.PrimaryEntityName == "fmg_cashreceipt" &&
                parent?.MessageName != F5_CashOperationEndpoint.ReceiptMessage)
                throw new InvalidPluginExecutionException(
                    "F5S010: Transfer cannot write a receipt.");
            if (context.PrimaryEntityName == "fmg_cashtransfer" &&
                parent?.MessageName != F5_CashOperationEndpoint.TransferMessage)
                throw new InvalidPluginExecutionException(
                    "F5S010: Receipt cannot write a transfer.");
            if (context.MessageName == "Create")
                F5_FinancialWriteCandidatePolicy.RequireCreate(context,
                    stepId, service);
            else if (context.MessageName == "Update" &&
                (context.PrimaryEntityName == "fmg_cashreceipt" ||
                 context.PrimaryEntityName == "fmg_cashtransfer"))
                F5_FinancialHeaderCompletionPolicy.RequireUpdate(context,
                    stepId, service);
            else
                throw new InvalidPluginExecutionException(
                    "F5G001: No authorized financial write policy.");
        }
    }
}
