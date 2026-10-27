using System;
using System.Linq;
using FinancialManager.CashDomain;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// SOURCE-ONLY validation of the final header update for a fully balanced
    /// cash operation. Not registered in Dataverse or in an active F5G001 guard.
    /// Must be proven live with the exact pinned step and rollback BEFORE use.
    /// </summary>
    public static class F5_FinancialHeaderCompletionPolicy
    {
        public static void RequireUpdate(IPluginExecutionContext child, Guid pinnedMainStep,
            IOrganizationService service)
        {
            if (child == null || service == null)
                throw new InvalidPluginExecutionException("F5S012: Missing server execution context.");
            if (pinnedMainStep == Guid.Empty ||
                child.Stage != 10 || child.Mode != 0 || !child.IsInTransaction ||
                child.MessageName != "Update" ||
                !(child.InputParameters?.ContainsKey("Target") == true &&
                  child.InputParameters["Target"] is Entity))
                Reject();

            var update = child.InputParameters?["Target"] as Entity;
            if (update == null)
                throw new InvalidPluginExecutionException("F5S012: Missing financial update.");
            if (update.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(update.RowVersion) ||
                update.LogicalName != child.PrimaryEntityName ||
                update.Attributes.Count != 1 || !update.Contains("fmg_status"))
                Reject();
            var parent = child.ParentContext;
            if (parent == null)
                throw new InvalidPluginExecutionException("F5S012: Missing registered parent context.");
            if (parent.Stage != 30 || parent.Mode != 0 ||
                !parent.IsInTransaction || parent.Depth < 1 ||
                child.Depth != parent.Depth + 1 || parent.CorrelationId == Guid.Empty ||
                child.CorrelationId != parent.CorrelationId ||
                child.OperationId != parent.OperationId ||
                parent.UserId == Guid.Empty ||
                parent.UserId != parent.InitiatingUserId ||
                child.UserId != parent.UserId ||
                child.InitiatingUserId != parent.InitiatingUserId ||
                parent.OwningExtension?.LogicalName != "sdkmessageprocessingstep" ||
                parent.OwningExtension.Id != pinnedMainStep)
                Reject();
            var request = F5_CashOperationEndpoint.ParseRequest(
                parent.MessageName, parent.InputParameters);
            var actor = new DataverseCashAuthorization(service)
                .Authorize(parent.InitiatingUserId, request);
            var plan = CashOperationPolicy.Prepare(request, actor);

            var table = plan.Kind == CashOperationKind.SettleReceipt
                ? "fmg_cashreceipt" : "fmg_cashtransfer";
            var draft = plan.Kind == CashOperationKind.SettleReceipt ? 797281000 : 797281100;
            var posted = plan.Kind == CashOperationKind.SettleReceipt ? 797281001 : 797281101;
            var postingKind = plan.Kind == CashOperationKind.SettleReceipt ? 797281200 : 797281202;
            if (update.LogicalName != table ||
                update.GetAttributeValue<OptionSetValue>("fmg_status")?.Value != posted)
                Reject();

            var header = service.Retrieve(table, update.Id,
                new ColumnSet("fmg_externalrequestid", "fmg_requestfingerprint",
                    "fmg_initiatinguserid", "fmg_status", "fmg_amount",
                    "transactioncurrencyid", "fmg_sourceaccount",
                    "fmg_destinationaccount", "fmg_cashaccount"));
            if (header == null)
                throw new InvalidPluginExecutionException("F5S012: Missing immutable cash header.");
            if (header.Id != update.Id ||
                string.IsNullOrWhiteSpace(header.RowVersion) ||
                header.RowVersion != update.RowVersion ||
                header.GetAttributeValue<string>("fmg_externalrequestid") != plan.RequestKey ||
                header.GetAttributeValue<string>("fmg_requestfingerprint") != plan.Fingerprint ||
                header.GetAttributeValue<string>("fmg_initiatinguserid") !=
                    plan.SubjectId.ToString("D") ||
                header.GetAttributeValue<OptionSetValue>("fmg_status")?.Value != draft ||
                header.GetAttributeValue<Money>("fmg_amount")?.Value !=
                    Math.Abs(plan.Legs[0].SignedAmountPen))
                Reject();
            var currency = header.GetAttributeValue<EntityReference>("transactioncurrencyid");
            if (currency == null || currency.LogicalName != "transactioncurrency" ||
                currency.Id == Guid.Empty)
                throw new InvalidPluginExecutionException("F5S012: Invalid currency reference.");
            if (plan.Kind == CashOperationKind.InternalTransfer)
            {
                if (header.GetAttributeValue<EntityReference>("fmg_sourceaccount")?.Id !=
                    plan.Legs[0].AccountId ||
                    header.GetAttributeValue<EntityReference>("fmg_destinationaccount")?.Id !=
                    plan.Legs[1].AccountId)
                    Reject();
            }
            else if (header.GetAttributeValue<EntityReference>("fmg_cashaccount")?.Id !=
                plan.Legs[0].AccountId)
                Reject();

            var query = new QueryExpression("fmg_cashposting")
            {
                ColumnSet = new ColumnSet("fmg_name", "fmg_externalrequestid",
                    "fmg_operationid", "fmg_cashaccount", "fmg_signedamount",
                    "fmg_postedon", "fmg_kind", "transactioncurrencyid"),
                TopCount = 3
            };
            query.Criteria.AddCondition("fmg_operationid", ConditionOperator.Equal,
                update.Id.ToString("D"));
            var result = service.RetrieveMultiple(query);
            if (result == null)
                throw new InvalidPluginExecutionException("F5S012: Missing complete ledger.");
            if (result.MoreRecords ||
                result.Entities.Count != plan.Legs.Count ||
                result.Entities.Select(e => e.Id).Distinct().Count() != plan.Legs.Count)
                Reject();

            for (var i = 0; i < plan.Legs.Count; i++)
            {
                var leg = plan.Legs[i];
                var expectedKey = "F5-" + plan.Fingerprint + "-" + i;
                var rows = result.Entities.Where(e =>
                    e.GetAttributeValue<string>("fmg_externalrequestid") == expectedKey).ToArray();
                if (rows.Length != 1 || rows[0].Id == Guid.Empty)
                    throw new InvalidPluginExecutionException("F5S012: Missing exact ledger leg.");
                var row = rows[0];
                var rowAccount = row.GetAttributeValue<EntityReference>("fmg_cashaccount");
                var rowCurrency = row.GetAttributeValue<EntityReference>("transactioncurrencyid");
                if (row.GetAttributeValue<string>("fmg_name") !=
                        "F5-" + plan.Fingerprint.Substring(0, 48) + "-" + i ||
                    row.GetAttributeValue<string>("fmg_operationid") != update.Id.ToString("D") ||
                    row.GetAttributeValue<OptionSetValue>("fmg_kind")?.Value != postingKind ||
                    rowAccount == null || rowAccount.LogicalName != "fmg_cashaccount" ||
                    rowAccount.Id != leg.AccountId ||
                    rowCurrency == null || rowCurrency.LogicalName != "transactioncurrency" ||
                    rowCurrency.Id != currency.Id ||
                    row.GetAttributeValue<Money>("fmg_signedamount")?.Value != leg.SignedAmountPen ||
                    !row.Contains("fmg_postedon") ||
                    row.GetAttributeValue<DateTime>("fmg_postedon") != leg.ValueDate)
                    Reject();
            }
        }

        private static void Reject() =>
            throw new InvalidPluginExecutionException(
                "F5S012: Header cannot be completed without two exact authorized balanced legs.");
    }
}
