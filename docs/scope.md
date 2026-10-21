# Project scope

## Purpose

Deliver a lightweight internal financial control and budgeting application for a small Peruvian internet service provider operating in two locations. The system supplements existing customer and billing software; it does not replace it.

This is a **proposed design baseline**, not a confirmed description of the current business process. The reference dashboards illustrate analytical capabilities, not a mandate to replicate their design or accounting terminology. The public repository must not identify the customer or contain production financial records.

## Business outcomes

1. Record, review, and trace cash receipts and payments.
2. Understand the financial performance of each operating location and the business as a whole.
3. Plan spending by month, category, and location, with an annual consolidated view.
4. Compare actuals against approved budgets and prior periods.
5. Separate operational spending from equipment investment and other non-operating cash outflows.
6. Make authorized changes and decisions traceable across multiple users.

## Functional scope

| ID | Capability | Initial release |
| --- | --- | --- |
| FM-01 | Authenticated access and role-based permissions | Required |
| FM-02 | Financial movement entry, posting, voiding, and searchable history | Required |
| FM-03 | Expense and income category management | Required |
| FM-04 | Branch/location attribution and consolidated reporting | Required |
| FM-05 | Internal cash and bank-label accounts, without bank connections | Required |
| FM-06 | Annual budget with monthly category allocations and approval | Required |
| FM-07 | Budget-versus-actual tracking and variance alerts in reports | Required |
| FM-08 | Daily/monthly/annual management reports and drill-through | Required |
| FM-09 | Recent business activities and Dataverse auditing | Required |
| FM-10 | Document reference and optional receipt attachment | Candidate; validate storage needs |
| FM-11 | iOS Shortcut through an authenticated Power Apps deep link | Later |
| FM-12 | Read-only conversational reporting | Later |
| FM-13 | External messaging, banking, or billing-system integration | Out of scope |

## Financial boundary

This release is **cash-oriented management control**: receipts and payments are posted when money moves. It must not label those totals as revenue recognized, sales invoiced, net invoicing, gross profit, or statutory net profit. A paid equipment acquisition is a cash outflow and may be classified as capital expenditure; it is not automatically an operating expense.

The approved category catalog is a management taxonomy, not the Peruvian Plan Contable General Empresarial (PCGE). No accounting entries, tax declarations, receivable/payable ledgers, or official financial statements are generated.

Expenses, payments, purchases, taxes, and investments are distinguished in data and reports. A purchase not yet paid is not represented as a payment; future purchase requests or financial commitments require a separate workflow decision.

## Users and authorization

Proposed business functions are Finance Manager, Treasury Clerk, Branch Operator, Branch Manager, Executive Viewer, and Auditor. Technical platform administration is a separate privileged responsibility. Permissions are enforced by Dataverse roles and record ownership, not client-side visibility alone.

Two branches will exist in production; test environments use synthetic branch names. A shared/corporate movement must be explicitly classified rather than automatically duplicated across branches.

## Out of scope

- Customer records, ISP operations, network inventory, subscriptions, and MikroWisp integration
- Banking authentication, payment transfers, and automatic statement reconciliation
- Electronic invoicing, SUNAT submissions, electronic payroll, and formal accounting
- Customer, employee, and supplier master-data management beyond any minimal non-sensitive transaction description
- Forecasts presented as certain outcomes, unauthorized AI access, and unrestricted financial query generation

## Confirm before production

- The cash-basis assumption and whether any previously invoiced amounts must be shown separately
- Exactly which movement types count toward each management result
- Opening balances, transfers between internal accounts, and how to represent shared expenses
- Budget owner, approval threshold, revision policy, and overspend behavior
- Whether proof-of-payment attachments are necessary, including retention rules
- Local reporting currency (proposed PEN), date cut-off (proposed America/Lima), and reporting periods
- Which roles may view both branches, approve budgets, or void posted movements
- Any employee compensation confidentiality requirements
- Existing source of periodic income figures and whether individual receipts are entered or summarized

See [Data model](data-model.md), [Reporting](reporting.md), [Screens](screens.md), and [Verification](verification.md).
