# F5 CashReviewExecution — registered, guarded development candidate

Separate .NET Framework 4.8 plug-in package with `F5_GrantReviewEndpoint`,
`DataverseGrantReviewWorkflow`, `DataverseGrantReviewPrivileges`,
`DataverseCashGrantRequestReader` and
`F5_ValidatedNestedReviewGuard`.

## Connected DEV checkpoint — October 27, 2026

`fmg_CashReviewExecution` was installed in `financial-manager-dev`
and updated through `pac plugin push` after correcting Dataverse's
case-sensitive privilege names. The two private, typed Custom APIs
`fmg_SubmitCashGrantRequest` and `fmg_DecideCashGrantRequest` have
been **registered as metadata** with their effective execution privileges
and validated parameters. Connected registration returned exit 0 with
`F5_REVIEW_24_DIRECT_MUTATION_GUARDS_UNCHANGED`.

The required exact privilege names are
`prvCreatefmg_CashGrantRequest`,
`prvWritefmg_CashGrantRequest`,
`prvReadfmg_CashGrantRequest`,
`prvCreatefmg_CashAccessGrant` and
`prvWritefmg_CashAccessGrant`.
**Capitalization is significant in ExecutePrivilegeName.**
Do not replace these names with lower-case table logical names.

**API registration is not authorization to issue grants.**
The live 18 cash, 3 grant and 3 request deny-all steps still block every
direct financial table mutation; no candidate nested review step has been
substituted for a live denial. Installing an API does not authorize a
user to submit or approve through it.

## Candidate security design

The proposal requires an authenticated interactive Dataverse human with
the effective proposal privilege; decision requires effective **Global**
review privilege. The proposer, beneficiary and reviewer must be distinct
for approval. The workflow validates active cash account/branch,
beneficiary identity, immutable fingerprint, expiration, recorded
decision and optimistic concurrency `RowVersion`.

`F5_ValidatedNestedReviewGuard` independently checks two exact,
distinct, active MainOperation pins, the synchronized transactional
child-parent context, actor identity, server privilege, target table,
field whitelist, and the original proposal. It fails closed against
client tags, arbitrary inherited context, direct CRUD, delete, replay
and unauthorized account grants.

The next step is read-only MainOperation identity confirmation, then
possible **overlapping shadow guard registration** retaining the
original 24 denies. Switching off the three request and three
grant denial steps is prohibited until independent connected
permission, transaction, rollback, and concurrent-write acceptance.
If the candidate fails, keep F5Q009/F5A009 enabled.

Manual login testing with a second account is deferred by user decision.
Automatic maker/approver segregation is **never** waived. Actual cash
transfer, two-leg rollback, receipt, balances and reconciliation are
separate connected acceptance criteria, not covered by API registration.

The older `F5_ServerWriteScope` tests remain evidence of a synthetic
source-level ticket experiment; a SharedVariables marker must not be
trusted as the only nested authorization source.
