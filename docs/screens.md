# Screen specification

The application is an operational interface; Power BI is the analytical workspace. This is a **functional screen specification**, not implemented UI source.

## Navigation

```text
Power Apps
├── Home
├── Transactions
│   ├── Transaction details
│   └── New / Edit draft
├── Budgets
│   ├── Annual plan with monthly allocations
│   └── Approval and revision history
├── Master data
│   ├── Categories
│   ├── Branches
│   └── Cash accounts
└── Activity
    └── Recent authorized business events

Power BI
├── Executive Overview
├── Expense Analysis
├── Budget Control
└── Income and Trends
```

Navigation and controls are shown according to role, but access is enforced by Dataverse security. User-facing labels may be in Spanish; source names follow the project's English naming conventions.

## Home

Purpose: immediate operational overview, not a duplicate of the full Power BI report.

- Current period and accessible branch scope.
- Receipt total, payment total, net cash movement, and budget remaining.
- Unposted draft queue and recent assigned actions for authorized staff.
- Shortcuts: New receipt, New payment, Transactions, Budget.
- Last data refresh timestamp if embedding or linking to imported Power BI.

## Transactions

### List

Search by reference or description; filter by period, status, branch, category, and flow class. Display direction, amount, date, status, category, branch/scope, and last modifier. Default to current period rather than loading the entire history.

### Form

Required inputs: date, receipt/payment, flow class, amount, category, internal cash account, and location scope. Optional: description, document reference. Select group then leaf category. On save, show validation errors without losing entries.

Actions are role-specific: Save draft, Post, Void with reason, View audit context. Posted transactions are read-only. A correction creates a new entry linked operationally to the voided record if that relationship is introduced.

## Budgets

- Annual budget header per branch or Shared scope.
- Monthly grid with category/group hierarchy, allocations, annual subtotal, and budget version.
- Actions: Save draft, Submit, Approve/Reject (authorized approver only).
- Approved version is immutable. Revision starts a new version and never edits approved historical values.
- An approved budget comparison view displays actual execution and overspend cases.

## Master data

Administrators maintain codes, descriptions, hierarchy, active status, and allowed cash-flow classification. Prevent duplicates and posting to non-leaf/inactive categories. Keep cash-account labels free of bank account credentials.

## Activity

Display only permitted business events such as posting, voiding, budget submission, approval, and category deactivation. Filter by actor, time, branch, and record. Full audit history remains in Dataverse audit storage with restricted access and retention controls.

## Interaction standards

- Keyboard access, visible validation, clear empty/error/loading states, and contrast-sensitive formatting.
- Do not use red/green as the only means to express favorable/unfavorable status; include symbols or text.
- Confirm consequential operations and display the resulting state.
- Test desktop/tablet layouts and the mobile expense-entry flow.
- Avoid oversized dashboards within Power Apps; link to Power BI for deeper analysis.

## Design references

The provided reference images motivate drill-down matrices, time comparisons, budget variances, account groupings, and period filters. Third-party logos, visuals, branding, and confidential sample figures are not copied into this application.

See [Reporting](reporting.md), [Scope](scope.md), and [Security](security.md).
