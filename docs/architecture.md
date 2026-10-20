# Architecture

## System boundary

The application remains within Microsoft Power Platform. Microsoft Dataverse is the authoritative operational store. Power Apps provides transaction entry and maintenance; Power BI provides read-oriented management reports. Power Automate is introduced only when a defined workflow needs it.

```text
Power Apps ──────────────┐
                         ├── Dataverse ── Power BI
Power Automate (optional)┘
       ▲
       │
Approved external integrations (future)
```

All ingestion paths must authenticate the caller and enforce the same business rules. A flow owner connection must not silently grant a caller the owner's unrestricted data access.

## Solution management

- Solution unique name: `FinancialManager`
- Publisher prefix: `fmg`
- Unmanaged solution in development; managed releases for production.
- Source files under `power-platform/` will be generated from an actual Dataverse solution using supported tooling.
- Power BI reports are maintained as Power BI Project (PBIP) text definitions under `power-bi/`.
- Environment-specific values are configured during deployment, not hard-coded in source control.

## Reporting semantics

The initial result is income less expense **within a selected transaction-date period**. It is a management cash-flow indicator, not accounting profit. Transactions in draft or voided status do not contribute to published totals. Account balances are not reported until opening balances and transfers are defined.

All reports must state period boundaries, reporting currency, and refresh time. Any model using imported data must disclose its refresh delay. Cross-currency aggregation is prohibited unless a supported conversion methodology is implemented.

## Integration boundary

- iOS: begin with an authenticated Power Apps deep link and user confirmation.
- Direct shortcut submissions: consider only after OAuth, caller attribution, input checks, and idempotency are demonstrated.
- Conversational reporting: use allowlisted, parameterized read-only queries returning deterministic numbers. A language model may interpret the question but must not invent amounts or execute arbitrary queries.
- WhatsApp: a separate channel decision with identity binding, per-user authorization, and cost review.

## Release approach

Development and production use separate environments and connections. A deployment requires reviewed solution artifacts, a security test, verified report figures, and a recovery procedure. Git history is not a database backup.

The current repository does not contain a deployed solution, cloud flow, or report.
