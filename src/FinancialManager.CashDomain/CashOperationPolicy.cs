using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FinancialManager.CashDomain
{
    /// <summary>
    /// Pure, source-only acceptance contract. It is NOT an authorization
    /// service, an IPlugin implementation, or a deployed Dataverse API.
    /// The eventual server adapter must construct TrustedCashActor from the
    /// authenticated execution context and verified live account permissions.
    /// </summary>
    public static class CashOperationPolicy
    {
        public const decimal MaximumAmountPen = 1000000000m;
        private static readonly Regex RequestKeyPattern =
            new Regex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$", RegexOptions.CultureInvariant);

        public static PreparedCashOperation Prepare(CashOperationRequest request, TrustedCashActor actor)
        {
            if (request == null || actor == null)
                throw new CashPlanningException("F5P001", "A cash request and a verified actor are required.");
            if (actor.SubjectId == Guid.Empty)
                throw new CashPlanningException("F5P002", "An authenticated business actor is required.");
            if (!Enum.IsDefined(typeof(CashOperationKind), request.Kind) ||
                string.IsNullOrEmpty(request.RequestKey) ||
                !RequestKeyPattern.IsMatch(request.RequestKey))
                throw new CashPlanningException("F5P001", "Invalid cash operation type or request key.");
            if (request.AmountPen <= 0 || request.AmountPen > MaximumAmountPen ||
                decimal.Round(request.AmountPen, 2) != request.AmountPen)
                throw new CashPlanningException("F5P001", "Cash amount must be positive PEN with at most two decimal places.");
            if (request.Currency != "PEN" ||
                request.ValueDate == DateTime.MinValue ||
                request.ValueDate.TimeOfDay != TimeSpan.Zero)
                throw new CashPlanningException("F5P001", "Currency must be PEN and value date must be a calendar date.");
            if (request.SourceAccountId == Guid.Empty || (request.EvidenceReference ?? "").Length > 180)
                throw new CashPlanningException("F5P001", "Invalid cash account or evidence reference.");

            // This is a defensive second check, not a substitute for live
            // record/team/branch authorization by the server adapter.
            if (!actor.CanPerform(request.Kind) || !actor.CanAccessAccount(request.SourceAccountId))
                throw new CashPlanningException("F5P002", "The actor cannot perform this cash operation.");
            IReadOnlyList<CashLeg> legs;
            if (request.Kind == CashOperationKind.SettleReceipt)
            {
                if (request.DestinationAccountId.HasValue ||
                    string.IsNullOrWhiteSpace(request.EvidenceReference))
                    throw new CashPlanningException("F5P001", "Settled receipts require evidence and no destination account.");
                legs = Array.AsReadOnly(new[]
                {
                    new CashLeg(request.SourceAccountId, request.AmountPen, request.ValueDate)
                });
            }
            else
            {
                if (!request.DestinationAccountId.HasValue ||
                    request.DestinationAccountId.Value == Guid.Empty ||
                    request.DestinationAccountId.Value == request.SourceAccountId)
                    throw new CashPlanningException("F5P001", "A transfer requires two different cash accounts.");
                if (!actor.CanAccessAccount(request.DestinationAccountId.Value))
                    throw new CashPlanningException("F5P002", "The destination account is outside the actor's permissions.");
                legs = Array.AsReadOnly(new[]
                {
                    new CashLeg(request.SourceAccountId, -request.AmountPen, request.ValueDate),
                    new CashLeg(request.DestinationAccountId.Value, request.AmountPen, request.ValueDate)
                });
            }
            return new PreparedCashOperation(request.RequestKey, request.Kind, actor.SubjectId,
                                             Fingerprint(request, actor.SubjectId), legs);
        }

        /// <summary>
        /// Call only AFTER server-side reauthorization and lookup of a committed,
        /// immutable operation by an Active Dataverse alternate key.
        /// Races must be settled by the database unique constraint, not this method.
        /// </summary>
        public static Guid? ResolveRetry(PreparedCashOperation candidate, CommittedCashOperation? existing)
        {
            if (candidate == null)
                throw new CashPlanningException("F5P001", "A validated cash command is required.");
            if (existing == null) return null;
            if (existing.OperationId == Guid.Empty ||
                existing.RequestKey != candidate.RequestKey ||
                existing.Fingerprint != candidate.Fingerprint ||
                existing.SubjectId != candidate.SubjectId ||
                existing.Kind != candidate.Kind)
                throw new CashPlanningException("F5P003", "Conflicting reuse of a cash idempotency key.");
            return existing.OperationId;
        }

        private static string Fingerprint(CashOperationRequest request, Guid subjectId)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write((int)request.Kind);
                writer.Write(request.RequestKey);
                writer.Write(subjectId.ToByteArray());
                writer.Write(request.SourceAccountId.ToByteArray());
                writer.Write(request.DestinationAccountId.HasValue);
                if (request.DestinationAccountId.HasValue)
                    writer.Write(request.DestinationAccountId.Value.ToByteArray());
                writer.Write(request.AmountPen.ToString("0.00", CultureInfo.InvariantCulture));
                writer.Write(request.ValueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.Write(request.Currency);
                writer.Write((request.EvidenceReference ?? "").Trim());
                writer.Flush();
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }
    }

    public enum CashOperationKind { SettleReceipt = 1, InternalTransfer = 2 }

    public sealed class CashOperationRequest
    {
        public CashOperationRequest(CashOperationKind kind, string requestKey, Guid sourceAccountId,
            Guid? destinationAccountId, decimal amountPen, DateTime valueDate, string currency,
            string evidenceReference)
        {
            Kind = kind;
            RequestKey = requestKey;
            SourceAccountId = sourceAccountId;
            DestinationAccountId = destinationAccountId;
            AmountPen = amountPen;
            ValueDate = valueDate;
            Currency = currency;
            EvidenceReference = evidenceReference;
        }
        public CashOperationKind Kind { get; }
        public string RequestKey { get; }
        public Guid SourceAccountId { get; }
        public Guid? DestinationAccountId { get; }
        public decimal AmountPen { get; }
        public DateTime ValueDate { get; }
        public string Currency { get; }
        public string EvidenceReference { get; }
    }

    public sealed class TrustedCashActor
    {
        private readonly HashSet<Guid> allowedAccounts;
        private readonly HashSet<CashOperationKind> allowedOperations;

        /// <summary>
        /// These collections MUST come from verified Dataverse rights, team,
        /// branch and account-state checks. Never construct from client claims.
        /// </summary>
        public TrustedCashActor(Guid subjectId, IEnumerable<Guid> allowedAccountIds,
            IEnumerable<CashOperationKind> permittedOperations)
        {
            SubjectId = subjectId;
            allowedAccounts = new HashSet<Guid>(allowedAccountIds ?? Array.Empty<Guid>());
            allowedOperations = new HashSet<CashOperationKind>(permittedOperations ?? Array.Empty<CashOperationKind>());
        }
        public Guid SubjectId { get; }
        public bool CanAccessAccount(Guid accountId) => allowedAccounts.Contains(accountId);
        public bool CanPerform(CashOperationKind kind) => allowedOperations.Contains(kind);
    }

    public sealed class CashLeg
    {
        public CashLeg(Guid accountId, decimal signedAmountPen, DateTime valueDate)
        {
            AccountId = accountId;
            SignedAmountPen = signedAmountPen;
            ValueDate = valueDate;
        }
        public Guid AccountId { get; }
        public decimal SignedAmountPen { get; }
        public DateTime ValueDate { get; }
    }

    public sealed class PreparedCashOperation
    {
        internal PreparedCashOperation(string requestKey, CashOperationKind kind, Guid subjectId,
            string fingerprint, IReadOnlyList<CashLeg> legs)
        {
            RequestKey = requestKey;
            Kind = kind;
            SubjectId = subjectId;
            Fingerprint = fingerprint;
            Legs = legs;
        }
        public string RequestKey { get; }
        public CashOperationKind Kind { get; }
        public Guid SubjectId { get; }
        public string Fingerprint { get; }
        public IReadOnlyList<CashLeg> Legs { get; }
    }

    public sealed class CommittedCashOperation
    {
        public CommittedCashOperation(Guid operationId, string requestKey, CashOperationKind kind,
            Guid subjectId, string fingerprint)
        {
            OperationId = operationId;
            RequestKey = requestKey;
            Kind = kind;
            SubjectId = subjectId;
            Fingerprint = fingerprint;
        }
        public Guid OperationId { get; }
        public string RequestKey { get; }
        public CashOperationKind Kind { get; }
        public Guid SubjectId { get; }
        public string Fingerprint { get; }
    }

    public sealed class CashPlanningException : InvalidOperationException
    {
        public CashPlanningException(string code, string message) : base(code + ": " + message)
        {
            Code = code;
        }
        public string Code { get; }
    }
}
