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


## Connected account and scope metadata (2026-10-26)

A metadata-only SDK read in `financial-manager-dev` established the following contract without reading any account values or money:

- `fmg_cashaccount` and `fmg_branch` are **OrganizationOwned**, currently with `IsAuditEnabled=false` and optimistic concurrency enabled. Neither is a user/team-owned account-scoping object.
- `fmg_cashaccount.fmg_branch` targets `fmg_branch` and uses `Restrict` deletion. Both tables have `fmg_isactive`. The account-type option is independent of explicit authorization scope. Do not assume an account with a null branch is shared.
- The existing expense `fmg_financialtransaction` and all six F5 tables are user-owned and audited. The F5 receipt, transfer, posting, opening, statement and reconciliation lookups and PEN Money fields matched the expected mapping.
- The proposed `fmg_SettleCashReceipt`, `fmg_PostCashTransfer`, `fmg_ReverseCashOperation`, `fmg_ApproveAccountOpening` and `fmg_ReconcileBankStatement` Custom APIs were all absent.

**Security consequence:** no organization-owned cash account may be authorized through Dataverse `ownerid` or a client-submitted branch selector. The server must derive identity and its branch entitlements from verified, independent memberships; confirm both account and branch are active; and require a separately approved shared-account grant when a branch is absent. The source-only `CashAccountScopePolicy` implements these fail-closed predicates and is unit tested; it is **not** a live Dataverse permission adapter.

Auditing of account and branch reference changes is a separate go-live control: do not change these environments or roles merely to make a read-only inspector pass. Review environment/table auditing and verify with non-admin accounts before users may post money. The identity-to-branch entitlement data source and Shared account designation are **unresolved** business decisions and cannot be inferred from the inspected schema.

Command: `cash-contract-inspect` (read-only). The full connected metadata report is stored locally outside Git under `FinancialManager-Work/F5-P1-CONTRACT-METADATA-20261026.log`.


## Reversal planner implemented in source, not deployed

The `FinancialManager.CashDomain` library now contains `CashReversalPolicy`, a non-IPlugin, non-transport planner that emits compensating immutable legs referencing every original posting. For a receipt it requires exactly one original positive posting; for a transfer it requires exactly two distinct, balanced postings on the same date. In all cases it rejects duplicate posting IDs, partial original operations, an already-reversed/draft original, missing actor/account reversal privileges, backdating, invalid money precision and a missing reason. Each accepted request has a canonical fingerprint bound to the original operation and initiating actor.

This **does not** establish a safe server-side reversal. The Dataverse adapter must authenticate and authorize the initiating actor; retrieve the original operation and all its immutable postings; atomically check and update original reversal state using row-version; enforce uniqueness for a full reversal operation under concurrent requests; create all compensating postings in one platform transaction; and prove rollback on a forced mid-operation failure. Existing `fmg_cashposting.fmg_reversespostingid` is a text field, not a database-enforced foreign key or proof of no double reversal. Do not allow the proposed Custom API to run until those controls are present.

All tests are synthetic and read-only with respect to Dataverse; the deployed `fmg_CashIntegrity` 18-step deny-all guard remains active.


## Transaction coordinator + immutable persistence prerequisites (2026-10-26)

The connected development Custom API `fmg_CashTransactionReadiness` returned `ObservedStage=30`, `InTransaction=True`, `SameIdentity=True`, `CashWritesEnabled=False`. Readback then confirmed 18/18 cash guard steps, all 6 Active unique keys and zero records across the 6 F5 cash tables. This demonstrates the runtime stage of **the read-only diagnostic only**; operational cash Custom APIs remain unregistered and unaccepted.

`CashExecutionCoordinator` now defines the **source-only** server orchestration contract: platform-verified synchronous transaction and matching initiator, fresh per-operation authorization (including retries), canonical amount/currency/ledger planning, lookups by Active alternate request key, immutable canonical fingerprint, header + one/two ledger postings + final status within the same transaction, and post-write ledger reconciliation. It **does not invoke Dataverse**, and its fault-injection and rollback simulation **are not connected acceptance**. Production transport must implement `ICashLedgerTransaction` and `IServerCashAuthorization` strictly within the main-operation plug-in, not the UI.

