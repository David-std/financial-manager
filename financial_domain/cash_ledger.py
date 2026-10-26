from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from decimal import Decimal
from typing import Literal, Sequence

# Specification/reference model ONLY. The production source of truth will be
# Dataverse plug-ins, audited immutable postings and authorized ledger accounts.
Kind = Literal["receipt", "payment", "transfer", "reversal"]


class LedgerIntegrityError(ValueError):
    pass


def _money(value: Decimal) -> Decimal:
    if not isinstance(value, Decimal) or not value.is_finite():
        raise LedgerIntegrityError("Amounts must be finite Decimal values, never floats")
    if value != value.quantize(Decimal("0.01")):
        raise LedgerIntegrityError("PEN postings must have at most two decimal places")
    return value


@dataclass(frozen=True)
class ApprovedOpening:
    account_id: str
    effective_on: date
    amount_pen: Decimal
    approved: bool

    def __post_init__(self) -> None:
        _money(self.amount_pen)
        if not self.account_id:
            raise LedgerIntegrityError("An opening balance requires an account")


@dataclass(frozen=True)
class CashPosting:
    posting_id: str
    operation_id: str
    account_id: str
    posted_on: date
    amount_pen: Decimal
    kind: Kind
    reverses_posting_id: str | None = None
    currency: str = "PEN"

    def __post_init__(self) -> None:
        _money(self.amount_pen)
        if not self.posting_id or not self.operation_id or not self.account_id:
            raise LedgerIntegrityError("Each posting requires IDs and its cash account")
        if self.currency != "PEN":
            raise LedgerIntegrityError("A currency conversion cannot be inferred")
        if not self.amount_pen:
            raise LedgerIntegrityError("A posting cannot have zero value")
        if self.kind not in ("receipt", "payment", "transfer", "reversal"):
            raise LedgerIntegrityError("Unsupported posting type")
        if self.kind == "receipt" and self.amount_pen < 0:
            raise LedgerIntegrityError("Settled receipt increases cash")
        if self.kind == "payment" and self.amount_pen > 0:
            raise LedgerIntegrityError("Posted payment decreases cash")
        if (self.kind == "reversal") != bool(self.reverses_posting_id):
            raise LedgerIntegrityError("Reversals require a referenced original posting")


def validate_postings(postings: Sequence[CashPosting]) -> None:
    by_id: dict[str, CashPosting] = {}
    by_op: dict[str, list[CashPosting]] = {}
    for posting in postings:
        if posting.posting_id in by_id:
            raise LedgerIntegrityError("Duplicate posting ID")
        by_id[posting.posting_id] = posting
        by_op.setdefault(posting.operation_id, []).append(posting)

    reversed_by: dict[str, CashPosting] = {}
    for posting in postings:
        if posting.kind != "reversal":
            continue
        original_id = posting.reverses_posting_id
        if original_id in reversed_by:
            raise LedgerIntegrityError("A posting cannot be reversed twice")
        original = by_id.get(original_id or "")
        if original is None or original.kind == "reversal":
            raise LedgerIntegrityError("A reversal must reference a real original posting")
        if posting.account_id != original.account_id or posting.amount_pen != -original.amount_pen:
            raise LedgerIntegrityError("A reversal must offset the original cash account exactly")
        if posting.posted_on < original.posted_on:
            raise LedgerIntegrityError("A reversal cannot predate its original posting")
        reversed_by[original_id] = posting

    for operation_id, legs in by_op.items():
        kinds = {p.kind for p in legs}
        if len(kinds) != 1:
            raise LedgerIntegrityError("Do not mix kinds inside one operation")
        kind = legs[0].kind
        if kind in ("receipt", "payment"):
            if len(legs) != 1:
                raise LedgerIntegrityError("A receipt/payment operation has one cash leg")
        elif kind == "transfer":
            if len(legs) != 2 or len({p.account_id for p in legs}) != 2:
                raise LedgerIntegrityError("A transfer must have two different cash accounts")
            if sum((p.amount_pen for p in legs), Decimal("0")) != 0:
                raise LedgerIntegrityError("A transfer must balance to zero cash")
            if len({p.posted_on for p in legs}) != 1:
                raise LedgerIntegrityError("Both transfer legs must have the same posting date")
        elif kind == "reversal":
            original_legs = [by_id[p.reverses_posting_id or ""] for p in legs]
            original_ops = {p.operation_id for p in original_legs}
            if len(original_ops) != 1:
                raise LedgerIntegrityError("A reversal must cover one original operation")
            original = by_op[next(iter(original_ops))]
            if len(original) != len(legs):
                raise LedgerIntegrityError("An entire transfer must be reversed atomically")
            if len({p.posted_on for p in legs}) != 1:
                raise LedgerIntegrityError("The reversal cash legs must post on the same day")


def book_balance(
    account_id: str,
    as_of: date,
    openings: Sequence[ApprovedOpening],
    postings: Sequence[CashPosting],
) -> Decimal:
    validate_postings(postings)
    candidates = [x for x in openings if x.account_id == account_id and x.effective_on <= as_of]
    candidates = [x for x in candidates if x.approved]
    if len(candidates) != 1:
        raise LedgerIntegrityError("Exactly one approved opening must exist before calculating a balance")
    opening = candidates[0]
    if any(
        p.account_id == account_id and p.posted_on < opening.effective_on
        for p in postings
    ):
        raise LedgerIntegrityError("Cash movements before the approved opening need reconciliation")
    return opening.amount_pen + sum(
        (
            p.amount_pen
            for p in postings
            if p.account_id == account_id and opening.effective_on <= p.posted_on <= as_of
        ),
        Decimal("0"),
    )


@dataclass(frozen=True)
class BankReconciliation:
    account_id: str
    as_of: date
    book_pen: Decimal
    statement_pen: Decimal
    difference_pen: Decimal

    @property
    def reconciled(self) -> bool:
        return self.difference_pen == 0


def reconcile(
    account_id: str,
    as_of: date,
    statement_balance_pen: Decimal,
    openings: Sequence[ApprovedOpening],
    postings: Sequence[CashPosting],
) -> BankReconciliation:
    statement_balance_pen = _money(statement_balance_pen)
    ledger = book_balance(account_id, as_of, openings, postings)
    return BankReconciliation(
        account_id=account_id,
        as_of=as_of,
        book_pen=ledger,
        statement_pen=statement_balance_pen,
        difference_pen=statement_balance_pen - ledger,
    )
