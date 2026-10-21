# Architecture

## Components

```text
Users and roles (Microsoft Entra ID)
               |
          Power Apps
     transaction / budget UI
               |
     Dataverse business data
       |               |
   Power BI       Dataverse audit
  semantic model  and business events
       |
  Reviewed measures and reports

Power Automate: future notifications and authenticated integrations
```

Dataverse is the system of record for operational data. Power BI is read-oriented and must not become a second, manually maintained financial ledger. Authentication and authorization stay within Entra ID and Dataverse. No custom backend is necessary for the initial release.

## Solution and source control

- Solution unique name: `FinancialManager`
- Publisher prefix: `fmg`
- Development: unmanaged solution in an isolated developer environment
- Production: managed solution deployment to a separately administered environment
- `power-platform/`: supported solution source generated from the real environment using Power Platform CLI
- `power-bi/`: Power BI Project (PBIP) definitions, without local caches or confidential exports

Never handwrite fake Canvas App, Dataverse, or solution export artifacts merely to populate GitHub.

## Data and authorization boundaries

- Dataverse system users and security roles are authoritative. Avoid maintaining parallel application passwords and permission flags.
- Use branch-specific owner teams or another validated Dataverse record-security configuration. A branch selector is not an access control.
- Shared/corporate movements belong to an explicitly authorized owner/scope, not both branch teams by default.
- The server must enforce posting, voiding, budget approval, and category-consistency rules across every client and connector.
- Dataverse auditing captures source-record changes; a separate read-only activity feed can expose selected business events to authorized users.

## Cash-oriented reporting

- Posted receipts and payments drive cash-flow measures.
- Operating cash result excludes capital acquisitions and other separately defined non-operating payment classes.
- Budget utilization compares approved allocation values with qualifying posted payments only; unpaid commitments do not appear in actual payments.
- Financial measures use a declared reporting calendar, PEN currency, and explicit movement-class mappings.
- Summary totals must reconcile with the drill-through movements; missing source data must not be silently replaced with zero.

If balances are requested, account transfers and opening balances require explicit modeling before they can be reported credibly.

## Power BI model

Prefer a star-schema semantic model with transaction facts, approved budget allocation facts, and shared dimensions (date, branch, category, cash account where relevant). Separate measures for receipts, payments, operating expenditure, capital spending, and cash result.

Initially favor Import mode with documented refresh timing and security. Reassess DirectQuery only if justified by freshness, scale, licensing, and measured performance. Never assume a report reflects an immediately saved movement.

## Integration boundaries

- iOS: authenticated Power Apps deep link with confirmation first.
- Direct entry later: OAuth, user attribution, validation, replay protection, and request deduplication.
- Conversational reports later: allowlisted read-only queries, scoped by user authorization, with deterministic numerical results.
- WhatsApp or other messaging: separate identity, data access, cost, and privacy review.

## Deployment gates

A release requires audited tables, tested role privileges, validated invariants, approved financial definitions, reconciliation with sample movements, report access tests, backup/restore provisions, and compatible licenses.

For the functional boundaries, see [Scope](scope.md). For the schema and posting rules, see [Data model](data-model.md).
