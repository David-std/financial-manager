import unittest
from datetime import date, timedelta
from decimal import Decimal

from financial_domain.cash_ledger import (
    ApprovedOpening,
    CashPosting,
    LedgerIntegrityError,
    book_balance,
    reconcile,
    validate_postings,
)

DAY = date(2026, 10, 24)
D = Decimal


def opening(account="bank-a", amount="1000.00"):
    return ApprovedOpening(account, DAY, D(amount), True)


def payment(value="-125.50"):
    return CashPosting("p1", "expense1", "bank-a", DAY, D(value), "payment")


def receipt(value="400.00"):
    return CashPosting("r1", "collection1", "bank-a", DAY, D(value), "receipt")


def transfer():
    return [
        CashPosting("t-out", "transfer1", "bank-a", DAY, D("-70.00"), "transfer"),
        CashPosting("t-in", "transfer1", "bank-b", DAY, D("70.00"), "transfer"),
    ]


class CashLedgerSpecificationTests(unittest.TestCase):
    def test_opening_plus_settled_receipt_less_posted_payment(self):
        self.assertEqual(book_balance("bank-a", DAY, [opening()], [receipt(), payment()]), D("1274.50"))

    def test_internal_transfer_does_not_create_revenue_or_expense(self):
        legs = transfer()
        self.assertEqual(sum((x.amount_pen for x in legs), D("0")), D("0"))
        self.assertEqual(book_balance("bank-a", DAY, [opening()], legs), D("930.00"))
        self.assertEqual(book_balance("bank-b", DAY, [opening("bank-b")], legs), D("1070.00"))

    def test_missing_transfer_leg_is_rejected(self):
        with self.assertRaisesRegex(LedgerIntegrityError, "two different"):
            validate_postings(transfer()[:1])

    def test_transfer_to_same_account_is_rejected(self):
        a, b = transfer()
        with self.assertRaisesRegex(LedgerIntegrityError, "two different"):
            validate_postings([a, CashPosting("t-in", "transfer1", a.account_id, DAY, b.amount_pen, "transfer")])

    def test_unbalanced_transfer_is_rejected(self):
        a, b = transfer()
        b = CashPosting("t-in", "transfer1", b.account_id, DAY, D("69.00"), "transfer")
        with self.assertRaisesRegex(LedgerIntegrityError, "balance to zero"):
            validate_postings([a, b])

    def test_duplicate_request_legs_rejected(self):
        p = payment()
        with self.assertRaisesRegex(LedgerIntegrityError, "Duplicate posting"):
            validate_postings([p, p])

    def test_duplicate_external_operation_id_rejected(self):
        p = payment()
        again = CashPosting("p2", p.operation_id, "bank-a", DAY, D("-30.00"), "payment")
        with self.assertRaisesRegex(LedgerIntegrityError, "one cash leg"):
            validate_postings([p, again])

    def test_reversal_of_a_payment_restores_cash_only_on_reversal_date(self):
        p = payment()
        reverse_day = DAY + timedelta(days=2)
        reversal = CashPosting("cancel-p1", "correction1", "bank-a", reverse_day,
                               D("125.50"), "reversal", reverses_posting_id=p.posting_id)
        entries = [p, reversal]
        self.assertEqual(book_balance("bank-a", DAY, [opening()], entries), D("874.50"))
        self.assertEqual(book_balance("bank-a", reverse_day, [opening()], entries), D("1000.00"))

    def test_transfer_reversal_is_atomic(self):
        a, b = transfer()
        reverse_day = DAY + timedelta(days=1)
        one = CashPosting("rev-a", "cancel-transfer1", "bank-a", reverse_day,
                          D("70.00"), "reversal", reverses_posting_id=a.posting_id)
        with self.assertRaisesRegex(LedgerIntegrityError, "entire transfer"):
            validate_postings([a, b, one])
        two = CashPosting("rev-b", "cancel-transfer1", "bank-b", reverse_day,
                          D("-70.00"), "reversal", reverses_posting_id=b.posting_id)
        validate_postings([a, b, one, two])
        self.assertEqual(book_balance("bank-a", reverse_day, [opening()], [a, b, one, two]), D("1000.00"))

    def test_cannot_reverse_twice(self):
        p = payment()
        a = CashPosting("rev1", "c1", "bank-a", DAY, D("125.50"), "reversal", reverses_posting_id="p1")
        b = CashPosting("rev2", "c2", "bank-a", DAY, D("125.50"), "reversal", reverses_posting_id="p1")
        with self.assertRaisesRegex(LedgerIntegrityError, "reversed twice"):
            validate_postings([p, a, b])

    def test_unapproved_or_missing_opening_forbids_balance(self):
        pending = ApprovedOpening("bank-a", DAY, D("1000"), False)
        for openings in ([], [pending]):
            with self.subTest(openings=openings):
                with self.assertRaisesRegex(LedgerIntegrityError, "approved opening"):
                    book_balance("bank-a", DAY, openings, [])

    def test_duplicate_approved_opening_forbids_balance(self):
        with self.assertRaisesRegex(LedgerIntegrityError, "Exactly one"):
            book_balance("bank-a", DAY, [opening(), opening(amount="2000")], [])

    def test_payment_or_receipt_has_expected_direction(self):
        with self.assertRaisesRegex(LedgerIntegrityError, "increases cash"):
            receipt("-20.00")
        with self.assertRaisesRegex(LedgerIntegrityError, "decreases cash"):
            payment("20.00")

    def test_amount_never_uses_binary_float_or_extra_precision(self):
        with self.assertRaises(LedgerIntegrityError):
            CashPosting("bad", "bad", "a", DAY, 0.1, "receipt")
        with self.assertRaises(LedgerIntegrityError):
            CashPosting("bad", "bad", "a", DAY, D("0.001"), "receipt")

    def test_reconciliation_needs_external_bank_balance_and_date(self):
        result = reconcile("bank-a", DAY, D("1250"), [opening()], [receipt(), payment()])
        self.assertFalse(result.reconciled)
        self.assertEqual(result.difference_pen, D("-24.50"))
        good = reconcile("bank-a", DAY, D("1274.50"), [opening()], [receipt(), payment()])
        self.assertTrue(good.reconciled)
        self.assertEqual(good.difference_pen, D("0"))

    def test_future_transaction_not_counted_at_cutoff(self):
        p = CashPosting("late", "late", "bank-a", DAY + timedelta(days=1),
                        D("-45.00"), "payment")
        self.assertEqual(book_balance("bank-a", DAY, [opening()], [p]), D("1000.00"))


if __name__ == "__main__":
    unittest.main()
