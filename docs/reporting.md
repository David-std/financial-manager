# Financial reporting specification

These are management reports, not statutory financial statements. All figures must be traceable to authorized Dataverse records.

## Dataset scope

- Reporting currency: PEN for the first release.
- Business date: `fmg_transactiondate`; reporting timezone: America/Lima for timestamps.
- Transactions included: `Posted` only.
- Dimensions: Date, Branch/Shared, Category hierarchy, Account, Flow Class.
- Budget data: monthly allocations from the effective approved budget version only.
- Periods: day, month, quarter, year, and selected range.
- Every page shows the selected date range, location scope, data refresh timestamp, and whether shared/corporate movements are included.

## KPI definitions

Let `amount(t)` be the positive PEN magnitude of an eligible posted transaction in the selected date range and authorized scope.

| Measure | Definition | Exclusions / notes |
| --- | --- | --- |
| Cash receipts | Sum of Receipt transactions | No assumption of invoiced or accrued revenue |
| Cash payments | Sum of Payment transactions | Includes operating and non-operating payments |
| Net cash movement | Cash receipts minus cash payments | Not a statutory net profit |
| Operating cash payments | Sum of Payment transactions classified OperatingPayment | Excludes capital, tax, and other classes unless explicitly approved |
| Operating cash result | Operating receipts minus operating cash payments | Cash-oriented proxy, **not gross/net margin** |
| Capital spending | Sum of CapitalPayment transactions | Shown separately from operating payments |
| Approved budget | Sum of effective approved allocation amounts | Filtered by month, category, and branch/scope |
| Budget used | Qualifying posted payments matched to their approved budget category and period | Define whether Shared can be budgeted separately |
| Budget remaining | Approved budget minus budget used | Negative means overspend |
| Budget use % | Budget used / approved budget | Undefined when budget is zero; show N/A, not 0% |
| Prior-year variance | Actual selected period minus comparable prior-year period | Labels and signs depend on metric direction |
| Prior-year variance % | (Actual - Prior year) / ABS(Prior year) | When prior-year value is zero, show N/A |

Avoid summing ratios, mixing receipts with invoice totals, or using green/red indiscriminately. For expense metrics, an increase may be unfavorable; for receipts, an increase may be favorable. A negative variance is not automatically bad without business context.

### Prior-year comparison

Use a proper continuous calendar dimension, matching selected dates against the comparable prior-year dates. Show `AC` (actual current period), `PY` (prior year), `Δ PY`, and `Δ PY %`. If the reporting calendar and selected month yield incomplete periods, label the comparison as such.

### Budget health

Color bands are configurable business rules, not financial truths. Proposed initial convention: green within plan, amber near the limit, red only after a confirmed overspend. Do not assign a cutoff (e.g. 80%, 90%) before the budget owner approves it. When a category has spending but no approved allocation, display `Unbudgeted`, not an invented 0% utilization.

## Pages

### Executive overview

- Receipt, payment, net cash movement, approved budget, used budget, remaining budget.
- Monthly receipt/payment trends and annual comparison.
- Top expenditure groups by value.
- Exception strip for unbudgeted payments and overspent categories.
- Filters for date period and branch scope; cross-filtered drill-through to source movements.

### Expense analysis

- Matrix grouped as Group > Leaf category.
- Columns: current actual, comparable prior-year actual, absolute and percentage differences.
- Optional account and flow-class slices.
- Expand/collapse hierarchy; conditional formatting and data bars.
- Drill-through reveals transaction date, reference, authorized description, amount, account and scope.

### Budget control

- Budget by group/category, month and branch; actual vs approved allocation.
- Committed future spending is **not** included unless a separate commitment process is later implemented.
- Overspent, unbudgeted and inactive allocation exceptions.
- Version and approval status visible; do not silently blend draft budgets.

### Income and trends

- Daily/monthly/yearly receipts and their distribution by approved income category.
- Paid receipts trend by branch; values must be clearly called **receipts**, not net invoicing or net sales.
- Comparison of comparable periods; no seller/customer/product reporting without corresponding data.

## Reconciliation examples

Suppose a controlled period contains:

- Posted operating receipts: S/ 20,000
- Posted operating payments: S/ 11,000
- Posted equipment acquisition: S/ 4,000
- Approved operating allocation: S/ 13,000

Expected: total payments S/ 15,000; net cash movement S/ 5,000; operating cash result S/ 9,000; operating budget remaining S/ 2,000 (subject to category matching). The S/ 4,000 equipment purchase does not enter operating payments.

Shared movements appear only once in consolidated totals. They are not copied into each branch total absent an approved allocation policy.

## Technical notes

Use an appropriately defined star schema and explicit DAX measures. Consider matrix visual with row hierarchy and built-in conditional formatting. Track refresh timestamps; verify all aggregations against controlled transaction sets. Authorization for report and detail views must be tested independently from Canvas App access.

See [Data model](data-model.md), [Screens](screens.md), and [Verification](verification.md).
