# Power BI reporting

Reports will be stored in Power BI Project (PBIP) format after creation in Power BI Desktop. This directory currently holds no report source.

## Planned pages

1. Executive Overview — receipts, payments, operating cash result, budget position
2. Expense Analysis — hierarchical account matrix with current/prior-period variance
3. Budget Control — monthly category allocations, overruns and exceptions
4. Income and Trends — receipt evolution by day, month, year and operating location

Definitions, allowed financial terminology, and controlled reconciliation cases are maintained in [Reporting](../docs/reporting.md). Report data must be subject to role-appropriate visibility and have an explicit refresh timestamp.

Do not commit PBIX exports, dataset extracts, financial figures, local cache files or data-source credentials.
