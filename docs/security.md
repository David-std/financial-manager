# Security baseline

Financial records and budgets are business-sensitive even when the application does not store banking credentials.

## Authentication and record access

- Authenticate with Microsoft Entra ID. Use tenant MFA and Conditional Access policies appropriate to the production licenses.
- Enforce access in Dataverse through security roles and tested record ownership/team membership.
- Application navigation and hidden controls are **not** authorization controls.
- Do not create local username/password tables or store banking credentials.
- Separate non-production environments and synthetic records from production business data.
- Review Power BI report sharing, export, semantic-model access, and any row-level security independently from the Canvas App.
- A corporate/shared record is not automatically readable by every branch user.

## Proposed responsibility matrix

Business roles reflect responsibilities, not arbitrary privilege levels. Real staffing and segregation constraints must be approved before implementation.

| Action | Finance Manager | Treasury Clerk | Branch Operator | Branch Manager | Executive Viewer | Auditor |
| --- | --- | --- | --- | --- | --- | --- |
| Create draft movement | All scopes | Assigned scopes | Assigned branch | Assigned branch | No | No |
| Post verified movement | All scopes | Authorized scopes | No | No | No | No |
| Void posted movement with reason | Yes | No | No | No | No | No |
| Edit category/account reference data | Yes | No | No | No | No | No |
| Draft budget | Yes | No | No | Assigned branch | No | No |
| Approve budget | Yes, subject to separation of duties | No | No | No | No | No |
| View financial reports | All scopes | Authorized scopes | Limited | Assigned branch | All approved scopes | All approved scopes |
| View full audit history | Restricted | No | No | No | No | Yes |

Technical Power Platform administration is **not** a routine finance-user role. Use documented separate administrative access. The detailed approval implementation and concurrency gates are in [ADR 0004](decisions/0004-safe-budget-approval.md) and [Draft concurrency ADR](decisions/0006-draft-concurrency.md); preparer/approver separation and an atomic effective-version operation must be deployed before unblocking `BGV009`. If the same person prepares and approves a budget in a small organization, record an explicit exception and compensating review instead of silently treating it as segregation of duties.

## Branch enforcement

Proposed: branch-specific Dataverse owner teams and one authorized shared/corporate scope. Users can belong to multiple teams when approved. Test direct table reads, updates, flow connections, Power BI access, and cross-branch filtering against that boundary.

Dataverse business units are an alternative if confirmed requirements justify organizational security boundaries. Do not create an elaborate business-unit tree solely because two branches exist.

## Change and audit history

- Enable Dataverse auditing at environment, table, and relevant column levels for transactions, budget approval/version changes, categories, and account labels.
- Preserve built-in Created By, Modified By, and timestamps.
- Do not allow ordinary users to delete posted movements, alter approved budgets, or modify audit settings.
- A curated `fmg_activityevent` feed supports the latest-business-events screen; it is not a complete security audit.
- Only trusted, server-validated operations can create activity events. Protect against client-submitted audit-event forgery.
- Define audit log retention and storage requirements for production; Git history and Canvas App telemetry do not replace financial record auditing.

## Validations and workflow

Cross-table category checks, immutable posted records, branch ownership, budget approvals, and expense overrides cannot rely only on Power Fx validation. Implement server-side enforcement using supported Dataverse capabilities and test against direct data updates and Power Automate connections.

All privileged posting/approval actions require authenticated attribution. When a flow uses a service account, store the initiator and verify the action against the initiator's permissions.

## Sensitive data and public repository

The repository must not include organization identity, employee compensation details, customer lists, production tenant IDs, connection tokens, bank account numbers, actual financial figures, or exported reports from production. Screenshots and sample records must use synthetic data.

Apply connector data policies and classify integration endpoints. An iOS direct-write flow requires OAuth, idempotency, replay resistance, and least-privilege connections. WhatsApp numbers alone are not identities for financial disclosure.

## Production acceptance

Validate authorization from both the application and direct Dataverse access; test cross-branch isolation, audit history, approval separation, Power BI sharing/export, backup and restore, environment migration, and incident recovery.

See [Verification](verification.md) and [Architecture](architecture.md).
