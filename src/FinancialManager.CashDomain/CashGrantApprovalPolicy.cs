using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FinancialManager.CashDomain
{
    public enum CashGrantChangeKind { Grant = 1, Revoke = 2 }

    /// <summary>
    /// Immutable authoring intent. The server adapter MUST obtain the
    /// requester from InitiatingUserId, not a field submitted by the UI.
    /// Request storage must have an Active unique key and optimistic concurrency.
    /// </summary>
    public sealed class CashGrantChangeRequest
    {
        public CashGrantChangeRequest(Guid requestId, Guid beneficiaryUserId,
            Guid cashAccountId, CashOperationKind operation, CashGrantChangeKind change,
            bool explicitSharedAccount, string reason, DateTime expiresUtc)
        {
            RequestId = requestId; BeneficiaryUserId = beneficiaryUserId;
            CashAccountId = cashAccountId; Operation = operation; Change = change;
            ExplicitSharedAccount = explicitSharedAccount; Reason = reason;
            ExpiresUtc = expiresUtc;
        }
        public Guid RequestId { get; }
        public Guid BeneficiaryUserId { get; }
        public Guid CashAccountId { get; }
        public CashOperationKind Operation { get; }
        public CashGrantChangeKind Change { get; }
        public bool ExplicitSharedAccount { get; }
        public string Reason { get; }
        public DateTime ExpiresUtc { get; }
    }

    public sealed class PendingCashGrantChange
    {
        internal PendingCashGrantChange(Guid requestId, Guid proposerId,
            Guid beneficiaryId, Guid accountId, CashOperationKind operation,
            CashGrantChangeKind change, bool explicitShared, string reason,
            DateTime createdUtc, DateTime expiresUtc, string fingerprint)
        {
            RequestId = requestId; ProposerId = proposerId; BeneficiaryId = beneficiaryId;
            AccountId = accountId; Operation = operation; Change = change;
            ExplicitShared = explicitShared; Reason = reason;
            CreatedUtc = createdUtc; ExpiresUtc = expiresUtc; Fingerprint = fingerprint;
        }

        public Guid RequestId { get; }
        public Guid ProposerId { get; }
        public Guid BeneficiaryId { get; }
        public Guid AccountId { get; }
        public CashOperationKind Operation { get; }
        public CashGrantChangeKind Change { get; }
        public bool ExplicitShared { get; }
        public string Reason { get; }
        public DateTime CreatedUtc { get; }
        public DateTime ExpiresUtc { get; }
        public string Fingerprint { get; }
    }

    public sealed class ApprovedCashGrantChange
    {
        internal ApprovedCashGrantChange(PendingCashGrantChange request,
            Guid approverId, DateTime approvedUtc)
        {
            Request = request; ApproverId = approverId; ApprovedUtc = approvedUtc;
        }
        public PendingCashGrantChange Request { get; }
        public Guid ApproverId { get; }
        public DateTime ApprovedUtc { get; }
    }

    public static class CashGrantApprovalPolicy
    {
        /// <summary>
        /// The caller must verify the proposer has a dedicated proposal role
        /// using server-side Dataverse privilege/role evidence. This method
        /// never grants cash access and creates no Dataverse records.
        /// </summary>
        public static PendingCashGrantChange Prepare(CashGrantChangeRequest request,
            Guid serverVerifiedProposer, DateTime serverUtcNow)
        {
            if (request == null || request.RequestId == Guid.Empty ||
                request.BeneficiaryUserId == Guid.Empty || request.CashAccountId == Guid.Empty ||
                serverVerifiedProposer == Guid.Empty ||
                serverVerifiedProposer == request.BeneficiaryUserId ||
                !Enum.IsDefined(typeof(CashOperationKind), request.Operation) ||
                !Enum.IsDefined(typeof(CashGrantChangeKind), request.Change) ||
                serverUtcNow.Kind != DateTimeKind.Utc ||
                request.ExpiresUtc.Kind != DateTimeKind.Utc ||
                request.ExpiresUtc <= serverUtcNow ||
                request.ExpiresUtc > serverUtcNow.AddDays(90))
                throw new CashPlanningException("F5A010", "Invalid independent grant proposal.");

            var reason = (request.Reason ?? string.Empty).Trim();
            if (reason.Length < 10 || reason.Length > 300)
                throw new CashPlanningException("F5A010", "An accountable grant justification is required.");
            return new PendingCashGrantChange(request.RequestId, serverVerifiedProposer,
                request.BeneficiaryUserId, request.CashAccountId, request.Operation,
                request.Change, request.ExplicitSharedAccount, reason,
                serverUtcNow, request.ExpiresUtc, Fingerprint(request, serverVerifiedProposer, reason));
        }

        /// <summary>
        /// The adapter MUST re-read the immutable pending row and its RowVersion,
        /// and independently verify approver's CURRENT financial-grant approval
        /// privilege before calling this method. Never trust client claims.
        /// </summary>
        public static ApprovedCashGrantChange Approve(PendingCashGrantChange proposal,
            Guid serverVerifiedApprover, DateTime serverUtcNow)
        {
            if (proposal == null || serverVerifiedApprover == Guid.Empty ||
                serverVerifiedApprover == proposal.ProposerId ||
                serverVerifiedApprover == proposal.BeneficiaryId ||
                proposal.RequestId == Guid.Empty || proposal.AccountId == Guid.Empty ||
                proposal.Fingerprint == null || proposal.Fingerprint.Length != 64 ||
                serverUtcNow.Kind != DateTimeKind.Utc ||
                serverUtcNow < proposal.CreatedUtc || serverUtcNow >= proposal.ExpiresUtc)
                throw new CashPlanningException("F5A011",
                    "Independent, current financial approval evidence required.");
            return new ApprovedCashGrantChange(proposal, serverVerifiedApprover, serverUtcNow);
        }

        private static string Fingerprint(CashGrantChangeRequest r, Guid proposer, string reason)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(r.RequestId.ToByteArray());
                writer.Write(proposer.ToByteArray());
                writer.Write(r.BeneficiaryUserId.ToByteArray());
                writer.Write(r.CashAccountId.ToByteArray());
                writer.Write((int)r.Operation);
                writer.Write((int)r.Change);
                writer.Write(r.ExplicitSharedAccount);
                writer.Write(reason);
                writer.Write(r.ExpiresUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                writer.Flush();
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }
    }
}
