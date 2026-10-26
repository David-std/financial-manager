using System;
using System.Collections.Generic;
using System.Linq;

namespace FinancialManager.CashDomain
{
    /// <summary>
    /// Server-only orchestration policy. The Dataverse adapter must provide
    /// authoritative live entitlements and a transaction-participating store.
    /// No entity writes occur in this library itself.
    /// </summary>
    public sealed class CashExecutionCoordinator
    {
        private readonly IServerCashAuthorization authorization;
        private readonly ICashLedgerTransaction ledger;

        public CashExecutionCoordinator(IServerCashAuthorization authorization,
            ICashLedgerTransaction ledger)
        {
            this.authorization = authorization ??
                throw new ArgumentNullException(nameof(authorization));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public CashExecutionResult Execute(CashRuntimeEvidence runtime, CashOperationRequest request)
        {
            CashRuntimePolicy.Require(runtime);
            if (request == null)
                throw new CashPlanningException("F5E001", "Missing operation request.");

            // Authorize every attempt, including an identical idempotent retry.
            // The implementation MUST load user/team membership and account
            // state from the server, never from client-supplied branch lists.
            var actor = authorization.Authorize(runtime.InitiatingUserId, request);
            if (actor == null || actor.SubjectId != runtime.InitiatingUserId)
                throw new CashPlanningException("F5E002", "Actor evidence does not match the platform initiator.");
            var plan = CashOperationPolicy.Prepare(request, actor);
            var existing = ledger.FindCommittedByKey(plan.Kind, plan.RequestKey);
            var replay = CashOperationPolicy.ResolveRetry(plan, existing);
            if (replay.HasValue)
            {
                // Do not acknowledge an already-committed operation until
                // its entire ledger is still intact. A matching fingerprint
                // in the header alone is insufficient evidence.
                CashExecutionIntegrity.VerifyRetry(plan, replay.Value,
                    ledger.ReadCompleteLedger(replay.Value));
                return new CashExecutionResult(replay.Value, true);
            }

            // A DB alternate key MUST serialize concurrent same-key creates.
            // On collision the entire transaction fails. A *new invocation*
            // may read the committed original and apply ResolveRetry safely.
            var operationId = ledger.CreateHeader(plan);
            if (operationId == Guid.Empty)
                throw new CashPlanningException("F5E003", "The operation header was not created.");

            var postingIds = new List<Guid>();
            foreach (var leg in plan.Legs)
            {
                var postingId = ledger.CreatePosting(operationId, plan, leg, postingIds.Count);
                if (postingId == Guid.Empty || postingIds.Contains(postingId))
                    throw new CashPlanningException("F5E003", "Invalid ledger posting identity.");
                postingIds.Add(postingId);
            }
            // Final status must not be reachable with a partially written set.
            ledger.CompleteHeader(operationId, plan.Fingerprint, postingIds);
            var committed = ledger.ReadCompleteLedger(operationId);
            CashExecutionIntegrity.Verify(plan, operationId, postingIds, committed);
            return new CashExecutionResult(operationId, false);
        }
    }

    public static class CashRuntimePolicy
    {
        public static void Require(CashRuntimeEvidence context)
        {
            if (context == null || context.Stage != 30 || context.Mode != 0 ||
                !context.IsInTransaction || context.InitiatingUserId == Guid.Empty ||
                context.ExecutionUserId == Guid.Empty)
                throw new CashPlanningException("F5E001", "A verified synchronous transactional MainOperation is required.");
            // Cross-principal delegated execution needs an independently
            // reviewed protocol and explicit human audit attribution.
            if (context.InitiatingUserId != context.ExecutionUserId)
                throw new CashPlanningException("F5E002", "Unapproved impersonation or delegated identity.");
        }
    }

    public sealed class CashRuntimeEvidence
    {
        public CashRuntimeEvidence(int stage, int mode, bool inTransaction,
            Guid initiatingUserId, Guid executionUserId)
        {
            Stage = stage; Mode = mode; IsInTransaction = inTransaction;
            InitiatingUserId = initiatingUserId; ExecutionUserId = executionUserId;
        }
        public int Stage { get; }
        public int Mode { get; }
        public bool IsInTransaction { get; }
        public Guid InitiatingUserId { get; }
        public Guid ExecutionUserId { get; }
    }

