# Dataverse data model

## Overview

Financial Manager uses Microsoft Dataverse as the system of record for expense payments, management budgets, reference data, and auditable business actions. This specification defines the business schema and its invariants independently of deployment history.

The solution unique name is `FinancialManager`, with publisher prefix `fmg`. Custom logical names include that prefix exactly once. Dataverse supplies primary IDs, system users, ownership, timestamps, and standard audit metadata.

### Financial boundaries

- The application records **outgoing payments** for internal spending control. Income, collections, invoicing, sales and transfers are separate domains.
- Expense categories follow the supplied internal codes `100`–`900`; these are **not** Peru's PCGE account codes.
- Payment amounts are positive and expressed in PEN for this scope. Cash outflows are not necessarily accounting expenses of the same period.
- `OperatingPayment`, `CapitalPayment`, `TaxPayment` and `OtherPayment` have distinct reporting meanings. Category selection alone does not determine the payment class.
- A budget balance is an allocation less qualifying posted payments, **not** available cash in a bank account.

## Core tables

| Table | Record ownership | Responsibility |
| --- | --- | --- |
| `fmg_branch` | Organization | Operating locations for assignment and reporting |
| `fmg_category` | Organization | Expense code hierarchy and category metadata |
| `fmg_cashaccount` | Organization | Internal payment-source labels, optionally associated with a branch |
| `fmg_financialtransaction` | User or team | Expense payments, lifecycle and audit attribution |
| `fmg_budget` | User or team | Annual spending plans, revision and approval workflow |
| `fmg_budgetallocation` | User or team | Monthly allocations to expense categories; privileges and parent-budget access must be enforced |
| `fmg_activityevent` | Restricted writes | Curated business-event feed, supplementary to Dataverse auditing |

Dataverse role privileges, record ownership and parent relationships must be designed together. An organization-owned catalog does not grant all users permission to edit it. The ownership model of each custom table is a creation-time architectural decision.

## Reference data

### Branch — `fmg_branch`

| Column | Type | Constraint |
| --- | --- | --- |
| `fmg_name` | Primary text | Required, human-readable location |
| `fmg_code` | Text (10) | Required, unique alternate key |
| `fmg_isactive` | Yes/No | Required, default Yes |

The operating-location catalog has the entries La Merced (`LM`) and San Ramón (`SR`). These are branch records; **Shared** is a movement scope, not an invented third branch. Deactivate referenced locations instead of deleting them.

### Category — `fmg_category`

| Column | Type | Constraint |
| --- | --- | --- |
| `fmg_name` | Primary text | Required |
| `fmg_code` | Text (10) | Required, unique alternate key; preserve supplied numeric labels |
| `fmg_acronym` | Text (10) | Optional short label |
| `fmg_description` | Multiline text | Optional business definition |
| `fmg_parentcode` | Text (10) | Optional import/reference code; not the relational source of truth |
| `fmg_parentcategory` | Lookup to Category | Empty for groups; required for leaf categories |
| `fmg_isgroup` | Yes/No | Required |
| `fmg_isactive` | Yes/No | Required, default Yes |

The catalog contains nine group nodes (`100`, `200`, ..., `900`) and 32 leaf codes. The complete code-to-label mapping is maintained in [Category catalog](category-catalog.md). No `Direction` or default payment-class column belongs to this expense taxonomy.

**Hierarchy rules**

- Group nodes use `IsGroup = true`, have no parent and cannot be selected in a posted payment.
- Leaf nodes use `IsGroup = false`, have exactly one active group parent, and `ParentCode` must agree with that parent record's `Code`.
- Prevent self-parenting, cycles and assignment of one leaf as another leaf's parent.
- Leaf `999` (`No Definido`) may be retained in incomplete drafts but may not be posted.
- A category in use is deactivated rather than deleted. Existing category IDs and historical meanings must be preserved across renames or migrations.
- The 100–900 structure is exclusively for expenditure. New receipt codes, alphabetical prefixes and income direction options are not added to this catalog.

### Cash Account — `fmg_cashaccount`

