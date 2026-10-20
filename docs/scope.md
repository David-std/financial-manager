# Project scope

## Problem

The requested product is a small internal management tool for recording financial movements and understanding income, expenses, and their difference over time. Existing customer management, billing, and telecommunications operations remain outside the system.

This document describes the proposed scope, not an approved requirements baseline. The current operational process has not been observed.

## Proposed users

| Role | Primary need |
| --- | --- |
| Administrator | Maintain reference data, permissions, and exceptions |
| Operator | Enter and review permitted transactions |
| Manager | View authorized summaries and transaction details |

Final role definitions and user counts require discovery.

## MVP capabilities

1. Register income and expenses with dates, positive amounts, categories, accounts, and optional notes.
2. Maintain active and inactive income and expense categories.
3. Maintain the internal account list without connecting to banks.
4. Review, filter, and inspect financial movements.
5. Show period totals, trends, and category breakdowns in Power BI.
6. Apply Dataverse permissions, input validation, and record auditing.
7. Confirm and void transactions with traceable reasons; never hard-delete posted records.

## Exclusions

- ISP/customer-management integration
- Banking and payment initiation
- Electronic invoicing and tax filing
- General ledger, accrual accounting, and statutory financial statements
- Payroll, inventory, and accounts receivable/payable
- WhatsApp, autonomous AI, and direct HTTP transaction creation in the first release

iOS shortcuts and conversational reporting are optional follow-on integrations, not prerequisites for the core application.

## Discovery required before implementation

- How are income and expenses recorded and verified today?
- Who registers, reviews, corrects, and reads movements?
- Which categories and internal cash accounts are actually needed?
- What is the reporting currency, timezone, and period cut-off?
- Are transactions recorded when paid, invoiced, or approved?
- Do opening balances, transfers, or attachments matter?
- What reporting questions must management answer?
- What retention, backup, licensing, and approval requirements apply?

## Release boundary

The MVP is complete only after operational access rules and the acceptance tests in [Verification](verification.md) pass in a non-production environment. A functional screen alone is not sufficient for release.
