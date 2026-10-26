# ADR 0008 — Secure cash-operation API boundary

Date: 2026-10-26
Status: **Proposed; server operation code, permissions, deployment and UAT NOT accepted**

Supersedes any recommendation to call `ExecuteTransactionRequest` **inside** a synchronous Dataverse plug-in. This ADR refines [ADR 0007](0007-cash-ledger-foundations.md), not the verified F5 deployment.

## Verified baseline and boundary

The development environment now contains the six F5 cash tables, nine restrictive relationships, six Active alternate keys and one `fmg_CashIntegrity` package with 18 active, synchronous PreValidation guards. The connected test rejected direct Create on all six tables with `F5G001` and subsequent reads showed no rows. This is a temporary **deny-all** security boundary, not a cash posting service. No other plug-in, role or user interface receives a bypass.

Keep `BGV009`, the unassigned budget approver role, payment authorization and the F3/F4 user-acceptance gates unchanged.

## Proposed server commands (not deployed)

| Command | Intended outcome | Financial invariant |
| --- | --- | --- |
| `fmg_SettleCashReceipt` | Settle one verified receipt and issue a cash leg | One positive posting; receipt is **not** recognized earned revenue |
| `fmg_PostCashTransfer` | Move cash between two different authorized accounts | Two legs, opposite signs and identical value date; sum = zero |
| `fmg_ReverseCashOperation` | Reverse an entire original operation for a mandatory reason | Append full opposite legs referencing originals; never overwrite or delete |
| `fmg_ApproveAccountOpening` | Approve a defensible opening balance with segregation | Exactly one valid approved opening per account and effective period |
| `fmg_ReconcileBankStatement` | Compare immutable book total at cutoff with independent bank evidence | Preserve unresolved differences; no adjustment through reconciliation itself |

Names and parameter types must be frozen with a separate schema/API review before deployment. Do not register an API just because a proposed command appears in this table. All monetary input is explicit PEN Decimal at two fractional digits, finite, within the deployed Money column range. Currency conversion must be a separate reviewed service. Effective date and posting timestamp are distinct.

## Trust and authorization model

1. Use Dataverse Entra-authenticated execution identity. `CustomAPI.IsPrivate` affects discoverability and **does not grant authorization**. Require `CustomAPI.ExecutePrivilegeName` with a reviewed existing privilege; additionally check actor, required business role, allowed scope, account ownership/team membership, active account and source/destination branch restrictions inside the server handler. No automatic System Administrator, service principal or shared-account override.
2. Derive initiator from the verified Dataverse execution context. Reject user-supplied actor IDs, target owner, impersonation hints, bypass booleans and client-computed ledger legs. If a delegated service identity is required, adopt a separate cryptographically grounded integration protocol and record the verified human actor.
3. Do not permit a user to manipulate `fmg_cashposting`, opening approvals, bank evidence or audit records through direct table CRUD, Power Automate or React. Guard enforcement is server-side and persists when the UI is hidden.
4. Do not allow branch/shared switching to silently expand account access. Always re-evaluate authorization against current server data, including idempotent retry calls.
5. Test direct SDK/Web API, delegated Power Apps, cross-branch user, finance officer, viewer, auditor and administrator independently. A synthetic role selector and one administrator cannot establish segregation of duties.

## Idempotency, concurrency and ledger integrity

- A unique external request key identifies a proposed operation, but uniqueness alone does not make retries safe. Store or reconstruct a canonical immutable request fingerprint (kind, amount, currency, account IDs, scope, effective date and linked source). An identical authorized retry returns the original operation identity; a mismatching key replay fails with a conflict.
- Resolve concurrent same-key races using Dataverse's **Active** alternate key, detect duplicate-key faults, retrieve the committed original, reauthorize the caller and compare fingerprints; never issue another posting on a race.
- Validate exactly one ledger posting for a settled receipt, exactly two balanced legs for an internal transfer, and complete reversal of the original operation. Reversals get a new operation ID/date and preserve original postings and audit attribution.
- Never calculate a book balance without exactly one approved opening at or before the cutoff; reject missing opening, duplicate opening, unsupported currency, postings before opening and incomplete original/reversal pairs.
- Bank statements must come from independent source evidence. A zero difference does not imply that imported statement records are trustworthy or that outstanding items are resolved.
- Lock or constrain concurrent status changes using row-version/optimistic concurrency and unique keys. A pre-read alone is not an atomic condition.

## Transactional execution decision

Dataverse explicitly warns against `ExecuteMultipleRequest`, `ExecuteTransactionRequest`, `CreateMultipleRequest`, `UpdateMultipleRequest` and `UpsertMultipleRequest` **inside synchronous plug-ins**. Those messages remain appropriate for a separate external test client performing an intentional rollback sentinel; they are not the implementation pattern for a plug-in.

The proposed Custom API main-operation plug-in must assert `IPluginExecutionContext.IsInTransaction` in the registered runtime stage and use ordinary sequential `IOrganizationService.Create` operations within the same platform transaction. If it cannot demonstrate a common transaction, fail closed and revisit the architecture rather than writing partially committed legs. Throwing a synchronous plug-in failure must atomically roll back the receipt/transfer row and every leg. Confirm these properties with connected tests, not unit tests alone.

## Safe evolution from the temporary guard

1. Implement and test the new server handler, validation and provisioning tools in source **without registering an operational API or relaxing the existing guard**.
2. Verify privileges, actor identities, transactional stage and the six account/branch metadata contracts. Add a deny-by-default authorization contract to the guard, with exhaustive negative tests for spoofed `ParentContext`, fake request parameters, wrong stage, depth, message, account, actor and missing identity.
3. A future revision may allow nested ledger writes **only** when the platform-created, verified server operation context comes from an approved Custom API and the caller passes the corresponding checks. A client-supplied flag, correlation ID, request name or arbitrary service account is insufficient proof. If the server cannot reliably distinguish trusted nested calls, retain deny-all and choose a different architecture.
4. Deploy and verify the revised guard without deleting the 18 current steps or exposing a gap. Confirm direct CRUD is still rejected before trying an authorized business command. Do not use an administrator override to force a test through.
5. In the isolated authorized test environment, prove positive operations, duplicate-key replay and payload conflict, insufficient permissions, transfer conservation, failed second-leg rollback, complete reversal and simultaneous retries. Delete synthetic test records only through a reviewed cleanup procedure; never alter real records.
6. Verify zero unexpected rows, publisher/solution components, trace/audit attribution, and all readbacks. Enable Power Apps/React/Power BI cash capabilities **only after** server gates and independent-user UAT are accepted.

## Unresolved design requirements

- Freeze account status and branch/shared ownership policy using live schema reads, not guesses from a frontend fixture.
- Decide approved opening flow, statement import provenance/immutability, reversal uniqueness keys, audit actor snapshot, unique operation fingerprint columns, document references and retention.
- Confirm minimum execution privileges and their team assignment with another licensed identity, not the current administrator.
- Demonstrate the Custom API stage transaction behavior and the precise server-only guard authorization mechanism. Both are hard release blockers.

## Authoritative platform guidance

- [Custom API: privacy and ExecutePrivilegeName](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api)
- [Database transactions and plug-in context](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/scalable-customization-design/database-transactions)
- [Avoid batch requests inside synchronous plug-ins](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/best-practices/business-logic/avoid-batch-requests-plugin)
- [Synchronous plug-in errors and rollback](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/handle-exceptions)

**Acceptance state:** specification only. No operational Custom API, data write permission, approval or cash screen was deployed by this decision.
