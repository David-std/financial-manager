# ADR 0011: Cash guard overlap, isolated rollback proof and F6 entry criteria

**Status:** Accepted for DEV laboratory validation only — not authorization to operate cash.
**Date:** 2026-10-28

## Decision

Keep all 24 original direct-mutation denials active (18 financial rows, 3 grants,
3 grant requests). Add independent **rank-2** validators for the protected
financial workflow without disabling, reordering, impersonating, bypassing or
deleting any original rank-1 denial.

The financial standby registrar verifies that the installed
`fmg_CashExecution` NuGet package owns both the transaction endpoint and
`F5_ValidatedNestedCashGuard` plug-in types, the **two distinct** pinned
MainOperation steps correspond to the actual registered private
`fmg_SettleCashReceipt` and `fmg_PostCashTransfer` Custom APIs, and
`Create`, `Update`, and `Delete` filters match exactly three allowed
financial tables. Each nine-step overlap registration is checked after
creation and requires the original guard on the same message, table and
rank to be active. The additional guard is fail-closed and does **not** trust
client-supplied tickets or `ParentContext` alone.

The previous six review standby validators on grants and grant requests remain
untouched. None of these new guards constitutes authority to issue a grant
or post a financial movement; rank-1 denials remain controlling.

## Accepted evidence

- The isolated `fmg_CashNestedTrustProbe` in DEV executed **two protected
  nested creates** and intentionally failed, then re-read no surviving
  synthetic rows: `F5_TRUST_TWO_WRITES_ROLLED_BACK_REAL_DATAVERSE`.
  It does not touch real cash accounts, grants, transfers or balances.
- Direct Create of that diagnostic table was rejected with `F5S002`.
- The deployed nine cash validator steps were read back during registration
  with `F5_FINANCIAL_STANDBY_PROTECTED`, and the tool confirmed that all
  24 original deny guards remain active. External reinspection should be
  repeated separately before any cutover.
- Domain and plug-in tests exercise accepted transfer pairs, single-leg
  receipts, nonce/replay protection, concurrency, reversals and bank
  reconciliation policy. These are **local tests**, not positive DEV
  financial writes.
- Six native Dataverse F5 entity-set names and read columns are verified by
  `cash-live-entitysets-inspect`. The connected React view only sends
  delegated GET requests through the existing identity-checked repository;
  it does not infer balances from incomplete paginated results, nor offer
  live cash mutation buttons.

## Open acceptance gates

1. A real requester and **distinct** authorizer must submit/decide a grant
   through authenticated Custom APIs; verify maker/checker, effective
   privileges, immutable proposal fingerprint, expiration, self-approval
   denial, revocation and audit.
2. Only after a safe guard transition proves the actual nested execution
   origin, actor-scoped grant and **transaction rollback** may the old
   deny-all steps be replaced. There must never be a no-guard interval.
3. Execute a genuine approved receipt and two-leg transfer; check posted
   rows, account scope, PEN consistency, zero transfer sum, idempotent
   retries, concurrent requests, reversing transactions and rollback of
   a failed second leg.
4. Accept account opening, bank statement and reconciliation operations
   with genuine authorization and audit. These DEV tables currently have
   zero operational rows, so the read-only interface does not prove a
   successful bank reconciliation.
5. Repeat a Delete-denial test against an **existing** isolated record.
   A Delete of a nonexistent cash GUID did not prove the exact `F5G001`
   plug-in ran; do not mark that probe as passed.
6. The DEV metadata inspector observed `fmg_cashaccount` itself has
   auditing disabled; the six dedicated F5 financial tables are audited.
   Assess and enable account audit settings for table and relevant columns
   before accepting financial account administration in production.

The user's second-login manual UAT is **deferred and accepted as an open
dependency**, not a PASS. The lack of a real approver cannot be satisfied by
a synthetic approver, system administrator self-approval or disabling a
denial.

## F6 transition

Non-mutating F6 work may proceed: ALM, managed-solution packaging, CI,
reporting, RLS design, audit readiness, accessibility, release documentation
and rollback runbooks. The protected F5 financial workflows remain disabled
in LIVE until these acceptance gates are met. Do not describe F5 as
operationally completed or release cash movement capability on the strength
of laboratory/negative tests alone.
