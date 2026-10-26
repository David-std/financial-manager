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
    /// Source-only immutable reversal specification. The future server adapter
    /// MUST re-read the original operation, its complete legs, row version,
    /// current reversal state and the caller's current account permissions in
    /// the same transactional context. This code does NOT perform a write.
    /// </summary>
    public static class CashReversalPolicy
    {
        private static readonly Regex Key = new Regex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$",
            RegexOptions.CultureInvariant);

        public static PreparedCashReversal Prepare(
            PostedCashOperation original, CashReversalCommand request, TrustedCashActor actor)
        {
            if (original == null || request == null || actor == null ||
                original.OperationId == Guid.Empty || actor.SubjectId == Guid.Empty)
                throw new CashPlanningException("F5R001", "Valid original, request and actor are required.");
            if (!actor.CanPerform(CashOperationKind.Reversal))
                throw new CashPlanningException("F5R002", "Reversal business permission is required.");
            if (original.WasReversed || !original.IsPosted)
                throw new CashPlanningException("F5R003", "The original must be posted and not previously reversed.");
            if (original.Kind != CashOperationKind.SettleReceipt &&
                original.Kind != CashOperationKind.InternalTransfer)
                throw new CashPlanningException("F5R001", "Unsupported original operation kind.");
            if (request.RequestKey == null || !Key.IsMatch(request.RequestKey) ||
                string.IsNullOrWhiteSpace(request.Reason) ||
                request.Reason.Trim().Length > 300 ||
                request.ReversalDate == DateTime.MinValue ||
                request.ReversalDate.TimeOfDay != TimeSpan.Zero)
                throw new CashPlanningException("F5R001", "Valid key, date and reversal reason are required.");

            var legs = original.Legs?.ToArray();
            var required = original.Kind == CashOperationKind.InternalTransfer ? 2 : 1;
            if (legs == null || legs.Length != required)
                throw new CashPlanningException("F5R001", "All original posting legs must be present.");
            if (legs.Any(leg =>
                    leg == null || leg.PostingId == Guid.Empty || leg.AccountId == Guid.Empty ||
                    leg.SignedAmountPen == 0 ||
                    leg.SignedAmountPen < -CashOperationPolicy.MaximumAmountPen ||
                    leg.SignedAmountPen > CashOperationPolicy.MaximumAmountPen ||
                    decimal.Round(leg.SignedAmountPen, 2) != leg.SignedAmountPen ||
                    leg.PostedOn == DateTime.MinValue ||
                    leg.PostedOn.TimeOfDay != TimeSpan.Zero))
                throw new CashPlanningException("F5R001", "Malformed original ledger legs.");
            if (legs.Select(x => x.PostingId).Distinct().Count() != legs.Length ||
                legs.Select(x => x.PostedOn).Distinct().Count() != 1 ||
                request.ReversalDate < legs[0].PostedOn)
                throw new CashPlanningException("F5R001", "Duplicate postings or invalid reversal chronology.");
            if (original.Kind == CashOperationKind.SettleReceipt && legs[0].SignedAmountPen <= 0)
                throw new CashPlanningException("F5R001", "A settled receipt must have a positive original leg.");
            if (original.Kind == CashOperationKind.InternalTransfer &&
                (legs[0].AccountId == legs[1].AccountId ||
                 legs[0].SignedAmountPen + legs[1].SignedAmountPen != 0 ||
                 legs[0].SignedAmountPen * legs[1].SignedAmountPen >= 0))
                throw new CashPlanningException("F5R001", "The original transfer must have two balanced opposite legs.");
            if (legs.Any(x => !actor.CanPerformOnAccount(CashOperationKind.Reversal, x.AccountId)))
                throw new CashPlanningException("F5R002", "Reversal permission is missing for a source account.");

            var reversed = Array.AsReadOnly(legs.Select(leg => new ReversalCashLeg(
                leg.PostingId, leg.AccountId, -leg.SignedAmountPen, request.ReversalDate)).ToArray());
            return new PreparedCashReversal(original.OperationId, actor.SubjectId,
                request.RequestKey, Hash(original, request, actor.SubjectId), reversed);
        }

        private static string Hash(PostedCashOperation original,
            CashReversalCommand command, Guid actorId)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("F5ReversalV1");
                writer.Write(actorId.ToByteArray());
                writer.Write(original.OperationId.ToByteArray());
                writer.Write((int)original.Kind);
                writer.Write(command.RequestKey);
                writer.Write(command.ReversalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.Write(command.Reason.Trim());
                foreach (var leg in original.Legs.OrderBy(x => x.PostingId))
                {
                    writer.Write(leg.PostingId.ToByteArray());
                    writer.Write(leg.AccountId.ToByteArray());
                    writer.Write(leg.SignedAmountPen.ToString("0.00", CultureInfo.InvariantCulture));
                    writer.Write(leg.PostedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
                writer.Flush();
                using (var hash = SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }
    }

    public sealed class PostedCashOperation
    {
        public PostedCashOperation(Guid operationId, CashOperationKind kind,
            IEnumerable<PostedCashLeg> legs, bool isPosted, bool wasReversed)
        {
            OperationId = operationId;
            Kind = kind;
            Legs = Array.AsReadOnly((legs ?? Array.Empty<PostedCashLeg>()).ToArray());
            IsPosted = isPosted;
            WasReversed = wasReversed;
        }
        public Guid OperationId { get; }
        public CashOperationKind Kind { get; }
        public IReadOnlyList<PostedCashLeg> Legs { get; }
        public bool IsPosted { get; }
        public bool WasReversed { get; }
    }

    public sealed class PostedCashLeg
    {
        public PostedCashLeg(Guid postingId, Guid accountId, decimal signedAmountPen, DateTime postedOn)
        {
            PostingId = postingId;
            AccountId = accountId;
            SignedAmountPen = signedAmountPen;
            PostedOn = postedOn;
        }
        public Guid PostingId { get; }
        public Guid AccountId { get; }
        public decimal SignedAmountPen { get; }
        public DateTime PostedOn { get; }
    }

    public sealed class CashReversalCommand
    {
        public CashReversalCommand(string requestKey, DateTime reversalDate, string reason)
        {
            RequestKey = requestKey;
            ReversalDate = reversalDate;
            Reason = reason;
        }
        public string RequestKey { get; }
        public DateTime ReversalDate { get; }
        public string Reason { get; }
    }

    public sealed class ReversalCashLeg
    {
        public ReversalCashLeg(Guid reversesPostingId, Guid accountId, decimal amountPen, DateTime postedOn)
        {
            ReversesPostingId = reversesPostingId;
            AccountId = accountId;
            SignedAmountPen = amountPen;
            PostedOn = postedOn;
        }
        public Guid ReversesPostingId { get; }
        public Guid AccountId { get; }
        public decimal SignedAmountPen { get; }
        public DateTime PostedOn { get; }
    }

    public sealed class PreparedCashReversal
    {
        internal PreparedCashReversal(Guid originalOperationId, Guid actorId, string requestKey,
            string fingerprint, IReadOnlyList<ReversalCashLeg> legs)
        {
            OriginalOperationId = originalOperationId;
            ActorId = actorId;
            RequestKey = requestKey;
            Fingerprint = fingerprint;
            Legs = legs;
        }
        public Guid OriginalOperationId { get; }
        public Guid ActorId { get; }
        public string RequestKey { get; }
        public string Fingerprint { get; }
        public IReadOnlyList<ReversalCashLeg> Legs { get; }
    }
}