The existing two header tables lack `fmg_requestfingerprint` (SHA-256 uppercase, 64 characters) and `fmg_initiatinguserid` (canonical platform user GUID, 36 characters). A guarded, additive, idempotent metadata-only tool, `cash-operation-contract-inspect`/`cash-operation-contract --apply`, checks and provisions these auditable attributes on CashReceipt and CashTransfer without writing financial records or granting roles. It requires Active header/posting keys and empty development tables. The fields are part of the protected six-table write boundary. A current client-provided account/branch, ID, fingerprint or role never becomes trusted merely because these fields exist.

**Open acceptance conditions remain critical:** the real server adapter and immutable fingerprint persistence; independent server mapping of authenticated user/team to branch or explicit Shared grants; reference-data auditing; per-API execution privileges; controlled guard migration with 18 direct-CRUD denials unchanged; real second-posting rollback; duplicate-key races and row version behavior; non-admin cross-branch UAT; safe reversal uniqueness, opening, bank reconciliation and UI integration. Do not claim this completes F5 or authorizes transfers.


## Explicit account-per-operation grants, not implicit role expansion (2026-10-26)

A code review identified and repaired a privilege-widening risk in the earlier source-only `TrustedCashActor`: independently unioning allowed accounts and allowed actions could accidentally allow action X on an account with only action Y. The production path now enforces the **pair (account, operation)** at both transfer endpoints and all reversal legs. Existing synthetic `TrustedCashActor` overloads are preserved for old tests only; the actual Dataverse adapter always uses scoped grants.

`DataverseCashAuthorization` is now an **SDK-backed, read-only authorization adapter** requiring a live, not-disabled system user and exact enabled `fmg_cashaccessgrant` rows by authenticated `InitiatingUserId`. It loads each requested `fmg_cashaccount`, its active `fmg_branch`, a distinct exact user/account/operation grant, and an explicit `fmg_allowshared` flag for branchless accounts. Choice IDs are fixed: `797282000` SettleReceipt; `797282001` InternalTransfer; `797282002` Reversal. A grant for an unrelated user, a different operation or inactive branch is not sufficient. No team-role inference or automatic administrator access exists. This intentionally restricts the initial scope to **direct human-user grants**; team membership delegation requires a separate reviewed extension.

The new **user-owned, audited and optimistic-concurrency-enabled** `fmg_cashaccessgrant` table is an additive entitlement schema, with lookups `fmg_principal -> systemuser` and `fmg_cashaccount -> fmg_cashaccount`, required business Choice `fmg_operation`, flags `fmg_isactive` and `fmg_allowshared`, and an Active composite alternate key `fmg_CAG_PrincipalAccountOperation` over `(principal, account, operation)`. No business grants are created automatically. NULLs must not become an implicit authorization path.

`FinancialManager.CashAccessIntegrity.F5_BlockCashAccessWrites` is a separate deny-all package for all direct Create/Update/Delete of grants. Its three synchronous PreValidation steps must all be verified, and a deliberate unauthorized Create must return `F5A009`. Future grant approvals must use an authenticated, dual-control, audited server process. That process **does not exist yet**; no attempt should unblock the table by granting admin CRUD or trusting a UI flag.

Before installing this boundary in dev, require schema/package provenance, live 18 F5 guards, zero ledger rows, no existing grant rows, active composite key (may take time to activate), independent readback of 3/3 grant guard steps, and zero changes to cash operations. Do not deploy `DataverseCashAuthorization` into a live operation Custom API yet. Its source-only SDK tests use synthetic stubbed service responses and do not constitute non-admin UAT. Reference controls: <https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api>, <https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity>, <https://learn.microsoft.com/en-us/power-platform/admin/manage-dataverse-auditing>.


## Connected F5 grant boundary and source-only ledger SDK adapter

The F5 access-grant package and empty `fmg_cashaccessgrant` table were installed in the authorized development organization on 2026-10-26. Dataverse independently verified 3/3 synchronous grant CRUD guards with a deliberate direct Create failure `F5A009`; the original 18/18 cash CRUD guards remain active. All six original F5 alternate keys remain Active, all six F5 cash tables are empty, and `fmg_cashaccessgrant` has no rows. The new composite grant key was `Pending` on the last read: **do not treat this as an approved source of active user grants until the key becomes Active**. No role grants, account approvals, movements, real balance or UI changes were made. Evidence: external `FinancialManager-Work/F5-CASH-ACCESS-DEV-DEPLOY-20261026.log`, `F5-GRANT-DIRECT-DENIAL-20261026.log` and `F5-CASH-ACCESS-POSTCHECK-20261026.log`.

