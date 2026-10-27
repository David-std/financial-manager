using System;
using System.Linq;
using FinancialManager.CashDomain;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// Source-only, read-only materializer for an audited pending grant request.
    /// Mutations remain denied (F5Q009). Approval role checks and atomic
    /// application of the grant MUST be a separately reviewed server service.
    /// </summary>
    public sealed class DataverseCashGrantRequestReader
    {
        private readonly IOrganizationService service;

        public DataverseCashGrantRequestReader(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public VerifiedPendingGrantReadback ReadPending(Guid id)
        {
            if (id == Guid.Empty)
                throw new CashPlanningException("F5Q001", "Grant review record id required.");
            var row = service.Retrieve("fmg_cashgrantrequest", id,
                new ColumnSet("fmg_externalrequestid", "fmg_requestfingerprint",
                    "fmg_proposer", "fmg_beneficiary", "fmg_cashaccount", "fmg_operation",
                    "fmg_changekind", "fmg_allowshared", "fmg_reason",
                    "fmg_expiresatutc", "fmg_reviewstatus", "fmg_approver",
                    "createdon", "fmg_decidedatutc"));
            if (row == null || row.Id != id || string.IsNullOrWhiteSpace(row.RowVersion) ||
                row.GetAttributeValue<string>("fmg_externalrequestid") != id.ToString("D") ||
                row.GetAttributeValue<OptionSetValue>("fmg_reviewstatus")?.Value != 797283000 ||
                !row.Contains("fmg_allowshared") ||
                !row.Contains("createdon") || !row.Contains("fmg_expiresatutc") ||
                row.Contains("fmg_approver") || row.Contains("fmg_decidedatutc"))
                throw new CashPlanningException("F5Q001", "Review is not a pristine pending request.");

            var proposer = row.GetAttributeValue<EntityReference>("fmg_proposer");
            var beneficiary = row.GetAttributeValue<EntityReference>("fmg_beneficiary");
            var account = row.GetAttributeValue<EntityReference>("fmg_cashaccount");
            var option = row.GetAttributeValue<OptionSetValue>("fmg_operation");
            var change = row.GetAttributeValue<OptionSetValue>("fmg_changekind");
            var fingerprint = row.GetAttributeValue<string>("fmg_requestfingerprint");
            var reason = row.GetAttributeValue<string>("fmg_reason");
            if (proposer?.LogicalName != "systemuser" || proposer.Id == Guid.Empty ||
                beneficiary?.LogicalName != "systemuser" || beneficiary.Id == Guid.Empty ||
                account?.LogicalName != "fmg_cashaccount" || account.Id == Guid.Empty ||
                option == null || change == null ||
                fingerprint == null || fingerprint.Length != 64 ||
                string.IsNullOrWhiteSpace(reason))
                throw new CashPlanningException("F5Q001", "Malformed immutable review evidence.");

            CashOperationKind kind;
            switch (option.Value)
            {
                case 797282000: kind = CashOperationKind.SettleReceipt; break;
                case 797282001: kind = CashOperationKind.InternalTransfer; break;
                case 797282002: kind = CashOperationKind.Reversal; break;
                default: throw new CashPlanningException("F5Q001", "Unknown grant operation in review.");
            }
            CashGrantChangeKind action;
            switch (change.Value)
            {
                case 797283100: action = CashGrantChangeKind.Grant; break;
                case 797283101: action = CashGrantChangeKind.Revoke; break;
                default: throw new CashPlanningException("F5Q001", "Unknown grant change kind.");
            }
            var created = DateTime.SpecifyKind(
                row.GetAttributeValue<DateTime>("createdon"), DateTimeKind.Utc);
            var expiry = DateTime.SpecifyKind(
                row.GetAttributeValue<DateTime>("fmg_expiresatutc"), DateTimeKind.Utc);
            var proposal = new CashGrantChangeRequest(id, beneficiary.Id, account.Id,
                kind, action, row.GetAttributeValue<bool>("fmg_allowshared"), reason, expiry);
            var trusted = CashGrantApprovalPolicy.Prepare(proposal, proposer.Id, created);
            if (!string.Equals(trusted.Fingerprint, fingerprint, StringComparison.Ordinal) ||
                !string.Equals(trusted.Reason, reason, StringComparison.Ordinal))
                throw new CashPlanningException("F5Q001", "Cash review fingerprint mismatch.");
            return new VerifiedPendingGrantReadback(trusted, row.RowVersion);
        }
    }

    public sealed class VerifiedPendingGrantReadback
    {
        internal VerifiedPendingGrantReadback(PendingCashGrantChange proposal, string rowVersion)
        {
            Proposal = proposal;
            RowVersion = rowVersion;
        }
        public PendingCashGrantChange Proposal { get; }
        public string RowVersion { get; }
    }
}
