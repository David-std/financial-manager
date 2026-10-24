# ADR 0004 — Controlled budget approval and effective-version safety

Date: 2026-10-23
Status: **Approved design direction, not authorized for deployment**.

## Context

Financial Manager currently has a server-enforced, synchronous Dataverse budget guard. It permits Draft creation/editing, Draft → Submitted (with allocations), and administrator rejection of Submitted. It deliberately rejects Submitted → Approved through error `BGV009`. This is **an application policy**, unrelated to Power BI/Fabric or Power Apps trial licensing. The application must never remove the guard just because the owner authorizes expanding development scope.

The development environment currently has one verified application tester/account. There is no accepted evidence of a second authorized identity with a distinct finance-approver role or a concurrency-safe budget-version selection mechanism.

## Approval policy

An approval request must be a **dedicated, authenticated server operation**, not a public `dataApi.updateRow` changing a status field. Before committing, it must verify, from server trusted context:

1. The budget is presently **Submitted**, has required year/scope/version/effective-month/active-branch metadata and one or more valid monthly allocations. Reload the row and allocations inside the operation.
2. The initiating human actor is a member of the specifically granted **Financial Manager Budget Approver** role and has record-specific read/approve privilege. **System Administrator alone is not a substitute for documented approval authority**.
3. The initiator is **not** the creator, submitter, or relevant owner of this budget. Team-owned budgets require explicit segregation rules as well; unknown identities fail closed.
4. Only the server sets `fmg_status = Approved`, `fmg_approvedby` and `fmg_approvedon` using its own verified identity and UTC timestamp. Clients cannot spoof those fields, backdate approvals, or edit approved records.
5. An *atomic uniqueness mechanism* arbitrates the effective budget for each year, scope/branch and month. A query of currently approved versions **is not enough**, because concurrent transactions may both pass the same query.
6. Version-effective date rules must define how a later approved version supersedes earlier months, with immutable history and no gaps or overlapping effective versions. Approving one revised version without resolving the old version is forbidden.
7. The operation runs within a Dataverse transaction or an equivalent locking/alternate-key scheme and either commits the approval, audit attribution and version effect together, **or none at all**.
8. Verify permissions and concurrency using **two distinct identities** and simultaneous synthetic approval requests in a disposable dev environment before enabling the UI button. Validate unauthorized users and both directions of segregation, as well as two conflicting versions.
9. Power BI approved-budget measures must remain unavailable until these transitions and version selection are genuinely enabled and evidenced. Submitted/Draft rows are not approved budget.

## Implementation ordering

1. Create/assign least-privilege finance approval role and verify account memberships with a second nonauthoring user.
2. Add a server-managed approval command and atomic effective-version arbitration. Prefer a dedicated operation over a regular status update.
3. Add rejection and approval reason/audit metadata, and tests for concurrency, race/retry and server-generated Approved By/On. Keep activity events gated until a trusted event writer is implemented.
4. Deploy the new server capability into `financial-manager-dev`, run rollback-only negative and conflict tests, and verify privileges from different users.
5. Only then add a `Review / Approve` UI with explicit confirmation and refresh. Record an independent Power Apps acceptance test. No production approvals occur in this development environment.

## Interim decision

**Keep `BGV009` enabled and do not present a functional Approve button.** The user has approved implementing the full workflow, but this does not establish a separate approver identity or the correctness of concurrent version approval. Continue implementing the missing controls without creating production approvals or using real financial data.

Related: [Data model](../data-model.md), [Security](../security.md), [Reporting](../reporting.md), [ADR 0003](0003-expanded-financial-scope.md).

## Implemented but not deployed: annual approval transaction primitive

The unregistered `BudgetApprovalOperation` now contains a concrete service-side write sequence. It validates `IsInTransaction`, loads the submitted budget and its RowVersion, checks the explicit approver role membership and separation from the creator/owner, requires allocations, and acquires a unique **annual scope claim** before issuing an `UpdateRequest` with `IfRowVersionMatches`. Exceptions are never swallowed, so a properly registered in-transaction operation rolls back both writes. The claim key uses `year:scope:branchGuid` or `year:scope:shared`.

This release deliberately supports **at most one approved budget per year and scope**. It cannot approve a revised version or partially overlapping effective periods; supersession requires another design and transaction workflow. This conservative restriction prevents duplicate annual approvals while keeping the initial data model simple.

**Required deployment gates, not yet completed:**

- Create `fmg_budgetapprovalclaim` with a server-owned `fmg_scopekey` field, lookup to the budget, a successfully **Active** unique alternate key on `fmg_scopekey`, and server rules rejecting all direct client Create/Update/Delete actions. Check uniqueness under actual concurrent synthetic requests.
- Create and assign the distinct `Financial Manager Budget Approver` security role to an independently authorized second human identity. The read-only role check on 2026-10-23 returned **zero** matching roles.
- Register an appropriately bound dedicated Dataverse Custom API with plug-in main operation, authenticated initiation, `IsInTransaction` evidence, least-privilege execution and correct rejection of direct writes. The existing `BGV009` guard and anti-spoof rule will need a narrowly scoped server-only approval path; do **not** remove them generally or simply permit ordinary status updates.
- Validate that the alternate-key claim and budget status update occur in the same transaction, including rollback on a row-version mismatch, duplicate annual claim, missing privilege and missing allocation. Use two separate users and concurrent requests in a synthetic dev test.
- Confirm `approvedby`, `approvedon`, immutable history and audit with independent server readback. Only then expose the Approve button and alter the reporting-approved budget eligibility.

**Current safety:** the new operation is **not exposed, registered or called**, its claim table/key are not provisioned, and no budget can be approved through it. `BGV009` still rejects approvals in the deployed plugin. The operation and its fake-service unit tests are infrastructure preparation, **not** proof of an atomic Dataverse production transaction. See Microsoft's [Dataverse database transactions](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/scalable-customization-design/database-transactions) and [optimistic concurrency](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/optimistic-concurrency) guidance.
