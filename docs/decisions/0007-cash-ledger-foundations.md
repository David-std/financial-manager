# ADR 0007 — Cash receipts, internal transfers and bank reconciliation

Date: 2026-10-25
Status: **Specified with a Python reference model; Dataverse tables and operational flows NOT deployed**

## Motivation

Financial Manager currently records confirmed cash **outflows**. A receipt is not automatically earned revenue, and an internal transfer is not an expense. Account balances are not obtainable from summing payments alone. Introducing these as interactive production features without opening balances, rights, immutable histories and bank evidence would be misleading.

## Required immutable financial model

| Concept | Proposed Dataverse record | Scope / invariants |
| --- | --- | --- |
| Settled receipt | `fmg_cashreceipt` | Separate from a sale/invoice; cash account, branch or shared, PEN amount, value date, external unique request ID, actor, supporting reference, Draft → Settled → Reversed (separate reversal posting) |
| Cash transfer | `fmg_cashtransfer` | Origin and destination distinct and authorized; one operation ID and two opposite-signed account legs committed **atomically**; transfer never increases consolidated receipts/expenses |
| Cash posting | `fmg_cashposting` | Immutable account leg, operation reference, signed PEN amount, posting date, actor, unique posting ID; server generated only, never editable directly by the user |
| Opening balance | `fmg_accountopening` | Exactly one approved opening per account in the first delivery; effective date, PEN amount, approver with segregation, controlled adjustment/version procedure |
| Bank statement | `fmg_bankstatement` | Explicit external source, statement date, imported items and author; never inferred from a dashboard |
| Reconciliation | `fmg_bankreconciliation` | Book balance as of a cutoff, statement closing balance and unresolved difference; adjustments only through authorized posting, never silent overwrites |

The names are **design proposals**, not proof that these tables are present in the solution. Schema naming, owner teams, auditing, idempotency indexes, retention and plugin registration must be confirmed before implementation.

## Server responsibilities

1. Create all cash legs in one Dataverse transaction, protecting the invariant that transfer debit and credit sum to zero and refer to two distinct authorized accounts.
2. Enforce unique operation/request identifiers and a complete, immutable reversal rather than deleting or editing a posted cash leg.
3. Reconcile balances only from an approved opening and authorized, committed postings effective at the selected cutoff. Reject missing or multiple openings and incomplete transfers.
4. Preserve per-record actor, effective date, posting timestamp, before/after audit, and branch/team access. A service-account operation must carry and validate the initiating identity, not merely stamp its own ID.
5. A receipt may have commercial context but cannot be labeled earned income without a separately implemented revenue recognition model.
6. A bank statement discrepancy must remain visible; a matching book total alone does not establish reconciliation without independent source evidence.
7. Do not activate user-facing screens or analytical balances from these proposed domain types until Dataverse schema, atomicity, role restrictions and reconciled fixtures are accepted.

## Reference executable tests

`financial_domain/cash_ledger.py` and `tests/test_cash_ledger_contract.py` implement an **in-memory reference specification**, not the backend. Its tests cover signs, two-legged transfer conservation, idempotency, full reversal, cutoff dates, unique approved opening, immutable Decimal PEN and bank-statement differences.

The reference deliberately has no HTTP endpoint, Dataverse authorization, workflow publication or actual balance data. These tests run through the Python CI suite and cannot be cited as a successful live transfer.

## Next acceptance gates

- Confirm requirement ownership and actual chart of accounts, branch/team policies, currencies and permissions.
- Deploy versioned schema/relationships and server-side plugins in the development solution only after validation.
- Test successful and failed transfer/post/reversal with synthetic authorized records and guaranteed rollback, then independent principals.
- Reconcile a fixed test fixture against an external synthetic bank statement across period cutoffs; assert debit/credit conservation and no duplicated consolidated cash totals.
- Implement specialized React/Power Apps forms and Power BI measures only after server contract and permissions are accepted.

See [Security](../security.md), [Verification](../verification.md) and [Draft concurrency ADR](0006-draft-concurrency.md).
