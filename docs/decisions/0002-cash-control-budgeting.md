# ADR 0002: Cash-oriented controls and approved budgets

- Status: Proposed; pending business review
- Decision date: 2026-10-20

## Context

The project references show hierarchical expense comparisons, financial summaries, budget use, income trends and branch filtering. The business wants multiple authenticated users and traceable changes but does not require replacement of its ISP billing system.

## Decision

Extend the initial prototype beyond a transaction ledger to include two operating scopes, annual budgets allocated monthly, budget revisions, a hierarchy of internal expense codes, and operational/audit views.

Adopt a cash-oriented reporting definition. Separate operating payments, asset acquisitions, and tax payments; do not present receipt totals as net invoicing or accounting profit. Preserve the supplied management codes as an internal taxonomy without treating them as PCGE.

Use Dataverse teams/roles for record authorization; use native auditing plus a limited business-activity feed. Keep Power Apps operational and Power BI analytical.

## Consequences

- Slightly larger data model, but reports have meaningful budget and branch context.
- Approval, posting, and revision rules require server-side enforcement.
- Production sign-off is necessary for the cash basis, category gaps, allocation of shared spending and finance-role responsibilities.
- Invoicing, accounting entries, cash balances, bank connections and external AI integrations remain separate decisions.
