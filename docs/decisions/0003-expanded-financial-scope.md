# ADR 0003 — Broader financial-management scope

Date: 2026-10-23
Status: Accepted for phased implementation; individual financial workflows require their own acceptance gates.

## Context

The expense-and-budget-only initial scope was a practical implementation boundary, not a permanent product limitation. The project owner has authorized expanding Financial Manager toward a broader internal financial-control application. This authorization does not itself establish accounting policies, enable production integrations, or approve weakening transaction and budget safeguards.

The current Dataverse solution already contains seven tables, payment- and budget-integrity plug-ins, and a four-page model-driven application. The existing `fmg_financialtransaction` table is deliberately **outgoing payment only**.

## Decision

Extend the product by **separate financial domains**, not by reinterpreting expense-only fields and categories. Implement in validated increments:

1. **Expense-payment operations:** complete Draft/Posted/Voided acceptance, navigation, access checks, and documented correction flow.
2. **Budget operations:** author annual Draft plans and monthly category allocations; retain approval restrictions until roles, concurrent versions, effective periods, and separation of duties have been independently verified.
3. **Incoming money:** add a separate receipt domain with an explicitly defined lifecycle and internal account attribution. A receipt is a cash inflow, **not necessarily earned revenue** or an invoice.
4. **Internal transfers:** model linked transfer-out/transfer-in movements as one consistent business operation, with authorization and no double-counted consolidated cash movement.
5. **Balances and reconciliation:** add controlled opening balances, reconciled movements, and as-of balances only after defining the origin and integrity of each value. No direct bank integration is assumed.
6. **Power BI:** build a versionable PBIP (PBIR report + TMDL model), first reconciling posted payments. Extend metrics to receipts, transfers, cash balances and budget comparisons only when each corresponding domain exists and is validated.

## Invariants

- No extension repurposes the 100–900 **expense category** catalog as an income catalog.
- No new `Direction` field is added to `fmg_financialtransaction`; maintain independent domain entities and server-side lifecycle enforcement.
- Do not include any Draft or Voided financial entries in posted actuals; never invent balances, margin or profit from partial cash data.
- Approval cannot be enabled by changing a client button or bypassing a plug-in. It requires tested separate roles, approval authority, audit trail, and concurrency-safe budget version selection.
- Business operations need permission-scoped Dataverse access and immutable historical records. Avoid secrets, customer identities, real transaction data or bank identifiers in the public repository.
- Use `develop` exclusively. New generated solution components must be exported from the real environment, not fabricated as hand-written solution metadata.

## Delivery boundaries

The established expense-and-budget specifications document **implemented baseline v1**. This ADR permits additional planned domains, but none should be described as already implemented until table schema, plug-ins, application UI, automated tests, deployment and interactive acceptance all pass. Production release remains subject to [Verification](../verification.md).

See [Scope](../scope.md), [Data model](../data-model.md), [Architecture](../architecture.md), and [Reporting](../reporting.md).
