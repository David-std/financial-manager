# Project scope

## Purpose

Deliver a lightweight internal financial control and budgeting application for a small Peruvian internet service provider operating in two locations. The system supplements existing customer and billing software; it does not replace it.

This specification defines the business scope and requirements; it does not assert facts about operational processes that have not been established. Reference dashboards illustrate analytical capabilities, not a requirement to replicate their design or accounting terminology. The public repository must not identify the customer or contain production financial records.

## Approved expansion (2026-10-23)

The initial-release requirements below remain the **implemented expense-and-budget baseline**, but they are no longer a permanent product boundary. The project owner authorized phased expansion toward incoming payments, internal transfers, auditable balances and broader financial management. Those additions require their own Dataverse models, server validations, authorization and acceptance before they enter reports. This approval does not imply that revenue, cash balances or profit are already available.

The architectural decision and ordering are defined in [ADR 0003](decisions/0003-expanded-financial-scope.md). No legacy expense payment is reclassified as an incoming payment.

## Business outcomes

1. Record, review, and trace expense payments.
2. Understand paid spending by operating location and for the business as a whole.
3. Plan spending by month, category, and location, with an annual consolidated view.
4. Compare actuals against approved budgets and prior periods.
5. Separate operational spending from equipment investment and other non-operating cash outflows.
6. Make authorized changes and decisions traceable across multiple users.

## Functional scope

| ID | Capability | Initial release |
| --- | --- | --- |
| FM-01 | Authenticated access and role-based permissions | Required |
| FM-02 | Expense payment entry, posting, voiding, and searchable history | Required |
| FM-03 | Expense category management | Required |
| FM-04 | Branch/location attribution and consolidated reporting | Required |
| FM-05 | Internal cash and bank-label accounts, without bank connections | Required |
| FM-06 | Annual budget with monthly category allocations and approval | Required |
| FM-07 | Budget-versus-actual tracking and variance alerts in reports | Required |
| FM-08 | Daily/monthly/annual expense and budget reports with drill-through | Required |
| FM-09 | Recent business activities and Dataverse auditing | Required |
| FM-10 | Document reference and optional receipt attachment | Candidate; validate storage needs |
| FM-11 | Native iOS Shortcut and Power Automate intake for expense Drafts | Shortcut source generated; import signature, flow and iPhone validation pending |
| FM-12 | Read-only conversational reporting | Later |
| FM-13 | External messaging, banking, or billing-system integration | Out of scope |

## Financial boundary

This release is **expense-side, cash-oriented management control**: payments are posted when funds leave an internal account. Income, receipts, invoicing and complete cash-flow accounting are deferred. Spending must not be presented as revenue recognized, net invoicing, gross profit or statutory net profit. A paid equipment acquisition may be a capital outflow rather than an operating expense.

The approved category catalog is a management taxonomy, not the Peruvian Plan Contable General Empresarial (PCGE). No accounting entries, tax declarations, receivable/payable ledgers, or official financial statements are generated.

Expenses, payments, purchases, taxes, and investments are distinguished in data and reports. A purchase not yet paid is not represented as a payment; future purchase requests or financial commitments require a separate workflow decision.

## Users and authorization

Proposed business functions are Finance Manager, Treasury Clerk, Branch Operator, Branch Manager, Executive Viewer, and Auditor. Technical platform administration is a separate privileged responsibility. Permissions are enforced by Dataverse roles and record ownership, not client-side visibility alone.

Two branches will exist in production; test environments use synthetic branch names. A shared/corporate movement must be explicitly classified rather than automatically duplicated across branches.

## Out of scope

- Customer records, ISP operations, network inventory, subscriptions, and MikroWisp integration
- Banking authentication, payment transfers, and automatic statement reconciliation
- Income/receipt entry, electronic invoicing, SUNAT submissions, electronic payroll, and formal accounting
- Customer, employee, and supplier master-data management beyond any minimal non-sensitive transaction description
- Forecasts presented as certain outcomes, unauthorized AI access, and unrestricted financial query generation

## Confirm before production

- The cash-basis assumption and whether any previously invoiced amounts must be shown separately
- Exactly which payment classes count toward each management spending measure
- Opening balances, transfers between internal accounts, and how to represent shared expenses
- Budget owner, approval threshold, revision policy, and overspend behavior
- Whether proof-of-payment attachments are necessary, including retention rules
- Local reporting currency (proposed PEN), date cut-off (proposed America/Lima), and reporting periods
- Which roles may view both branches, approve budgets, or void posted movements
- Any employee compensation confidentiality requirements
- Whether a separate income/receipts module should be considered after the expense-only MVP

See [Data model](data-model.md), [Reporting](reporting.md), [Screens](screens.md), and [Verification](verification.md).
