# Power BI — Financial Manager

The Power BI Project in [FinancialManager/FinancialManager.pbip](FinancialManager/FinancialManager.pbip) is the source-controlled, editable expense-reporting surface of Financial Manager. It comprises a semantic model (TMDL) and four PBIR pages with independent desktop and phone layouts.

Power BI **analyzes authorized transactions** and must not become an alternative payment-entry workflow, authentication system, cash ledger, or approver UI. Consult the enduring [reporting specification](../docs/reporting.md), [data model](../docs/data-model.md), [security policy](../docs/security.md), and [semantic model contract](semantic-model.md) for financial and access-control requirements.

## Reports

| Page | Purpose |
| --- | --- |
| Resumen de gastos | Confirmed expense-payment KPIs, trends, composition and recent detail |
| Sedes y movimientos | Branch/shared attribution, accounts and transaction detail |
| Análisis de categorías | Category participation, comparative spending, volumes and classification |
| Tendencias de pagos | Annual and monthly evolution and spending composition |

The baseline uses five imported tables (`FactPostedExpense`, `DimDate`, `DimCategory`, `DimBranch`, `DimCashAccount`) and four dimension-to-fact relationships. Only **Posted** payments in **PEN** contribute to payment amounts. Draft and Voided are excluded; Shared payments count once rather than duplicating across branches. Approved-budget comparisons are unavailable until their separate authorization and effective-version gates have passed. There is no valid revenue/profit/cash-balance data source in this expense-only baseline.

## Authoring and verification

From the repository root:

```powershell
powerbi-report-author validate power-bi/FinancialManager/FinancialManager.pbip
python -m unittest discover -s tests -q
python scripts/check_repository.py
```

The PBIP stores a `DataverseHost` **parameter placeholder**. Only set that host in an authorized **local working copy** using Power BI Desktop (Transform Data → Manage Parameters), authenticate with the account authorized for the Financial Manager Dataverse environment, then refresh. Do not commit local settings, PBIX, cache, hostnames, tokens or financial rows.

For an isolated reproducible visual demonstration, use the synthetic fixture builder:

```powershell
python scripts/create_power_bi_demo.py
```

The builder works with invented payment records and saves its generated PBIP under a local temporary folder. The demo is not a publication or reconciliation against real records. [Power Query source](posted-expenses.pq) and [semantic model contract](semantic-model.md) describe table eligibility and model relationships.

The desktop-report generator is in `scripts/build_power_bi_v8.py`; the phone-layout refinement is `scripts/polish_power_bi_mobile_v9.py`. All generated PBIR sources must conform to schema and all changes require actual Desktop rendering checks, including filtered data, date labels, scrollable content and accessibility.

## F6 RLS security gate

The TMDL role `F6_DenyUntilAuthorizedBranchMembership` currently denies rows
across all five model tables. This is a **fail-closed interim role** and not
real per-branch authorization. Publishing the model is blocked until the
branch membership mapping, Viewer-only permission boundary, actual user
acceptance and data reconciliation are completed. See
[F6 reporting and RLS](../docs/f6-reporting-rls.md).

## Operational safeguards

- Reporting refresh can lag transaction writes; disclose freshness rather than suggesting immediate consistency.
- Published semantic-model access, export, identity, branch ownership and RLS must be independently accepted.
- Budget reports cannot represent Submitted/Draft allocations as Approved.
- Actual balances, receipts and transfers must be built from separate, approved domains and fully reconciled.
- Temporary Desktop inspection screenshots, iterative review narratives, release checkpoints and validation evidence belong **outside the repository**. Keep this file as stable documentation, not a chronological status log.
