# Power BI reporting

Financial Manager's reporting workspace will be maintained as a **Power BI Project (PBIP)**, with report definitions in PBIR and a source-controlled semantic model. The first versioned project is now in [FinancialManager/FinancialManager.pbip](FinancialManager/FinancialManager.pbip), with a PBIR report and a five-table TMDL semantic model. The [semantic-model contract](semantic-model.md) and [posted-expense M source](posted-expenses.pq) document eligibility and traceability. **TMDL parsing and PBIR schema checks pass, but Desktop data import, visual rendering and service publishing remain unverified.** The reporting requirements are in [docs/reporting.md](../docs/reporting.md).

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

## First PBIP project — implementation source ready, refresh not yet verified

Source-ready, non-secret starter files:

- `posted-expenses.pq`: **FactPostedExpense**, only Posted expense rows with PEN currency, no Draft/Voided records.
- `queries/DimCategory.pq`: all category names, codes, groups and parent links, including historical inactive categories.
- `queries/DimBranch.pq`: branches (inactive historical members retained).
- `queries/DimCashAccount.pq`: the internal account dimension, not cash balances.
- `queries/DimDate.pq`: independent continuous calendar covering 2020–2035.
- `measures.dax`: proposed paid-spending, classification, shared/branch and year-on-year DAX, requiring validation in Desktop.

Each `.pq` file is **one separate Power Query M expression**, except the main file which supplies the expense fact. Do not concatenate them into one query. In Power BI Desktop: create a local **text parameter** `DataverseHost` for the authorized Dataverse environment host (without the URL scheme), choose **Get Data → Dataverse → Organizational account → Import**, and build or paste each expression in the Advanced Editor. The local parameter value and login must stay out of Git. Mark `DimDate[Date]` as a date table, set one-to-many single-direction dimension relationships, and validate actual column types and GUID joins before using measures.

**Current account finding:** the browser Power BI session shows **UPC as its only selectable tenant**, despite the `company717.onmicrosoft.com` account being authorized in Power Apps/Dataverse. This does not mean those tenants merged. Authoring a local Desktop model against a Dataverse host can be attempted using the account with Dataverse access; publishing a workspace in the Financial Manager tenant remains a **separate tenant licensing and membership check**. The visible 59-day Power BI trial in UPC does not establish license rights in the Developer Dataverse tenant. Do not join/disconnect the PC from any organization or cancel trial access as a workaround.

**Implementation boundary:** the PBIP with four expense-analysis pages and sixteen configured visuals exists and passes source/schema/TOM checks; actual Power BI Desktop opening, authenticated refresh, screenshots of all four pages and model reconciliation remain pending. The project is not published to Power BI Service.

## Current versioned PBIP

The reproducible expense-analysis project is at [FinancialManager/FinancialManager.pbip](FinancialManager/FinancialManager.pbip).

- Five Import tables in TMDL: FactPostedExpense, DimDate, DimCategory, DimBranch, and DimCashAccount.
- Four single-direction fact-to-dimension relationships. Shared expenses have no branch and must appear once in consolidated spending.
- Twelve spending/payment measures; no approved-budget, income, cash-balance, revenue or profit measures.
- Four PBIR pages with sixteen bound native visuals: **Resumen de gastos**, **Sedes y movimientos**, **Análisis de categorías**, and **Tendencias de pagos**. Budget-control visualization stays unavailable until independent approval and version selection are validated.
- An official Power BI report CLI scaffold, valid PBIR schemas and successful Analysis Services TOM/TMDL deserialization. These are structural checks, **not evidence of successful refresh or rendered pages**.

Regenerate the model and report sources with the following commands from the repository root:

    python power-bi/tools/build_pbip_model.py
    python power-bi/tools/build_pbip_report.py
    powerbi-report-author validate power-bi/FinancialManager/FinancialManager.pbip

The report stores a DataverseHost **parameter placeholder**, not a real host or credential. When opening the PBIP in Power BI Desktop, use **Transform Data → Manage Parameters** to set DataverseHost to the verified Financial Manager Dataverse hostname. Authenticate with the authorized organizational account, then Import/Refresh. The user's Navigator screenshot confirmed that Desktop can enumerate **financial-manager-dev**. It did not prove these particular M queries have been executed or loaded.

Do not commit the locally edited parameter, PBIX exports, local settings, ABF cache, payment data or access tokens. Preserve the placeholder in version control and configure the endpoint locally. Review any Desktop-generated definition changes before staging them.

The final acceptance must verify SQL/TDS column names; Posted and PEN filtering; row counts and sums against Dataverse; GUID joins; that Shared totals are not duplicated into branches; date/YearMonth sorting; all four pages' rendering; RLS and refresh. Synthetic Draft expense data contributes nothing to Paid Spending until it is Posted, and no fictitious Posted rows should be introduced just to populate charts.

Budget variance reporting remains unavailable until a budget is independently Approved and effective. This first PBIP can be developed without invoking the pending approval API.
