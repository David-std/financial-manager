# Expense and budget reporting specification

These are management reports, not statutory financial statements. The **first analytical baseline** includes expense payments and spending budgets only. The product's phased expansion authorizes future incoming payments and transfers, but these domains do not yet exist in this source dataset; income, customer receipts, margins, profit and complete cash flow cannot be inferred from its current tables. See [ADR 0003](decisions/0003-expanded-financial-scope.md) and [semantic-model preparation](../power-bi/semantic-model.md).

## Dataset scope

- Reporting currency: PEN; confirm transaction currency explicitly.
- Payment date: proposed `fmg_transactiondate`; reporting timezone: America/Lima for timestamps.
- Actuals include `Posted` expense payments only. Exclude Draft and Voided.
- Dimensions: Date, authorized Branch/Shared scope, Category hierarchy, Cash Account, and expense Flow Class.
- Budget values derive only from effective approved monthly allocations.
- Periods: day, month, quarter, year, and selected range.
- All pages show active filters, Shared inclusion and data refresh timestamp.

## Measures

Let `amount(t)` be the positive PEN amount of an eligible posted payment in the authorized scope.

| Measure | Definition |
| --- | --- |
| Total paid spending | Sum of all posted expense-payment amounts, including non-operating outflows |
| Operating payments | Sum of `OperatingPayment` class; no automatic inclusion of capital/tax payments |
| Capital payments | Sum of `CapitalPayment` class, shown separately |
| Tax payments | Sum of `TaxPayment` class; not a tax determination |
| Other payments | Sum of `OtherPayment` class, reviewed separately |
| Approved budget | Effective approved allocation amounts in matching month, category and scope |
| Budget used | Qualifying posted payments matched to approved allocation |
| Budget remaining | Approved budget minus budget used |
| Budget use % | Budget used / approved budget; display N/A when approved budget is zero |
| Prior-year difference | Current paid spending minus comparable prior-year paid spending |
| Prior-year difference % | (Current - Prior year) / ABS(Prior year); display N/A when prior year is zero |

Do not sum percentages, treat missing information as zero, or interpret red/green without numerical and textual context. An increase in spending is not automatically favorable. Payments with no approved allocation are `Unbudgeted`, not 0% utilized.

### Period comparisons

Use a continuous calendar and comparable periods. Labels `AC`, `PY`, `Δ PY`, and `Δ PY %` must match the actual dates. Show when the current reporting period is incomplete.

### Budget health

Color bands and override thresholds require business approval. Showing an overspend warning does not itself establish an enforced spending limit.

## Report pages

### Executive spending overview

- Total spending, operating/capital split, approved budget, budget used and remaining.
- Monthly trend, prior-period comparison, top categories and branch distribution.
- Unbudgeted and overspent exceptions; drill-through to original payment records.

### Expense analysis

- Expandable Group > Leaf category matrix with current, prior-year, absolute and percentage variances.
- Filters for date range, branch/shared scope, category, payment class and cash account.
- Accessible data bars and conditional formatting with textual meaning.
- Drill-through shows authorized date, reference, amount, description, payment account and scope.

### Budget control

- Approved allocations versus qualifying posted payments by month, category and branch/shared scope.
- Version, effective period and approval status visible.
- Overspent, unbudgeted and categorization exceptions.
- Unpaid commitments are not payments and do not consume actuals in the initial design.

### Spending trends

- Daily/monthly/yearly payment evolution and category distribution.
- Operating, capital, tax and other approved spending classes.
- Branch and Shared comparisons with security enforced outside visuals.
- No customer, seller, sales, invoices, receipts, or income reports without separate approved data.

## Reconciliation example

If posted operating payments total S/ 11,000, posted equipment purchases total S/ 4,000, and the matching approved operating allocation totals S/ 13,000, the result is S/ 15,000 in total paid spending, S/ 11,000 in operating payments, S/ 4,000 in capital payments, and S/ 2,000 operating budget remaining. This assumes exact alignment of approved category, month and scope. It does not establish bank balance or profit.

Shared payments appear once in consolidated reporting and are not duplicated in branch totals unless an approved allocation method is introduced.

## Technical notes

Use a star-schema semantic model with explicit validated DAX measures. Reconcile against controlled Dataverse fixtures, show refresh timestamps, and test Power BI report, export and detail access independently from Power Apps.

See [Data model](data-model.md), [Screens](screens.md), [Scope](scope.md), and [Verification](verification.md).
