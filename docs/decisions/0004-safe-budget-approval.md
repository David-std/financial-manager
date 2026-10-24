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