| Column | Type | Constraint |
| --- | --- | --- |
| `fmg_name` | Primary text | Required payment-source label |
| `fmg_type` | Single Choice | `Cash`, `Bank`, `Wallet`, `Other` |
| `fmg_isactive` | Yes/No | Required, default Yes |
| `fmg_branch` | Lookup to Branch | Optional; empty for a shared/corporate payment source |

Branch-specific cash boxes belong to their corresponding branch. A shared bank label has no branch lookup. Its lack of branch association does **not** grant unconditional access to users in every branch.

Cash Account holds no banking credentials, actual account balance, reconciliation state, or bank integration. Whether `Type` uses a local or global choice definition is a solution-maintenance decision, not a business-data distinction.

## Expense payments

### Financial Transaction — `fmg_financialtransaction`

A transaction represents one outgoing payment. The table has **User or team ownership** so that branch-level record privileges can be enforced with Dataverse roles and teams.

| Column | Dataverse type | Constraint |
| --- | --- | --- |
| `fmg_name` | Primary text / AutoNumber | Unique, stable human-readable reference |
| `fmg_transactiondate` | Date Only | Required for posting; payment date in the business calendar |
| `fmg_amount` | Currency (2 decimals) | Required for posting; strictly greater than zero; PEN |
| `fmg_flowclass` | Choice | `OperatingPayment`, `CapitalPayment`, `TaxPayment`, `OtherPayment` |
| `fmg_category` | Lookup to Category | Required active leaf, not group or `999`, for posting |
| `fmg_scope` | Choice | `Branch`, `Shared` |
| `fmg_branch` | Lookup to Branch | Required and active if Branch; empty if Shared |
| `fmg_cashaccount` | Lookup to Cash Account | Required, active, permitted for that branch/scope |
| `fmg_status` | Choice | `Draft`, `Posted`, `Voided`; default Draft |
| `fmg_description` | Multiline text | Optional, size-limited, no credentials |
| `fmg_documentreference` | Text (100) | Optional supporting reference |
| `fmg_voidreason` | Multiline text | Required on authorized void |
| `fmg_externalrequestid` | Text (100) | Optional idempotency key for later authenticated integrations |

Dataverse's transaction currency must be verified as PEN on write, rather than inferred from the base-currency setting alone. System-created user/time metadata must be preserved.

### State transitions

| From | To | Preconditions |
| --- | --- | --- |
| New | Draft | Authorized creator; incomplete non-financial details may remain |
| Draft | Posted | Required fields, references, branch permissions, payment class and amount verified by the server |
| Posted | Voided | Authorized operator and documented void reason |

Posted records are immutable apart from a controlled void transition. Correcting an amount, date, category or scope requires retaining the voided record and creating a new payment. Neither drafts nor voided payments contribute to actual paid-spending measures.

Do not model income using a `Direction` choice in this table. Approval of an unpaid purchase request is also not equivalent to a posted payment; any commitment or purchase-request workflow must be modeled explicitly.

## Budgets

### Budget — `fmg_budget`

| Column | Type | Constraint |
| --- | --- | --- |
| `fmg_name` | Primary text | Human-readable budget reference |
| `fmg_year` | Whole number | Budget fiscal/reporting year |
| `fmg_scope` | Choice | `Branch`, `Shared` |
| `fmg_branch` | Lookup to Branch | Required for Branch scope, empty for Shared |
| `fmg_version` | Whole number | Positive version for the same year and scope |
| `fmg_effectivefrommonth` | Whole number | Month 1–12 when this version takes effect |
| `fmg_status` | Choice | `Draft`, `Submitted`, `Approved`, `Rejected`, `Superseded` |
| `fmg_approvedby` | Lookup to system user | Required upon approval |
| `fmg_approvedon` | Date and Time | Required upon approval |

An approval operation must verify approver authority and any separation-of-duties rule. An approved version is immutable; revisions create a new version. For each year, scope and month, reporting must select exactly one effective approved version.

### Budget Allocation — `fmg_budgetallocation`

