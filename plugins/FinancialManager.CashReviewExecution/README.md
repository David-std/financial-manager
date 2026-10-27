# F5 CashReviewExecution — compiled development candidate

Separate .NET Framework 4.8 plug-in package with `F5_GrantReviewEndpoint`, `DataverseGrantReviewWorkflow`, `DataverseGrantReviewPrivileges` and `DataverseCashGrantRequestReader`.

The proposal command requires the actual authenticated interactive Dataverse human with effective `prvCreatefmg_cashgrantrequest`. The decision requires effective **Global** `prvWritefmg_cashgrantrequest`. The proposer, beneficiary and reviewer must be distinct when approving grants. The workflow checks original account/branch and beneficiary, immutable fingerprints, expiry, signed review decision and optimistic concurrency `RowVersion`.

The package **is built/tested only and is NOT deployed**. The review API registration tool contains a proposed, strictly typed, idempotent metadata contract, but deployment was blocked by environment security controls and is not exposed by the deployment command dispatcher. The 24 existing F5G001/F5A009/F5Q009 direct CRUD protection steps remain the live enforcement. This package cannot grant anyone rights until a separately accepted nested review authorization and independent human approval workflow is proven; it does not relax direct writes.

Manual second-user UAT is deferred by the user and is not required to accept code or CI. This does **not** mean the server can self-approve or assign grants without distinct verified identity.

Do not confuse a registered, protected CashExecution API with authorized cash posting. At the latest connected check there were at least two active accounts and two active branches but zero active grants and zero pending grant requests. Positive cash transfer/rollback acceptance requires a legitimate grant and an accepted, guarded cutover on the actual financial tables.