    /// <summary>Only a secured Dataverse server adapter can implement this.</summary>
    public interface IServerCashAuthorization
    {
        TrustedCashActor Authorize(Guid platformInitiatingUserId, CashOperationRequest request);
    }

    /// <summary>
    /// Every method MUST execute in the same Dataverse platform transaction.
    /// Fingerprint/initiator must be durably stored with a UNIQUE request key;
    /// write permissions are never conferred by this interface.
    /// </summary>
    public interface ICashLedgerTransaction
    {
        CommittedCashOperation? FindCommittedByKey(CashOperationKind kind, string requestKey);
        Guid CreateHeader(PreparedCashOperation plan);
        Guid CreatePosting(Guid operationId, PreparedCashOperation plan, CashLeg leg, int index);
        void CompleteHeader(Guid operationId, string fingerprint, IReadOnlyList<Guid> postingIds);
        IReadOnlyList<CashPostingSnapshot> ReadCompleteLedger(Guid operationId);
    }

    public sealed class CashPostingSnapshot
    {
        public CashPostingSnapshot(Guid postingId, Guid operationId, Guid accountId,
            decimal amountPen, DateTime valueDate)
        {
            PostingId = postingId; OperationId = operationId; AccountId = accountId;
            SignedAmountPen = amountPen; ValueDate = valueDate;
        }
        public Guid PostingId { get; }
        public Guid OperationId { get; }
        public Guid AccountId { get; }
        public decimal SignedAmountPen { get; }
        public DateTime ValueDate { get; }
    }

    public sealed class CashExecutionResult
    {
        public CashExecutionResult(Guid operationId, bool wasReplay)
        {
            OperationId = operationId; WasReplay = wasReplay;
        }
        public Guid OperationId { get; }
        public bool WasReplay { get; }
    }

    public static class CashExecutionIntegrity
    {
        public static void VerifyRetry(PreparedCashOperation plan, Guid operationId,
            IReadOnlyList<CashPostingSnapshot> existing)
        {
            if (plan == null || existing == null || existing.Count != plan.Legs.Count)
                throw new CashPlanningException("F5E004", "Committed retry has incomplete postings.");
            var ordered = new List<Guid>();
            foreach (var leg in plan.Legs)
            {
                var matches = existing.Where(p => p != null &&
                    p.AccountId == leg.AccountId &&
                    p.SignedAmountPen == leg.SignedAmountPen &&
                    p.ValueDate == leg.ValueDate).ToArray();
                if (matches.Length != 1)
                    throw new CashPlanningException("F5E004", "Committed retry posting does not match immutable input.");
                ordered.Add(matches[0].PostingId);
            }
            Verify(plan, operationId, ordered, existing);
        }

        public static void Verify(PreparedCashOperation plan, Guid operationId,
            IReadOnlyList<Guid> postingIds, IReadOnlyList<CashPostingSnapshot> actual)
        {
            if (plan == null || operationId == Guid.Empty || postingIds == null ||
                actual == null || actual.Count != plan.Legs.Count ||
                postingIds.Count != plan.Legs.Count ||
                postingIds.Any(id => id == Guid.Empty) ||
                postingIds.Distinct().Count() != postingIds.Count ||
                actual.Any(p => p == null || p.OperationId != operationId ||
                    p.PostingId == Guid.Empty) ||
                actual.Select(p => p.PostingId).Distinct().Count() != actual.Count)
                throw new CashPlanningException("F5E004", "Incomplete or invalid committed cash ledger.");

            for (var i = 0; i < plan.Legs.Count; i++)
            {
                var expected = plan.Legs[i];
                var posting = actual.SingleOrDefault(p => p.PostingId == postingIds[i]);
                if (posting == null || posting.AccountId != expected.AccountId ||
                    posting.SignedAmountPen != expected.SignedAmountPen ||
                    posting.ValueDate != expected.ValueDate)
                    throw new CashPlanningException("F5E004", "Committed cash leg differs from planned operation.");
            }
            if (plan.Kind == CashOperationKind.InternalTransfer &&
                actual.Sum(p => p.SignedAmountPen) != 0m)
                throw new CashPlanningException("F5E004", "Unbalanced committed transfer.");
        }
    }
}
