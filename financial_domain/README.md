# Financial domain — reference contracts

This folder contains pure, deterministic specifications for the next financial domains. It does **not** write to Dataverse or authorize real cash movements.

The initial `cash_ledger.py` model uses `Decimal` PEN values and immutable signed cash postings to specify requirements for settled receipts, posted payments, two-legged internal transfers, reversals, approved opening balances and statement reconciliation.

Run `python -m unittest tests.test_cash_ledger_contract -v` from the repository root. CI also discovers these tests automatically.

Production implementation must first pass the safety gates described in [ADR 0007](../docs/decisions/0007-cash-ledger-foundations.md): Dataverse table metadata, unique idempotency keys, atomic server operations, least-privilege roles, independent audit and accepted bank statements. No interface or report should imply that this reference module provides a live account balance.
