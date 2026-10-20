# Data model

The table and column names below are a **proposed contract**, subject to the AS-IS review. All custom Dataverse schema names use the solution publisher prefix `fmg`. Display labels may be localized without changing schema names.

## Tables

### `fmg_financialtransaction`

| Column | Dataverse type | Requirement |
| --- | --- | --- |
| Primary ID | GUID (platform generated) | Unique identity |
| `fmg_transactiondate` | Date Only | Required; business date |
| `fmg_direction` | Choice | Required: Income or Expense |
| `fmg_amount` | Currency | Required, strictly positive |
| `fmg_category` | Lookup to category | Required; matching direction |
| `fmg_cashaccount` | Lookup to cash account | Required; active |
| `fmg_status` | Choice | Draft, Posted, or Voided |
| `fmg_description` | Multiple lines of text | Optional; length limited |
| `fmg_voidreason` | Text | Required if a posted item is voided |
| `fmg_externalrequestid` | Text | Optional unique ingestion key for future integrations |

Use the platform-generated created/modified timestamps and user attribution. If an integration operates through a service identity, it must record the authenticated initiating user separately.

### `fmg_category`

| Column | Dataverse type | Requirement |
| --- | --- | --- |
| `fmg_code` | Text | Unique, stable category code |
| `fmg_name` | Text | Required |
| `fmg_direction` | Choice | Income or Expense |
| `fmg_parentcategory` | Self lookup | Optional, consistent direction |
| `fmg_isactive` | Yes/No | New entries use active categories |

Existing transactions retain references to inactive categories. Do not delete referenced categories.

### `fmg_cashaccount`

| Column | Dataverse type | Requirement |
| --- | --- | --- |
| `fmg_name` | Text | Required, unique among active accounts |
| `fmg_type` | Choice | Cash, Bank, Wallet, or Other |
| `fmg_isactive` | Yes/No | New entries use active accounts |

An account is an internal reporting label, not a connection to a financial institution. No bank account numbers or credentials are required.

## Relationships

- Category (1) to financial transaction (many)
- Cash account (1) to financial transaction (many)
- Parent category (1) to child categories (many)

## Invariants

1. Currency values represent non-negative magnitude; direction determines income or expense. Zero and negative amounts are rejected.
2. The first release supports one configured reporting currency. Summing different transaction currencies is invalid.
3. A transaction may become Posted only after server-side validation. Posted amounts, dates, categories, and account assignments are immutable.
4. Voiding a Posted transaction requires an authorized user, a reason, and audit evidence; it is excluded from current totals. Any effect on historical reports must be explicit.
5. An inactive category or account remains readable but cannot be assigned to a new posted movement.
6. Repeated submissions with the same external request ID must not create duplicate movements.
7. Reports use only Posted transactions and one consistently defined date boundary.

Column requiredness and UI rules alone do not establish all cross-table or state-transition invariants. Server-side enforcement must be implemented and tested across Power Apps and every additional ingestion route before production.

## Deferred decisions

Transfers, opening balances, attachments, budgets, approval workflows, multi-currency conversion, and formal reconciliation are not included until required by discovery.
