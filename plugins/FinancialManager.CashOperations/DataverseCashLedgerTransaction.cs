using System;
using System.Collections.Generic;
using System.Linq;
using FinancialManager.CashDomain;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// SDK-bound cash ledger adapter. SOURCE ONLY: not registered as any
    /// operational Custom API. The F5G001 guard intentionally denies all
    /// these writes until a trusted nested server context is accepted.
    /// Each call requires the same synchronous Dataverse transaction.
    /// </summary>
    public sealed class DataverseCashLedgerTransaction : ICashLedgerTransaction
    {
        private readonly IOrganizationService service;
        private readonly Guid principal;
        private readonly Guid currencyId;

        public DataverseCashLedgerTransaction(IOrganizationService service,
            Guid verifiedInitiatingUser, Guid verifiedPenCurrencyId)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            if (verifiedInitiatingUser == Guid.Empty || verifiedPenCurrencyId == Guid.Empty)
                throw new CashPlanningException("F5E001", "Verified actor and PEN currency required.");
            principal = verifiedInitiatingUser;
            currencyId = verifiedPenCurrencyId;
        }

        private static string Table(CashOperationKind kind)
        {
            if (kind == CashOperationKind.SettleReceipt) return "fmg_cashreceipt";
            if (kind == CashOperationKind.InternalTransfer) return "fmg_cashtransfer";
            throw new CashPlanningException("F5E001", "Unsupported ledger operation kind.");
        }

        public CommittedCashOperation? FindCommittedByKey(CashOperationKind kind, string requestKey)
        {
            var table = Table(kind);
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet("fmg_externalrequestid", "fmg_requestfingerprint",
                    "fmg_initiatinguserid", "fmg_status"),
                TopCount = 2
            };
            query.Criteria.AddCondition("fmg_externalrequestid", ConditionOperator.Equal, requestKey);
            var list = service.RetrieveMultiple(query);
            if (list == null || list.MoreRecords || list.Entities.Count > 1)
                throw new CashPlanningException("F5E004", "Nonunique or incomplete committed request lookup.");
            if (list.Entities.Count == 0) return null;
            var row = list.Entities[0];
            var status = row.GetAttributeValue<OptionSetValue>("fmg_status")?.Value;
            var postedStatus = kind == CashOperationKind.SettleReceipt ? 797281001 : 797281101;
            if (status != postedStatus || row.Id == Guid.Empty ||
                row.GetAttributeValue<string>("fmg_externalrequestid") != requestKey)
                throw new CashPlanningException("F5E004", "Existing cash key is not a committed operation.");
            var fingerprint = row.GetAttributeValue<string>("fmg_requestfingerprint");
            var identity = row.GetAttributeValue<string>("fmg_initiatinguserid");
            if (fingerprint == null || fingerprint.Length != 64 ||
                !Guid.TryParseExact(identity, "D", out var user) || user == Guid.Empty)
                throw new CashPlanningException("F5E004", "Committed cash operation provenance missing.");
            return new CommittedCashOperation(row.Id, requestKey, kind, user, fingerprint);
        }

        public Guid CreateHeader(PreparedCashOperation plan)
        {
            if (plan == null || plan.SubjectId != principal ||
                (plan.Kind != CashOperationKind.SettleReceipt &&
                 plan.Kind != CashOperationKind.InternalTransfer) ||
                plan.Legs.Count != (plan.Kind == CashOperationKind.SettleReceipt ? 1 : 2) ||
                plan.Fingerprint.Length != 64)
                throw new CashPlanningException("F5E001", "Verified immutable cash plan required.");
            var table = Table(plan.Kind);
            var header = new Entity(table);
            header["fmg_name"] = "F5 " + plan.Kind + " " + plan.RequestKey;
            header["fmg_externalrequestid"] = plan.RequestKey;
            header["fmg_requestfingerprint"] = plan.Fingerprint;
            header["fmg_initiatinguserid"] = principal.ToString("D");
            header["fmg_amount"] = new Money(Math.Abs(plan.Legs[0].SignedAmountPen));
            header["transactioncurrencyid"] = new EntityReference("transactioncurrency", currencyId);
            if (plan.Kind == CashOperationKind.SettleReceipt)
            {
                var account = service.Retrieve("fmg_cashaccount", plan.Legs[0].AccountId,
                    new ColumnSet("fmg_isactive", "fmg_branch"));
                if (account == null || account.Id != plan.Legs[0].AccountId ||
                    !account.Contains("fmg_isactive") ||
                    !account.GetAttributeValue<bool>("fmg_isactive"))
                    throw new CashPlanningException("F5E002", "Receipt account no longer active.");
                var branch = account.GetAttributeValue<EntityReference>("fmg_branch");
                if (branch != null && (branch.Id == Guid.Empty ||
                    branch.LogicalName != "fmg_branch"))
                    throw new CashPlanningException("F5E002", "Invalid receipt branch.");
                if (branch != null)
                {
                    var branchRow = service.Retrieve("fmg_branch", branch.Id,
                        new ColumnSet("fmg_isactive"));
                    if (branchRow == null || branchRow.Id != branch.Id ||
                        !branchRow.Contains("fmg_isactive") ||
                        !branchRow.GetAttributeValue<bool>("fmg_isactive"))
                        throw new CashPlanningException("F5E002", "Receipt branch no longer active.");
                    header["fmg_branch"] = branch;
                }
                header["fmg_scope"] = new OptionSetValue(branch == null ? 797281011 : 797281010);
                header["fmg_cashaccount"] = new EntityReference("fmg_cashaccount",
                    plan.Legs[0].AccountId);
                header["fmg_receivedon"] = plan.Legs[0].ValueDate;
                header["fmg_status"] = new OptionSetValue(797281000); // Draft
            }
            else
            {
                header["fmg_sourceaccount"] =
                    new EntityReference("fmg_cashaccount", plan.Legs[0].AccountId);
                header["fmg_destinationaccount"] =
                    new EntityReference("fmg_cashaccount", plan.Legs[1].AccountId);
                header["fmg_transferredon"] = plan.Legs[0].ValueDate;
                header["fmg_status"] = new OptionSetValue(797281100); // Draft
            }
            return service.Create(header);
        }

        public Guid CreatePosting(Guid operationId, PreparedCashOperation plan,
            CashLeg leg, int index)
        {
            if (operationId == Guid.Empty || plan == null || plan.SubjectId != principal ||
                index < 0 || index >= plan.Legs.Count ||
                leg == null || leg.AccountId != plan.Legs[index].AccountId ||
                leg.SignedAmountPen != plan.Legs[index].SignedAmountPen ||
                leg.ValueDate != plan.Legs[index].ValueDate)
                throw new CashPlanningException("F5E001", "Untrusted or mismatched posting.");
            var posting = new Entity("fmg_cashposting");
            posting["fmg_name"] = "F5-" + plan.Fingerprint.Substring(0, 48) + "-" + index;
            posting["fmg_externalrequestid"] = "F5-" + plan.Fingerprint + "-" + index;
            posting["fmg_operationid"] = operationId.ToString("D");
            posting["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", leg.AccountId);
            posting["fmg_signedamount"] = new Money(leg.SignedAmountPen);
            posting["fmg_postedon"] = leg.ValueDate;
            posting["fmg_kind"] = new OptionSetValue(
                plan.Kind == CashOperationKind.SettleReceipt ? 797281200 : 797281202);
            posting["transactioncurrencyid"] = new EntityReference("transactioncurrency", currencyId);
            return service.Create(posting);
        }

        public void CompleteHeader(Guid operationId, string fingerprint,
            IReadOnlyList<Guid> postingIds)
        {
            if (operationId == Guid.Empty || fingerprint == null ||
                fingerprint.Length != 64 || postingIds == null ||
                postingIds.Count < 1 || postingIds.Count > 2 ||
                postingIds.Any(id => id == Guid.Empty) ||
                postingIds.Distinct().Count() != postingIds.Count)
                throw new CashPlanningException("F5E003", "Invalid cash completion evidence.");
            var posted = service.Retrieve("fmg_cashposting", postingIds[0],
                new ColumnSet("fmg_operationid", "fmg_kind"));
            if (posted == null || posted.Id != postingIds[0] ||
                posted.GetAttributeValue<string>("fmg_operationid") != operationId.ToString("D"))
                throw new CashPlanningException("F5E003", "First posted leg not linked to header.");
            var kind = posted.GetAttributeValue<OptionSetValue>("fmg_kind")?.Value;
            var table = kind == 797281200 ? "fmg_cashreceipt" :
                        kind == 797281202 ? "fmg_cashtransfer" : null;
            if (table == null || (table == "fmg_cashreceipt" ? 1 : 2) != postingIds.Count)
                throw new CashPlanningException("F5E003", "Wrong posting kind or incomplete transfer.");
            var row = service.Retrieve(table, operationId,
                new ColumnSet("fmg_status", "fmg_requestfingerprint", "fmg_initiatinguserid"));
            if (row == null || row.Id != operationId || string.IsNullOrWhiteSpace(row.RowVersion) ||
                row.GetAttributeValue<string>("fmg_requestfingerprint") != fingerprint ||
                row.GetAttributeValue<string>("fmg_initiatinguserid") != principal.ToString("D") ||
                row.GetAttributeValue<OptionSetValue>("fmg_status")?.Value !=
                    (kind == 797281200 ? 797281000 : 797281100))
                throw new CashPlanningException("F5E003", "Unexpected header status, version or provenance.");
            var update = new Entity(table, operationId) { RowVersion = row.RowVersion };
            update["fmg_status"] = new OptionSetValue(
                kind == 797281200 ? 797281001 : 797281101);
            service.Execute(new UpdateRequest
            {
                Target = update,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            });
        }

        public IReadOnlyList<CashPostingSnapshot> ReadCompleteLedger(Guid operationId)
        {
            if (operationId == Guid.Empty)
                throw new CashPlanningException("F5E004", "Missing operation identity.");
            var query = new QueryExpression("fmg_cashposting")
            {
                ColumnSet = new ColumnSet("fmg_cashaccount", "fmg_signedamount",
                    "fmg_postedon", "fmg_operationid"),
                TopCount = 3
            };
            query.Criteria.AddCondition("fmg_operationid", ConditionOperator.Equal,
                operationId.ToString("D"));
            var result = service.RetrieveMultiple(query);
            if (result == null || result.MoreRecords || result.Entities.Count > 2)
                throw new CashPlanningException("F5E004", "Incomplete or unexpected posting collection.");
            return result.Entities.Select(row =>
            {
                var account = row.GetAttributeValue<EntityReference>("fmg_cashaccount");
                var amount = row.GetAttributeValue<Money>("fmg_signedamount");
                if (row.Id == Guid.Empty || account == null ||
                    account.LogicalName != "fmg_cashaccount" || account.Id == Guid.Empty ||
                    amount == null ||
                    row.GetAttributeValue<string>("fmg_operationid") != operationId.ToString("D") ||
                    !row.Contains("fmg_postedon"))
                    throw new CashPlanningException("F5E004", "Malformed persisted cash posting.");
                return new CashPostingSnapshot(row.Id, operationId, account.Id, amount.Value,
                    row.GetAttributeValue<DateTime>("fmg_postedon"));
            }).ToArray();
        }
    }
}
