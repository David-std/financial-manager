# Power BI reporting

Financial Manager's reporting workspace will be maintained as a **Power BI Project (PBIP)**, with report definitions in PBIR and a source-controlled semantic model. The directory currently contains **no Power BI report or semantic model**. The [semantic-model contract](semantic-model.md) and [posted-expense Power Query M starter](posted-expenses.pq) are preparation only, not a working PBIP or validated source connection. The reporting requirements are in [docs/reporting.md](../docs/reporting.md).

## MVP reporting pages

1. **Executive Spending Overview** — paid spending, operating/capital classification, monthly trend, and authorized scope.
2. **Expense Analysis** — category group/leaf matrix, comparable periods, branch/shared filters, and record drill-through.
3. **Budget Control** — effective approved monthly allocations versus qualifying posted payments, with unbudgeted and overspend exceptions.
4. **Spending Trends** — daily, monthly, and yearly paid spending by payment class, category, and scope.

## Source and accounting boundaries

- Source: authorized Financial Manager Dataverse tables. Actual spending includes only **Posted** expense payments; Draft and Voided are excluded. Power BI Desktop authoring does not require Pro; Power BI service licenses and Fabric trial capacities are separate and tenant-specific. Verify that the account's selected tenant is the intended Dataverse organization before creating a shared workspace.
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

## First desktop model workspace (not yet a PBIP)

Source-ready, non-secret starter files:

- `posted-expenses.pq`: **FactPostedExpense**, only Posted expense rows with PEN currency, no Draft/Voided records.
- `queries/DimCategory.pq`: all category names, codes, groups and parent links, including historical inactive categories.
- `queries/DimBranch.pq`: branches (inactive historical members retained).
- `queries/DimCashAccount.pq`: the internal account dimension, not cash balances.
- `queries/DimDate.pq`: independent continuous calendar covering 2020–2035.
- `measures.dax`: proposed paid-spending, classification, shared/branch and year-on-year DAX, requiring validation in Desktop.

Each `.pq` file is **one separate Power Query M expression**, except the main file which supplies the expense fact. Do not concatenate them into one query. In Power BI Desktop: create a local **text parameter** `DataverseHost` for the authorized Dataverse environment host (without the URL scheme), choose **Get Data → Dataverse → Organizational account → Import**, and build or paste each expression in the Advanced Editor. The local parameter value and login must stay out of Git. Mark `DimDate[Date]` as a date table, set one-to-many single-direction dimension relationships, and validate actual column types and GUID joins before using measures.

**Current account finding:** the browser Power BI session shows **UPC as its only selectable tenant**, despite the `company717.onmicrosoft.com` account being authorized in Power Apps/Dataverse. This does not mean those tenants merged. Authoring a local Desktop model against a Dataverse host can be attempted using the account with Dataverse access; publishing a workspace in the Financial Manager tenant remains a **separate tenant licensing and membership check**. The visible 59-day Power BI trial in UPC does not establish license rights in the Developer Dataverse tenant. Do not join/disconnect the PC from any organization or cancel trial access as a workaround.

**Implementation boundary:** no PBIP project or actual Power BI refresh/visual has been verified yet. The files are a source-backed baseline for authoring the report, not a claim that a model has been published.
