# ADR 0010 — F5 Cash grant review: pinned shadow guard and fail-closed transition

Status: Candidate, **metadata registered in DEV, not accepted for financial writes**. 2026-10-27.
Scope: only `fmg_cashgrantrequest` and `fmg_cashaccessgrant`.

## Context and decision

The live F5Q009/F5A009 PreValidation steps deny all direct Create, Update and Delete.
`F5_ServerWriteScope` used plugin `SharedVariables`; the connected isolated
CashTrust experiment did not propagate those markers to nested PreValidation.
Never substitute client tag, arbitrary parent message, depth, administrator
privilege or SYSTEM user for authorization.

Add `F5_ValidatedNestedReviewGuard` as a candidate separate from the ledger
guard. Its constructor needs **two distinct pinned sdkmessageprocessingstep
MainOperation IDs** read back from the registered Custom APIs. It requires
synchronous transactional parent/child lineage, exact authenticated actor,
operation-specific parent pin, current Dataverse privileges and immutable
proposal fingerprint. For a decision, it re-reads the pristine pending request,
checks separation of proposer, beneficiary and reviewer, validates the
conditional request update and checks the grant row against the approved
request. It denies Delete and unexpected attributes and never itself writes.

`CashReviewExecution` can be installed **inertly** without registering
Custom APIs/CRUD processing steps. Installation is NOT enablement; direct guards
must stay active and no role is granted by this packaging step.

## Connected DEV checkpoint

The independent `fmg_CashReviewExecution` package was installed and updated
after correcting exact privilege capitalization. Dataverse stores privileges
using table schema names (`prvCreatefmg_CashGrantRequest`,
`prvWritefmg_CashGrantRequest`); using lower-case logical names caused
Custom API creation fault 0x8004023B. Never bypass this by omitting
`ExecutePrivilegeName`.

The typed private `fmg_SubmitCashGrantRequest` and
`fmg_DecideCashGrantRequest` APIs were both registered with
`F5_API_REGISTRATION_EXIT=0`; subsequent guard inspection returned
`F5_REVIEW_24_DIRECT_MUTATION_GUARDS_UNCHANGED`.
`cash-review-step-inspect` confirmed exactly one active, separate
MainOperation step for each API (exit 0). Only metadata and the isolated
package were deployed; no new financial/grant CRUD step or role assignment
was installed. This is not positive grant or financial rollback UAT.

## Controlled cutover proposal (not yet executed)

1. Independently confirm both Custom API MainOperation steps; pin both IDs
   separately. Check execution privileges and effective roles by *actor*.
2. Install six additional active stage-10 synchronous shadow guard steps
   (3 verbs on each table) with **all original deny steps still active**.
   During overlap, every request must still be denied by originals.
   Verify both handlers, pins, package, solution, filters and ordering.
3. Test ordinary direct Create/Update/Delete denial and submitted/decided
   requests, including wrong role, self-approval, corrupted fingerprint,
   unknown account, expired request and wrong ETag. No real account
   entitlements may be invented for acceptance.
4. Disable old F5Q009/F5A009 only after the new guards are independently
   proven, one at a time with readback. If an error occurs, reenable originals
   before altering any new step. At no point should both protections be
   inactive. Reject operations conservatively while transitions are incomplete.
5. Accept real approval + grant atomicity only after a legitimate distinct
   approver acts and readback proves request/grant consistency. Verify
   revoke, denial, concurrency and replay. Manual second-account login UAT
   remains deferred; automatic separation remains enforced.

## Invariants and residual risk

- `ParentContext` availability and `OwningExtension` pin must be checked in
  the connected environment, not inferred solely from mocks.
- Plugin-package presence and two green CI workflows are not authorization
  to post cash, nor evidence of transaction rollback on cash tables.
- Candidate guard cannot relax `F5G001`, `BGV009` or any finance operation.
- Ledger cutover, cash receipts/transfers, second-leg rollback, balances,
  reconciliation, Power Apps and React operational integration remain separate.
- Keep all logs, organization IDs, identities and package binaries outside Git.

## 2026-10-27 — next checkpoint: read-only standby preflight

Implemented `CashGrantReviewStandbyDeployment` with explicit
`cash-review-standby-inspect` and `cash-review-standby-register --apply`
commands. Every step has the exact table/message filter, package-bound
plug-in type, separate pinned MainOperation identities, caller-only
execution, PreValidation/synchronous stage 10, **rank 2** and a checked
per-step configuration. Every legacy deny step must be enabled at **rank
1** before a candidate can be created; all 24 legacy guards are checked
again after each successful run.

A **connected read-only inspection** in `financial-manager-dev`
returned Exit 0: all six standby steps are absent; the 24 original
direct-write protections remain enabled. An attempt to perform the
actual standby registration was **blocked by the execution tool's
security controls**, before any mutation. Do not work around the
block or report the six candidate steps as deployed. Local CLI
build passed with zero warnings, and the Python guard-contract suite
passed. Safe source is retained for later authorization and inspection.

The rank-2 approach is intentionally **not** proof the new candidate
ran: the earlier rank-1 fail-closed guard prevents ordinary requests
from reaching the later step. A real cutover requires independent
connected verification and account-specific human approvals; a
successful metadata registration alone would not be sufficient.