`DataverseCashLedgerTransaction` now implements `ICashLedgerTransaction` in **source only**, compiled against the actual Dataverse SDK. It serializes Receipt/Transfer headers, immutable SHA256 fingerprint and user attribution, one or two PEN postings with deterministic unique posting keys, linkage to operation id, retrieves posted ledger pairs and uses `UpdateRequest(ConcurrencyBehavior.IfRowVersionMatches)` to finalize from Draft after re-reading the record. A committed-key retry must have a matching fingerprint, initiating identity, terminal status and all original legs. Synthetic SDK tests exercise field mappings, balancing and missing row versions. **No operational Custom API message references this class**; its code must not be registered before the cash guard has a platform-verifiable nested request policy and real role/branch acceptance.

Remaining hard prerequisites: an actual auditable dual-control mechanism to issue/revoke grants (currently deny-all), obtain at least two independently licensed non-admin principals, verify effective SDK/Web API privileges, confirm the grant key Active, protect/reference-audit account and branch changes, create a transactional `MainOperation` dispatcher and approved API request/response definitions, prove real successful 2-leg posting and failed-second-leg rollback under the F5 guard policy, complete idempotency/concurrent retries and budget gate UAT. Fake transaction tests are not evidence of production atomicity.


## 2026-10-26 continuation — Live transaction evidence and code-only typed endpoint

The existing live diagnostic `fmg_CashTransactionReadiness` was invoked through the verified development organization. Its **read-only** output was `ObservedStage=30`, `InTransaction=True`, `SameIdentity=True`, `CashWritesEnabled=False`. This proves that *that* synchronous Custom API MainOperation runs inside the Dataverse platform transaction under matching identities, not that a cash-settlement/transfer operation has been accepted. The inspected F5 header fingerprint and initiator fields were all present, and `CashLedgerContractReadiness.Inspect` independently verified receipt/transfer/posting fields, PEN monetary precision, choices, protected active keys, lookups, concurrency and the absence of cash rows.

The server endpoint `F5_CashOperationEndpoint` is **SOURCE ONLY and UNREGISTERED**. It accepts only `fmg_SettleCashReceipt` or `fmg_PostCashTransfer`, with SDK-typed `RequestKey`, `SourceAccount`, optional-required `DestinationAccount`, `Amount` (Money), `ValueDate` (DateTime), and `EvidenceReference`. Its currency is fixed to PEN and verified against one and only one `transactioncurrency` row; the caller cannot specify principal, branch, currency id, grant override or `tag`. It requires actual `IPluginExecutionContext` identity parity and transaction evidence BEFORE constructing an SDK store. The service uses the initiating user context, the real grant authorization reader, and the ledger adapter. No newly available API, hidden UI write, admin shortcut or guard exception was installed.

The ledger adapter now checks the current active status and branch of both transfer accounts immediately before creation, and verifies **each** posting's header link, signed amount, account and posting kind before changing the operation status with ETag/row-version concurrency. Negative SDK tests reject a foreign, imbalanced, mixed-kind or same-account second leg; tests are in-process only, and do not validate actual rollback yet.

Deployment gates remain: (a) new `fmg_CAG_PrincipalAccountOperation` must report `Active` (last confirmed `Pending`), (b) reviewed dual-control issuance/revocation and actual least-privilege role rights, (c) the F5 guard replacement must only honor provable nested execution of an approved Custom API, never client input or global admin bypass, (d) test atomic two-leg posting and intentional failure with audited rollback and independent non-admin identities in dev. If any of these fail, keep all 21 existing deny-all guards and operations inaccessible.


