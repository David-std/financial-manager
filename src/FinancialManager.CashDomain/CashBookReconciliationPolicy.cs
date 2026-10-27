using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FinancialManager.CashDomain
{
    /// <summary>
    /// Pure specification for posted PEN cash balance and bank reconciliation.
    /// All inputs must be loaded and authorized by a trusted Dataverse adapter;
    /// this policy does not create opening, bank or reconciliation records.
    /// A cash transfer is represented by its signed, account-specific posting,
    /// never classified as an income/expense by this balance computation.
    /// </summary>
    public static class CashBookReconciliationPolicy
    {
        public static CashBookReconciliation Prepare(
            Guid verifiedAccountId, CashOpeningSnapshot opening,
            IReadOnlyList<CashMovementSnapshot> postedMovements,
            CashBankStatementSnapshot statement)
        {
            if (verifiedAccountId == Guid.Empty || opening == null ||
                postedMovements == null || statement == null ||
                opening.OpeningId == Guid.Empty || statement.StatementId == Guid.Empty ||
                opening.AccountId != verifiedAccountId ||
                statement.AccountId != verifiedAccountId ||
                !ValidDate(opening.EffectiveDate) || !ValidDate(statement.AsOfDate) ||
                opening.EffectiveDate > statement.AsOfDate ||
                !ValidAmount(opening.AmountPen) || opening.AmountPen < 0m ||
                !ValidAmount(statement.ClosingBalancePen) ||
                string.IsNullOrWhiteSpace(opening.ApprovalReference) ||
                string.IsNullOrWhiteSpace(statement.BankDocumentReference) ||
                opening.Currency != "PEN" || statement.Currency != "PEN")
                throw new CashPlanningException("F5B001",
                    "A separately approved PEN opening and bank evidence are required.");

            var ids = new HashSet<Guid>();
            decimal amount = opening.AmountPen;
            foreach (var line in postedMovements)
            {
                if (line == null || line.PostingId == Guid.Empty ||
                    !ids.Add(line.PostingId) ||
                    line.AccountId != verifiedAccountId ||
                    !line.IsPosted || line.Currency != "PEN" ||
                    !ValidDate(line.ValueDate) ||
                    line.ValueDate < opening.EffectiveDate ||
                    line.ValueDate > statement.AsOfDate ||
                    !ValidAmount(line.SignedAmountPen) || line.SignedAmountPen == 0m ||
                    string.IsNullOrWhiteSpace(line.SourceOperationReference))
                    throw new CashPlanningException("F5B002",
                        "Only unique, complete and posted account ledger legs can affect cash balance.");
                amount += line.SignedAmountPen;
                if (!ValidAmount(amount))
                    throw new CashPlanningException("F5B003", "Cash balance exceeds accepted PEN precision.");
            }
            var difference = statement.ClosingBalancePen - amount;
            if (!ValidAmount(difference))
                throw new CashPlanningException("F5B003", "Bank difference exceeds accepted PEN precision.");
            var fingerprint = Fingerprint(verifiedAccountId, opening, postedMovements,
                statement, amount, difference);
            return new CashBookReconciliation(verifiedAccountId, opening.OpeningId,
                statement.StatementId, statement.AsOfDate, amount,
                statement.ClosingBalancePen, difference, difference == 0m,
                ids.Count, fingerprint);
        }

        private static bool ValidDate(DateTime d) =>
            d != DateTime.MinValue && d.TimeOfDay == TimeSpan.Zero &&
            d.Kind != DateTimeKind.Local;

        private static bool ValidAmount(decimal d) =>
            d >= -CashOperationPolicy.MaximumAmountPen &&
            d <= CashOperationPolicy.MaximumAmountPen &&
            decimal.Round(d, 2) == d;

        private static string Fingerprint(Guid accountId, CashOpeningSnapshot opening,
            IReadOnlyList<CashMovementSnapshot> movements,
            CashBankStatementSnapshot statement, decimal book, decimal diff)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("F5CashBankReconciliationV1");
                writer.Write(accountId.ToByteArray());
                writer.Write(opening.OpeningId.ToByteArray());
                writer.Write(opening.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.Write(opening.AmountPen.ToString("0.00", CultureInfo.InvariantCulture));
                writer.Write(opening.ApprovalReference.Trim());
                writer.Write(statement.StatementId.ToByteArray());
                writer.Write(statement.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.Write(statement.ClosingBalancePen.ToString("0.00", CultureInfo.InvariantCulture));
                writer.Write(statement.BankDocumentReference.Trim());
                foreach (var movement in movements.OrderBy(m => m.PostingId))
                {
                    writer.Write(movement.PostingId.ToByteArray());
                    writer.Write(movement.AccountId.ToByteArray());
                    writer.Write(movement.ValueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    writer.Write(movement.SignedAmountPen.ToString("0.00", CultureInfo.InvariantCulture));
                    writer.Write(movement.SourceOperationReference);
                }
                writer.Write(book.ToString("0.00", CultureInfo.InvariantCulture));
                writer.Write(diff.ToString("0.00", CultureInfo.InvariantCulture));
                writer.Flush();
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }
    }

    public sealed class CashOpeningSnapshot
    {
        public CashOpeningSnapshot(Guid openingId, Guid accountId, DateTime date,
            decimal amountPen, string currency, string approvalReference)
        {
            OpeningId=openingId;AccountId=accountId;EffectiveDate=date;
            AmountPen=amountPen;Currency=currency;ApprovalReference=approvalReference;
        }
        public Guid OpeningId { get; }
        public Guid AccountId { get; }
        public DateTime EffectiveDate { get; }
        public decimal AmountPen { get; }
        public string Currency { get; }
        public string ApprovalReference { get; }
    }
    public sealed class CashMovementSnapshot
    {
        public CashMovementSnapshot(Guid postingId, Guid accountId, DateTime date,
            decimal signedAmountPen, bool isPosted, string currency,
            string sourceOperationReference)
        {
            PostingId=postingId;AccountId=accountId;ValueDate=date;
            SignedAmountPen=signedAmountPen;IsPosted=isPosted;
            Currency=currency;SourceOperationReference=sourceOperationReference;
        }
        public Guid PostingId { get; }
        public Guid AccountId { get; }
        public DateTime ValueDate { get; }
        public decimal SignedAmountPen { get; }
        public bool IsPosted { get; }
        public string Currency { get; }
        public string SourceOperationReference { get; }
    }
    public sealed class CashBankStatementSnapshot
    {
        public CashBankStatementSnapshot(Guid statementId, Guid accountId,
            DateTime asOfDate, decimal closingBalancePen,
            string currency, string bankDocumentReference)
        {
            StatementId=statementId;AccountId=accountId;AsOfDate=asOfDate;
            ClosingBalancePen=closingBalancePen;Currency=currency;
            BankDocumentReference=bankDocumentReference;
        }
        public Guid StatementId { get; }
        public Guid AccountId { get; }
        public DateTime AsOfDate { get; }
        public decimal ClosingBalancePen { get; }
        public string Currency { get; }
        public string BankDocumentReference { get; }
    }
    public sealed class CashBookReconciliation
    {
        internal CashBookReconciliation(Guid accountId, Guid openingId, Guid statementId,
            DateTime asOf, decimal bookBalance, decimal bankBalance,
            decimal difference, bool reconciled, int postingCount, string fingerprint)
        {
            AccountId=accountId;OpeningId=openingId;StatementId=statementId;
            AsOfDate=asOf;BookBalancePen=bookBalance;BankBalancePen=bankBalance;
            DifferencePen=difference;IsReconciled=reconciled;
            PostingCount=postingCount;Fingerprint=fingerprint;
        }
        public Guid AccountId { get; }
        public Guid OpeningId { get; }
        public Guid StatementId { get; }
        public DateTime AsOfDate { get; }
        public decimal BookBalancePen { get; }
        public decimal BankBalancePen { get; }
        public decimal DifferencePen { get; }
        public bool IsReconciled { get; }
        public int PostingCount { get; }
        public string Fingerprint { get; }
    }
}
