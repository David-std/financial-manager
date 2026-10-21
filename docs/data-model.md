# Dataverse data model

Status: **Proposed**, not deployed. Names are preliminary logical contracts. Use the solution publisher prefix `fmg` for custom schema names; use platform-generated primary IDs, owners, created/modified users and timestamps. Display names may be localized for Spanish-speaking users.

## Core tables

| Table | Purpose | Representative custom columns |
| --- | --- | --- |
| `fmg_branch` | Operating location | `fmg_code` (Text, unique), `fmg_name` (Text), `fmg_isactive` (Yes/No) |
| `fmg_category` | Management income and expenditure taxonomy | `fmg_code` (Text, unique), `fmg_name` (Text), `fmg_parentcategory` (Lookup), `fmg_direction` (Choice), `fmg_isactive` (Yes/No) |
| `fmg_cashaccount` | Internal label for source/destination of payments | `fmg_name` (Text), `fmg_type` (Choice), `fmg_isactive` (Yes/No) |
| `fmg_financialtransaction` | Posted or draft cash-oriented transaction | See below |
| `fmg_budget` | Annual budget header with controlled versions | `fmg_year` (Whole Number), `fmg_scope` (Choice), `fmg_branch` (Lookup, conditional), `fmg_version` (Whole Number), `fmg_status` (Choice) |
| `fmg_budgetallocation` | Month/category spending allocation in one budget version | `fmg_budget` (Lookup), `fmg_month` (Whole Number), `fmg_category` (Lookup), `fmg_amount` (Currency) |
| `fmg_activityevent` | Read-only business workflow feed | `fmg_eventtype` (Choice), `fmg_recordtype` (Text), `fmg_recordid` (Text), `fmg_summary` (Text), `fmg_actor` (User Lookup), `fmg_eventtime` (Date and Time) |

The activity-event table is **not** a substitute for built-in Dataverse audit history. Write access must be restricted to trusted business operations; ordinary users can view only authorized events.

### `fmg_financialtransaction`

| Column | Dataverse type | Validation |
| --- | --- | --- |
| `fmg_transactiondate` | Date Only | Required, locally defined transaction date |
| `fmg_direction` | Choice | Receipt or Payment |
| `fmg_flowclass` | Choice | OperatingReceipt, OperatingPayment, CapitalPayment, TaxPayment, or OtherPayment |
| `fmg_amount` | Currency | Required and greater than zero; reporting currency PEN |
| `fmg_category` | Lookup | Required active leaf category, consistent with direction/class |
| `fmg_scope` | Choice | Branch or Shared |
| `fmg_branch` | Lookup | Required if Branch; empty if Shared |
| `fmg_cashaccount` | Lookup | Required active account |
| `fmg_description` | Multiline text | Optional, length limited, no secrets |
| `fmg_documentreference` | Text | Optional external receipt/document reference |
| `fmg_status` | Choice | Draft, Posted, or Voided |
| `fmg_voidreason` | Text | Required for an authorized void |
| `fmg_externalrequestid` | Text | Optional idempotency key for future integrations |

Use Dataverse currency precision appropriate for PEN and enforce a single transaction currency for this release. A payment for equipment can be a CapitalPayment; it is still included in total cash outflows but must not be silently treated as an operating expense. An expense allocation belongs to a leaf category; the 100–900 group labels are hierarchy nodes.

An optional document attachment requires separate decisions about storage, file type limits, malware scanning, permissions, and retention.

## Budget model

One `fmg_budget` identifies **year + scope + optional branch + version**. Its status is Draft, Submitted, Approved, Rejected, or Superseded. Monthly allocations are stored as `fmg_budgetallocation` rows; annual totals are derived, not independently edited.

A budget allocation is unique per budget version, month, and leaf category. An approved version and its lines are immutable. A change creates a subsequent version; reports select exactly one approved effective version per applicable period. No double counting across versions.

Only payment categories approved as budgetable participate in allocations. An over-budget posting generates a warning and requires the agreed override path; the reporting layer alone cannot authorize overspending. The initial design does not reserve budgets for unpaid purchases.

## Relational rules

- Branch 1:N FinancialTransaction, Budget
- Category 1:N FinancialTransaction, BudgetAllocation; Category 1:N child Categories
- CashAccount 1:N FinancialTransaction
- Budget 1:N BudgetAllocation
- SystemUser and owner teams determine creation and record visibility; do not build a second credentials table.

Transactions assigned Shared scope are not counted twice in each branch. A separate allocation method would be necessary to apportion shared spending; do not invent it.

## Integrity controls

1. Every posted movement has an authenticated actor, valid amount, active account, valid category, flow classification, and correctly scoped branch.
2. A posted movement is immutable. A correction is an authorized void plus a new movement, preserving traceability.
3. Voiding requires a reason and appropriate permissions; totals exclude voided movements.
4. Drafts do not appear in approved reporting measures.
5. Unknown/unclassified codes (including temporary categories) must be reviewed before posting.
6. Reusing a submitted external request ID must not create duplicate movements.
7. Approved budget versions and allocations cannot be silently edited.
8. Receipt totals, payment totals, and budget actuals are derived from clearly specified classes; neither capital purchases nor tax remittances are automatically treated as operating expense.
9. All cross-record, posting, and approval invariants require server-side enforcement. Power Fx or business rules alone are insufficient if records can be changed through another client.

## Deferred entities

Opening balances and transfers, accounts payable, invoices/sales, supplier master data, payroll, fixed-asset registers, cost center splits, commitment/reservation workflows, and multi-currency exchange rates are **not** silently implemented by these seven tables. Each needs a separate scoped decision.

See [Category catalog](category-catalog.md), [Reporting](reporting.md), and [Verification](verification.md).