**Grant-key job diagnostic:** The 2026-10-26 read-only `cash-grant-inspect` returned `F5_GRANT_KEY_STATUS=Pending` and `F5_GRANT_KEY_JOB_NOT_ASSIGNED` (empty or unavailable `EntityKeyMetadata.AsyncJob`). The inspector now emits the non-sensitive job state/status/error flag only when Dataverse supplies an async job reference. Do not assume indexing succeeded or force a key rebuild while Pending. Cash grants remain empty and deny-all. Microsoft documents that alternate key indexes are built asynchronously and are not enforced until `Active`: <https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity>.


## F5 P2 — time-limited two-person grant approval, without issuance (2026-10-26)

The `CashGrantApprovalPolicy` domain now models a server-created `Grant` or `Revoke` request, immutable SHA-256 fingerprint, independent proposer, beneficiary and approver, valid UTC timeline, justification, exact account/operation/shared intent, and a maximum 90-day lifetime. The approver cannot be the proposer or beneficiary; the proposer cannot benefit personally. This model is **not** proof of any person's role and does not insert, update or delete grant rows. The future server adapter must independently verify proposal/approval privileges and persist/compare an immutable pending row with ETag; it must not accept caller-provided roles or a self-asserted approval state.

To prevent lifetime drift, `DataverseCashAuthorization` now fails closed if a grant lacks `fmg_expiresatutc`, has an invalid expiry or is already expired, even if the legacy `fmg_isactive` flag is true. The new `cash-grant-expiry-inspect` / `cash-grant-expiry --apply` mode creates and publishes **one audited TimeZoneIndependent DateAndTime column** with an explicitly UTC business contract, only when the grant table is empty, the composite key is Active and all 21 existing grant/cash CRUD guards are active. It does not issue any grants or financial postings. Review and validate on the developer organization only.

The rights lifecycle remains incomplete until two protected Custom API handlers are implemented for proposal and independent approval/revocation, including distinct effective Dataverse role checks, immutable pending request storage with active unique keys, row-version/locking, server-only nested grant application under a narrowly verified guard, and multiple independent non-admin acceptance identities. `fmg_cashaccessgrant` direct CRUD must keep returning `F5A009`. Avoid interpreting `ApprovedCashGrantChange` as a persisted authorization: it is only a domain proof object.


## Source-only, protected grant-request persistence schema (F5 P3)

New `fmg_cashgrantrequest` is the **pending review record**, separate from effective `fmg_cashaccessgrant`. It must be UserOwned, audited, optimistic-concurrent, with immutable request id, SHA256 fingerprint, proposer/beneficiary/account lookups, kind, change, Shared intent, justification, UTC expiry, review state, optional independent approver/decision timestamp and an Active alternate key over `fmg_externalrequestid`. Proposed business lifecycle is Pending -> Approved, Rejected or Expired; it must never allow direct client transitions or edits to original proposal fields. The review table itself creates NO rights: approved requests only become effective after an independently reviewed server transaction has applied a time-limited grant.

Because direct mutation of review fields is financially sensitive, a **separate deny-all** plugin package `fmg_CashGrantRequestIntegrity` now guards synchronous PreValidation Create/Update/Delete of the review table with `F5Q009`. It does not edit or relax any of the existing 21 ledger/grant guards. No proposal/decision Custom API is registered; the request table is intended to remain empty during initial deployment. A narrowly authorized, server-only nested context and effective two-person privileges must be proven before the guard changes.

`DataverseCashGrantRequestReader` is a **read-only SDK adapter**: it requires a Pending row, matching GUID external key, nonempty ETag, no approver/decision fields, correctly typed/linked user and account, exact option values, valid UTC lifetime and the immutable fingerprint computed by the existing cash grant domain planner. Tampered beneficiary/account/action/reason or copied fingerprint fails closed. It cannot insert/approve a row. Its tests use a synthetic SDK provider and do NOT demonstrate end-user rights or Dataverse rollback.

Production migrations must avoid any unguarded window (bootstrap requires a protected transactional solution deployment plan, not merely sequential developer metadata operations). In DEV, provision the validated standalone deny-all package first, then empty metadata, immediately register 3 PreValidation steps and prove a direct Create is blocked. Demand `fmg_CGR_ExternalRequest` Active, 3/3 new guard steps, 3/3 cash grant guards, 18/18 financial guards, zero grants and zero cash rows; do not assign approvers automatically. <https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity>