| Column | Type | Constraint |
| --- | --- | --- |
| `fmg_name` | Primary text | Row reference |
| `fmg_budget` | Lookup to Budget | Required |
| `fmg_month` | Whole number | 1–12 |
| `fmg_category` | Lookup to Category | Required active leaf category |
| `fmg_amount` | Currency (2 decimals) | Non-negative budget amount in PEN |

Within one budget version, `(month, category)` must be unique. Users must have authorization for the parent budget before editing its lines; Dataverse does not automatically inherit all record privileges from a parent lookup. Budget lines are edited only while the owning budget is editable. Annual budget totals are derived from monthly lines; no independent annual amount may diverge from the sum.

Budget actuals are qualifying posted payments matched to month, scope, branch and category under an agreed classification policy. The existing Category catalog has no `IsBudgetable` or default flow-class attribute; budget eligibility must be an explicit rule rather than an inferred category property.

An overspend warning is not automatically a spending prohibition. A hard limit and exception/override workflow would need an approved policy with server-side concurrency protection. No unpaid commitment or reserved budget balance is implied.

## Business activity

### Activity Event — `fmg_activityevent`

| Column | Type | Constraint |
| --- | --- | --- |
| `fmg_name` | Primary text | Event label/reference |
| `fmg_eventtype` | Choice | Controlled business-event vocabulary |
| `fmg_recordtype` | Text | Related business table |
| `fmg_recordid` | Text | Related record ID |
| `fmg_summary` | Text | Concise, non-sensitive event description |
| `fmg_actor` | Lookup to system user | Originating user, verified server-side |
| `fmg_eventtime` | Date and Time | Event time |

The event feed exposes recent actions to authorized users. Ordinary users cannot directly create or modify activity events. Native Dataverse auditing is authoritative for row changes, prior values and privileged operations and must be enabled/configured separately.

## Relationship matrix

| Parent (1) | Child (N) | Child lookup |
| --- | --- | --- |
| Branch | Cash Account | `fmg_branch` (optional) |
| Category | Category | `fmg_parentcategory` (self-reference) |
| Branch | Financial Transaction | `fmg_branch` (conditional) |
| Category | Financial Transaction | `fmg_category` |
| Cash Account | Financial Transaction | `fmg_cashaccount` |
| Branch | Budget | `fmg_branch` (conditional) |
| Budget | Budget Allocation | `fmg_budget` |
| Category | Budget Allocation | `fmg_category` |

Use restrictive deletion behavior where available and enforce deactivation/revision rules on records referenced by payments or approved budgets. A shared payment must be counted once in consolidated totals; per-branch distribution requires a separately approved cost allocation rule.

## Validation and reporting invariants

1. Privileges and branch/team ownership are enforced in Dataverse, not just through a hidden button or form filter.
2. Monetary validation, required lookups, category status, ownership compatibility and workflow transitions are checked server-side for every client/connector.
3. Posting to a group, inactive leaf, or `999` is rejected; posted records cannot be changed in place.
4. External request IDs, when enabled, are deduplicated and tested against concurrent submissions.
5. Approved budget versions/lines are immutable and cannot be double-counted.
6. Only posted and non-voided expense payments enter Power BI actuals. Comparisons and allocations must reconcile to these source rows.
7. Shared amounts appear once in consolidated totals, not once per branch.
8. Spending analyses must distinguish operating, capital, tax and other payments. Classifications require explicit rules, not arbitrary inference from codes.
9. No measure may be labeled as profit, margin, revenue, invoicing, full cash-flow result, or verified account balance in an expense-only dataset.
10. Reports must state their refresh time and apply independently tested authorization.

## Separate domains

Receipts/income, customer billing, account balances and transfers, supplier payables, payroll, inventory/fixed assets, tax declarations, purchase requests/commitments, multi-currency, and attachment storage require separate models and decisions; they are not silently supplied by these tables.

See [Category catalog](category-catalog.md), [Scope](scope.md), [Reporting](reporting.md), [Screens](screens.md), [Security](security.md), and [Verification](verification.md).
