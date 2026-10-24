# Financial Manager — semantic model contract

Status: **PBIP source and executive v2 implemented**, authenticated Desktop import and empty-fact aggregate reconciliation passed on 2026-10-24. Final visual acceptance with representative posted fixtures and publishing to Power BI Service remain pending. See [Executive v2 specification](EXECUTIVE-V2.md). The project is versioned under [FinancialManager/](FinancialManager/).
Source of truth: authenticated Financial Manager Dataverse, not files copied from the operational environment.
Primary baseline: posted expense payments and budget versions. Subsequent receipt and transfer domains require separate facts and measures; see [ADR 0003](../docs/decisions/0003-expanded-financial-scope.md).

## Connector and delivery

- Use **Power BI Desktop → Dataverse → Import** initially. The supported connector is `CommonDataService.Database`, with organization-account authentication. Disable navigation-property expansion where it causes excessive complexity.
- Define the `DataverseHost` Power Query text parameter locally, with the authorized environment *host name* (no protocol or trailing slash). Never commit the environment-specific value or credentials.
- A local network test confirms TCP 1433 connectivity from the development computer; this **does not** prove that the Dataverse TDS endpoint is enabled, that Power BI is authenticated or that import succeeds. Verify all three in Desktop.
- Save the actual report from Power BI Desktop as **PBIP**, using **PBIR** report definitions and **TMDL** for the semantic model. Do not treat this design document or the M starter query as a PBIP artifact.
- No extracts, PBIX, cached rows, tokens, tenant identifiers, financial data or customer data belong in this public repository.

## Source model and granularity

| Table | Grain | Eligibility / transformations |
| --- | --- | --- |
| `FactPostedExpense` | One `fmg_financialtransactionid` | `fmg_status = 797280001` (Posted). Require positive PEN amount and valid transaction date. An omitted Branch lookup is legitimate only for Shared scope. |
| `DimDate` | One business calendar day | Contiguous Date table. A date-only payment belongs to that local business day; timestamps use America/Lima. |
| `DimCategory` | One `fmg_categoryid` | Include the full historical category catalog and self-parent hierarchy; map group and leaf labels explicitly. Do not silently discard inactive categories referenced by history. |
| `DimBranch` | One `fmg_branchid` | Branch catalog with inactive historical members retained. Shared is **not** a third branch. |
| `DimCashAccount` | One `fmg_cashaccountid` | Internal source labels, not bank balances or bank credentials. |
| `DimFlowClass` | One option code | `OperatingPayment`, `CapitalPayment`, `TaxPayment`, `OtherPayment`. Validate each actual Dataverse option ID. |
| `DimScope` | One option code | `Branch` and `Shared`; unrelated to a Branch ID when Shared. |
| `FactBudgetAllocation` | One budget version, category, month | Hold allocations separately, with the parent `fmg_budget` version/approval/effective-month attributes. Include in approved-budget measures **only after** approval and effective-version selection are enabled and validated. |

The baseline fact relates many-to-one to each dimension; `DimDate[Date]` filters `FactPostedExpense[TransactionDate]`. Filters should flow from dimensions into facts, avoiding bidirectional relationships and accidental many-to-many paths. Join via GUID IDs, not descriptions or category names.

Branch-specific and Shared payments share one consolidated spending fact. Shared rows **must appear once** in consolidated totals; do not multiply them across La Merced and San Ramón. Display Shared as a separate scope, and do not infer branch allocation without a recorded allocation rule. Category hierarchies should map `Parent Category` lookup explicitly.

## Measures: proposed DAX definitions

These definitions assume the model has already mapped `AmountPEN`, `TransactionDate`, and numeric class IDs correctly. The base model and current 26-measure executive v2 model were parsed and queried in Power BI Desktop with the presently empty Posted/PEN fact. Positive-payment scenarios still need acceptance.

```dax
Paid Spending = SUM ( FactPostedExpense[AmountPEN] )

Operating Spending =
    CALCULATE ( [Paid Spending], FactPostedExpense[FlowClassCode] = 797280000 )

Capital Spending =
    CALCULATE ( [Paid Spending], FactPostedExpense[FlowClassCode] = 797280001 )

Tax Spending =
    CALCULATE ( [Paid Spending], FactPostedExpense[FlowClassCode] = 797280002 )

Other Spending =
    CALCULATE ( [Paid Spending], FactPostedExpense[FlowClassCode] = 797280003 )

Prior Year Spending =
    CALCULATE ( [Paid Spending], SAMEPERIODLASTYEAR ( DimDate[Date] ) )

Year-over-Year Change = [Paid Spending] - [Prior Year Spending]

Year-over-Year Change % =
    DIVIDE ( [Year-over-Year Change], [Prior Year Spending] )
```

Because `FactPostedExpense` is filtered to Posted at ingestion, its simple amount SUM can never include Draft or Voided. If a later model includes all states, the eligibility filter must instead live inside the measure and must be tested.

Do **not** return zero for missing approved budgets, missing comparison periods, unbudgeted payments or unknown balances. Budget metrics remain unavailable until a server-approved, effective version is available and tested. Receipts are not automatically revenues, and transfers are not operational income or spending.

## Reconciliation and permission gates

1. Inspect Dataverse option mappings, timestamps, source currency and lookup columns in Power BI Desktop against the live schema.
2. Compare imported Posted IDs and totals to an independently authorized Dataverse query over the same date/scope filters. Synthetic or approved testing data only.
3. Ensure each payment ID occurs once and that Shared spend is never replicated under each branch.
4. Test current-year and comparable prior-year Date filters, incomplete months, category hierarchy and blank/zero denominators.
5. Model and test Power BI row-level security (RLS) separately from Power Apps and source Dataverse security. Do not infer Power BI RLS from app form filters.
6. Inspect approved budget selection for concurrent versions, temporal effective-month logic and scope/category matching before creating utilization percentages.
7. Validate PBIP open, refresh, report pages, export and sharing in the target workspace before publishing to other users.

References: [reporting specification](../docs/reporting.md), [data model](../docs/data-model.md), [Power BI README](README.md). Connector reference: https://learn.microsoft.com/en-us/power-query/connectors/dataverse.
