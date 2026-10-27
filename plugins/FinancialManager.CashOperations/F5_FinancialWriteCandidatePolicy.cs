using System;
using System.Collections.Generic;
using System.Linq;
using FinancialManager.CashDomain;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// EXPERIMENTAL POLICY ONLY. Not registered or called by the installed
    /// F5G001 guards. This fail-closed verifier requires BOTH the exact pinned
    /// server MainOperation and newly verified Dataverse account grants, then
    /// compares every proposed Create with the immutable parent cash plan.
    /// It must not become live until ParentContext compatibility, Update,
    /// true ledger rollback and replay are tested against the financial tables.
    /// </summary>
    public static class F5_FinancialWriteCandidatePolicy
    {
        private static readonly string[] ReceiptFields = {
            "fmg_name", "fmg_externalrequestid", "fmg_requestfingerprint",
            "fmg_initiatinguserid", "fmg_amount", "transactioncurrencyid",
            "fmg_scope", "fmg_cashaccount", "fmg_receivedon", "fmg_status"
        };
        private static readonly string[] ReceiptBranchFields =
            ReceiptFields.Concat(new[] { "fmg_branch" }).ToArray();
        private static readonly string[] TransferFields = {
            "fmg_name", "fmg_externalrequestid", "fmg_requestfingerprint",
            "fmg_initiatinguserid", "fmg_amount", "transactioncurrencyid",
            "fmg_sourceaccount", "fmg_destinationaccount", "fmg_transferredon",
            "fmg_status"
        };
        private static readonly string[] PostingFields = {
            "fmg_name", "fmg_externalrequestid", "fmg_operationid",
            "fmg_cashaccount", "fmg_signedamount", "fmg_postedon",
            "fmg_kind", "transactioncurrencyid"
        };

        public static void RequireCreate(IPluginExecutionContext child,
            Guid pinnedMainStep, IOrganizationService actorService)
        {
            Entity? target = child?.InputParameters?.ContainsKey("Target") == true
                ? child.InputParameters["Target"] as Entity : null;
            if (child == null || pinnedMainStep == Guid.Empty ||
                actorService == null || child.Stage != 10 || child.Mode != 0 ||
                !child.IsInTransaction || child.MessageName != "Create" ||
                target == null || target.Id == Guid.Empty ||
                target.LogicalName != child.PrimaryEntityName)
                throw new InvalidPluginExecutionException(
                    "F5S011: Missing verified source transaction.");
            var parent = child.ParentContext;
            if (parent == null)
                throw new InvalidPluginExecutionException("F5S011: Missing parent context.");
            if (parent.Stage != 30 || parent.Mode != 0 ||
                !parent.IsInTransaction || parent.Depth < 1 ||
                child.Depth != parent.Depth + 1 ||
                parent.OwningExtension?.LogicalName != "sdkmessageprocessingstep" ||
                parent.OwningExtension.Id != pinnedMainStep ||
                parent.UserId == Guid.Empty || parent.UserId != parent.InitiatingUserId ||
                child.UserId != parent.UserId ||
                child.InitiatingUserId != parent.InitiatingUserId ||
                parent.CorrelationId == Guid.Empty ||
                child.CorrelationId != parent.CorrelationId ||
                child.OperationId != parent.OperationId)
                Deny();

            var request = F5_CashOperationEndpoint.ParseRequest(
                parent.MessageName, parent.InputParameters);
            // The domain policy reruns on the CURRENT server-side grant and
            // active account states. Parent lineage is not an authorization.
            var authorization = new DataverseCashAuthorization(actorService);
            var actor = authorization.Authorize(parent.InitiatingUserId, request);
            var plan = CashOperationPolicy.Prepare(request, actor);

            var table = child.PrimaryEntityName;
            if (table == "fmg_cashreceipt" && plan.Kind == CashOperationKind.SettleReceipt)
            {
                var account = actorService.Retrieve("fmg_cashaccount",
                    plan.Legs[0].AccountId, new ColumnSet("fmg_branch"));
                var branch = account?.GetAttributeValue<EntityReference>("fmg_branch");
                ExactFields(target, branch == null ? ReceiptFields : ReceiptBranchFields);
                HeaderCommon(target, plan, actorService);
                SameAccount(target, "fmg_cashaccount", plan.Legs[0].AccountId);
                if (branch != null)
                {
                    var saved = target.GetAttributeValue<EntityReference>("fmg_branch");
                    if (branch.LogicalName != "fmg_branch" ||
                        saved?.LogicalName != "fmg_branch" ||
                        saved.Id == Guid.Empty || saved.Id != branch.Id)
                        Deny();
                }
                SameChoice(target, "fmg_scope", branch == null ? 797281011 : 797281010);
                SameDate(target, "fmg_receivedon", plan.Legs[0].ValueDate);
                SameChoice(target, "fmg_status", 797281000);
                return;
            }
            if (table == "fmg_cashtransfer" && plan.Kind == CashOperationKind.InternalTransfer)
            {
                ExactFields(target, TransferFields);
                HeaderCommon(target, plan, actorService);
                SameAccount(target, "fmg_sourceaccount", plan.Legs[0].AccountId);
                SameAccount(target, "fmg_destinationaccount", plan.Legs[1].AccountId);
                SameDate(target, "fmg_transferredon", plan.Legs[0].ValueDate);
                SameChoice(target, "fmg_status", 797281100);
                return;
            }
            if (table == "fmg_cashposting")
            {
                ExactFields(target, PostingFields);
                var prefix = "F5-" + plan.Fingerprint + "-";
                var external = target.GetAttributeValue<string>("fmg_externalrequestid");
                var index = external == prefix + "0" ? 0 : external == prefix + "1" ? 1 : -1;
                if (index < 0 || index >= plan.Legs.Count ||
                    target.GetAttributeValue<string>("fmg_name") !=
                        "F5-" + plan.Fingerprint.Substring(0, 48) + "-" + index)
                    Deny();

                var headerKey = target.GetAttributeValue<string>("fmg_operationid");
                if (!Guid.TryParseExact(headerKey, "D", out var headerId) ||
                    headerId == Guid.Empty)
                    Deny();
                var headerTable = plan.Kind == CashOperationKind.SettleReceipt ?
                    "fmg_cashreceipt" : "fmg_cashtransfer";
                var header = actorService.Retrieve(headerTable, headerId,
                    new ColumnSet("fmg_externalrequestid", "fmg_requestfingerprint",
                        "fmg_initiatinguserid", "fmg_status", "transactioncurrencyid"));
                if (header == null)
                    throw new InvalidPluginExecutionException("F5S011: Missing header.");
                if (header.Id != headerId ||
                    header.GetAttributeValue<string>("fmg_externalrequestid") != plan.RequestKey ||
                    header.GetAttributeValue<string>("fmg_requestfingerprint") != plan.Fingerprint ||
                    header.GetAttributeValue<string>("fmg_initiatinguserid") !=
                        plan.SubjectId.ToString("D"))
                    Deny();
                SameChoice(header, "fmg_status",
                    plan.Kind == CashOperationKind.SettleReceipt ? 797281000 : 797281100);
                SameAccount(target, "fmg_cashaccount", plan.Legs[index].AccountId);
                SameDate(target, "fmg_postedon", plan.Legs[index].ValueDate);
                SameMoney(target, "fmg_signedamount", plan.Legs[index].SignedAmountPen);
                SameChoice(target, "fmg_kind",
                    plan.Kind == CashOperationKind.SettleReceipt ? 797281200 : 797281202);
                var postedCurrency =
                    target.GetAttributeValue<EntityReference>("transactioncurrencyid");
                var headerCurrency =
                    header.GetAttributeValue<EntityReference>("transactioncurrencyid");
                if (postedCurrency == null || headerCurrency == null ||
                    postedCurrency.LogicalName != "transactioncurrency" ||
                    postedCurrency.Id == Guid.Empty ||
                    headerCurrency.LogicalName != "transactioncurrency" ||
                    postedCurrency.Id != headerCurrency.Id)
                    Deny();
                return;
            }
            Deny();
        }

        private static void HeaderCommon(Entity row, PreparedCashOperation plan,
            IOrganizationService svc)
        {
            if (row.GetAttributeValue<string>("fmg_externalrequestid") != plan.RequestKey ||
                row.GetAttributeValue<string>("fmg_requestfingerprint") != plan.Fingerprint ||
                row.GetAttributeValue<string>("fmg_initiatinguserid") !=
                    plan.SubjectId.ToString("D") ||
                row.GetAttributeValue<string>("fmg_name") !=
                    "F5 " + plan.Kind + " " + plan.RequestKey)
                Deny();
            SameMoney(row, "fmg_amount", Math.Abs(plan.Legs[0].SignedAmountPen));
            var code = row.GetAttributeValue<EntityReference>("transactioncurrencyid");
            if (code == null || code.LogicalName != "transactioncurrency" ||
                code.Id == Guid.Empty)
                throw new InvalidPluginExecutionException("F5S011: Missing PEN currency.");
            var currency = svc.Retrieve("transactioncurrency", code.Id,
                new ColumnSet("isocurrencycode"));
            if (currency == null || currency.Id != code.Id ||
                currency.GetAttributeValue<string>("isocurrencycode") != "PEN")
                Deny();
        }

        private static void ExactFields(Entity row, IEnumerable<string> fields)
        {
            var expected = new HashSet<string>(fields, StringComparer.Ordinal);
            if (row.Attributes.Count != expected.Count ||
                row.Attributes.Keys.Any(key => !expected.Contains(key)) ||
                expected.Any(key => !row.Contains(key)))
                Deny();
        }
        private static void SameAccount(Entity row, string field, Guid id)
        {
            var account = row.GetAttributeValue<EntityReference>(field);
            if (account?.LogicalName != "fmg_cashaccount" || account.Id != id)
                Deny();
        }
        private static void SameDate(Entity row, string field, DateTime date)
        {
            if (!row.Contains(field) || row.GetAttributeValue<DateTime>(field) != date)
                Deny();
        }
        private static void SameMoney(Entity row, string field, decimal amount)
        {
            if (row.GetAttributeValue<Money>(field)?.Value != amount)
                Deny();
        }
        private static void SameChoice(Entity row, string field, int value)
        {
            if (row.GetAttributeValue<OptionSetValue>(field)?.Value != value)
                Deny();
        }
        private static void Deny() =>
            throw new InvalidPluginExecutionException(
                "F5S011: Financial write candidate not proven for exact server transaction.");
    }
}
