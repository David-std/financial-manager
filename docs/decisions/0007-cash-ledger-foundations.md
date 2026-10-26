# ADR 0007 — Cash receipts, internal transfers and bank reconciliation

Date: 2026-10-25
Status: **Development metadata and six idempotency keys deployed; operational cash flows NOT enabled**

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
- Keep the deployed development schema under ALM and register synchronous server plug-ins, private transaction APIs and least-privilege roles only after independent validation.
- Test successful and failed transfer/post/reversal with synthetic authorized records and guaranteed rollback, then independent principals.
- Reconcile a fixed test fixture against an external synthetic bank statement across period cutoffs; assert debit/credit conservation and no duplicated consolidated cash totals.
- Implement specialized React/Power Apps forms and Power BI measures only after server contract and permissions are accepted.

See [Security](../security.md), [Verification](../verification.md) and [Draft concurrency ADR](0006-draft-concurrency.md).


## Proposed live Dataverse mapping and rollout

CashSchemaDeployment defines six user-owned, audited F5 entity types: fmg_cashreceipt, fmg_cashtransfer, fmg_cashposting, fmg_accountopening, fmg_bankstatement and fmg_bankreconciliation. Cash amounts use 2-decimal Money types and nine restrict-delete lookups to the existing accounts, branches and statement. CashUniqueKeys plans idempotency indexes and one approved opening per account/date. CashTransactionProbe uses ExecuteTransaction with a guaranteed-invalid final sentinel so both transfer legs are rolled back together.

These tools are not a dedicated financial operation API. A separate synchronous server plug-in must block direct cashposting mutations, implement authorized state transitions, and post/reverse both cash legs atomically. New roles and audit attribution require acceptance before any live posting. Real bank balances must never derive from metadata creation alone.

A Dataverse 0x80071151 customization lock during an unrelated solution Import prevents schema installation. It is a deployment blocker, not evidence of successful creation. Rerun inspection when the import completes normally.

## Development schema deployment and remaining safety boundary

The six F5 Dataverse tables were created and published inside the verified `FinancialManager` development solution. A connected metadata read verified user ownership, native auditing, optimistic concurrency and expected columns, with **zero rows** in each new table. All nine account/branch/statement lookups were provisioned. Six alternate keys, including the account/date opening key, were read back with **Active** index status. The key provisioner must supply `EntityKeyMetadata.DisplayName`; Dataverse rejects keys missing that label.

The rollback-only `CashTransactionProbe` was executed against the same development organization. It created a synthetic draft transfer and two balanced signed cash legs inside `ExecuteTransaction`; the mandatory failing sentinel produced the expected fault and subsequent queries confirmed **zero persisted transfer or posting rows**. This is evidence of atomic rollback, **not** evidence of a permitted business transfer or an approved cashposting Custom API.

F5 operational security gates remain: synchronous plug-in guards for direct Create/Update/Delete and immutable cash postings; service-side validated posting/reversal APIs with idempotency; scope/team privileges and audit attribution; accepted opening balances, bank reconciliation, independent identities and end-to-end UAT. Do not grant users new F5 roles, expose operational screens or calculate real cash balances from schema alone.
