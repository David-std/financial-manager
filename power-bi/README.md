# Power BI reporting

Financial Manager's reporting workspace will be maintained as a **Power BI Project (PBIP)**, with report definitions in PBIR and a source-controlled semantic model. The directory currently contains **no Power BI report or semantic model**. The [semantic-model contract](semantic-model.md) and [posted-expense Power Query M starter](posted-expenses.pq) are preparation only, not a working PBIP or validated source connection. The reporting requirements are in [docs/reporting.md](../docs/reporting.md).

## MVP reporting pages

1. **Executive Spending Overview** — paid spending, operating/capital classification, monthly trend, and authorized scope.
2. **Expense Analysis** — category group/leaf matrix, comparable periods, branch/shared filters, and record drill-through.
3. **Budget Control** — effective approved monthly allocations versus qualifying posted payments, with unbudgeted and overspend exceptions.
4. **Spending Trends** — daily, monthly, and yearly paid spending by payment class, category, and scope.

## Source and accounting boundaries

- Source: authorized Financial Manager Dataverse tables. Actual spending includes only **Posted** expense payments; Draft and Voided are excluded.
- Currency: PEN. Shared movements must be counted **once**, not copied into each branch total.
- Budget measures must use **approved and effective** allocations. Budget approval is intentionally blocked in the current development environment; do not show Draft/Submitted allocations as approved budgets. Unavailable budget comparisons must be labeled **not available**, not zero.
- No receipts, income, sales, invoicing, cash balances, net cash flow, margin, or profit metrics are supported by the MVP.
- Model relationships and DAX measures must be reconciled against controlled synthetic data. Preserve explicit filter context, period comparability, access control, and a data refresh timestamp.

## Delivery gates

1. Confirm Dataverse TDS/Power BI organizational authentication and option/lookup metadata. TCP 1433 reachability alone does not prove Dataverse TDS is enabled or that the semantic model refresh works.
2. Create the star-schema semantic model, a calendar table, and tested DAX measures.
3. Create the four report pages, with a clear empty/not-available state when there are no eligible posted payments or approved budgets.
4. Test reconciliation, branch/shared logic, drill-through, RLS/authorized access, and refresh against development fixtures.
5. Validate the PBIP with Power BI Desktop before any publication and check licensing and distribution requirements separately.

Do **not** commit PBIX exports, actual financial records, extracted datasets, cache files, tenant-specific endpoints, secrets, or credentials. Keep versioned sources exclusively on `develop` until review.

Related specifications: [Reporting](../docs/reporting.md), [Scope](../docs/scope.md), and [Verification](../docs/verification.md).
