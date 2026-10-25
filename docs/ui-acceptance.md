# Financial Manager — UI acceptance contract

This document defines **observable acceptance criteria** for the operational Power Apps and Financial Workspace interfaces. It is a stable validation contract, not a release-history or execution-status log. Record screenshots, execution dates, environment-specific IDs, defect investigations and transient checklists outside the repository. Source compilation, CI and publish do not establish user-acceptance success.

The authoritative scope and invariants are [screens](screens.md), [security](security.md), [data model](data-model.md) and [verification](verification.md). Live, irreversible financial actions must use clearly labeled, authorized development fixtures and explicit test safeguards.

## Power Apps — authenticated and authorized workflow

| ID | Scenario | Required observation |
| --- | --- | --- |
| UI-01 | Launch authenticated app | Navigation to Gastos, Presupuestos, Datos maestros and Detalle del gasto; no blank page or unexpected access |
| UI-02 | Expense list and filters | Reference, date, status, amount PEN, category, branch/scope and payment class correct; empty state clear; record-limit disclosure |
| UI-03 | Status and search | Draft/Posted/Voided filters return only accessible matching data; no bypass of branch/team security |
| UI-04 | Create Draft | A Draft record is created exactly once and Dataverse assigns its identity; never Posted from create |
| UI-05 | Detail navigation | Selected record ID is passed and the correct record is shown after opening, refreshing and returning |
| UI-06 | Edit Draft | Date, amount, class, scope, branch, active leaf category, cash account, description, optional reference and PEN currency round-trip correctly |
| UI-07 | Clear nullable Draft fields | Clearing optional text/lookups actually persists a null; stale data is not silently retained |
| UI-08 | Branch ↔ Shared | Branch→Shared clears prior branch association and accepts only permitted shared account; reverse path requires valid active branch/account |
| UI-09 | Post authorized Draft | Server validates all required values and privilege, transitions once to Posted, rejects stale retry and prevents subsequent arbitrary updates |
| UI-10 | Reject invalid Post | Missing/inactive/group/999 category, invalid or missing date, amount, account, scope/branch/currency are rejected while preserving Draft |
| UI-11 | Void authorized Posted | Authorized, reasoned Void accepted and audited exactly once; no financial fields or void reason can be retroactively changed |
| UI-12 | Budget authoring | Create versioned Draft budget and monthly allocations, update Draft, submit with confirmation; reject invalid lines and edits to submitted/approved versions |
| UI-13 | Catalog views | Categories, branches and accounts display active/historical data correctly; only authorized admins mutate actual catalogs |
| UI-14 | Loading, error and empty states | Server errors preserve input and never claim success; missing privileges are explained without leaking records |
| UI-15 | Keyboard, responsive and accessibility | Desktop, tablet and phone navigation; labels, focus, controls, contrast, alerts, no clipped data or page-wide horizontal overflow |
| UI-16 | Independent role and branch access | Verify non-admin owner/team scope, all denied Post/Void cases, shared-vs-branch isolation and audit access under real second identity |

**Budget approval gate:** Do not expose an effective Approve action while [ADR 0004](decisions/0004-safe-budget-approval.md) has not passed independent two-user role testing, separation of duties and competing-request rollback/concurrency acceptance. A hidden button is not permission. A simulated UI approval is not a real approval.

## Financial Workspace — synthetic preview only

| ID | Scenario | Required observation |
| --- | --- | --- |
| WX-01 | Data truthfulness | The 310 invented Posted payments reconcile with fixture totals, never with production claims; One persistent, discreet environment indication visible in the header |
| WX-02 | Period and scope filters | Month/year/category/scope filters update KPI, treemap, list and CSV identically |
| WX-03 | Category explorer | Rectangle area is proportional to selected Posted amount, category drill to accounts and actual fixture movements works, list alternative is operable by keyboard |
| WX-04 | In-memory Draft editor | Local-only Draft create/edit/discard validates fields, survives in-app navigation and resets on refresh; single global environment disclosure; no Dataverse connection |
| WX-05 | Transaction detail | Correct selected synthetic record, open/close panel, amount, account/scope and clear selection; no edit to Posted |
| WX-06 | Mobile | 390px phone and 768px tablet have no horizontal root overflow, filters and treemap keyboard/touch work, table cards fit |
| WX-07 | Security boundaries | No live financial endpoints, browser credentials, tokens, false role enforcement, posting, void or approval paths |
| WX-08 | Demo role clarity | If future role preview exists, it is explicitly illustrative, never treated as real access permission |
| WX-09 | Budget Draft planning | Create/edit Draft with month/category allocation, unique category/month pairs, total reconciliation, annual bars, discard; no approval or publish |
| WX-10 | Work queue | Counts and items are derived only from session Drafts and disappear after refresh; incomplete fields accurately flagged, navigation retains Draft |
| WX-11 | Activity feed | Only records actual local create/edit/delete actions in sequence; states non-authoritative; official Dataverse auditing is not simulated |
| WX-12 | Single status indication | Only the topbar identifies the demo environment; individual forms show normal professional language, without suggesting real persistence |
| WX-13 | Accessible treemap hierarchy | Map tiles use measured pixels; no clipped text in narrow cells; codes reconcile with adjacent full legend; matrix lists exact amounts and ticket per category; keyboard and focus operate at 320/390/430/768/1440 widths |
| WX-14 | Financial analytics consistency | Confirmed/PEN only, same available calendar months year-on-year, delta bridge adds to current total, groupings reconcile to row sums, Shared not duplicated, 2024 has no invented prior baseline, 2026 future months unavailable |
| WX-15 | Explanatory, actionable analysis | Category drivers, temporal comparison, heatmap, branch/account concentration, cash-outflow classification, sortable category ranking and largest payments point to the same underlying records; no revenue/profit/cash-balance figures |
| WX-16 | Month-specific time drill | Selecting a single month on home changes the trend to weekly buckets for that month; the displayed number of payments and amount reconcile, and excluded months are never shown as zeros |



The current React Workspace is **not** an authorized Dataverse client. A production interface requires Entra authenticated access, server-side role checks, record ownership isolation, activity/audit validation, and independent negative security tests before any real write.

## Reproducible automated tests

```powershell
python -m unittest discover -s tests -q
python scripts/check_repository.py
npm --prefix experiments/financial-workspace run build
node experiments/financial-workspace/analytics-model.test.mjs
node experiments/financial-workspace/smoke.mjs
powerbi-report-author validate power-bi/FinancialManager/FinancialManager.pbip
```

The browser smoke assumes an existing local preview at `http://127.0.0.1:5178/` and Microsoft Edge installed on the Windows device. Run it only with the local demo. Pass/fail of these tests does **not** replace actual acceptance of Dataverse write operations and permissions.

For release, validate source exports, installed solution compatibility, explicit signed-in role identities, debit/credit interpretation where applicable, server plug-in denial paths, budget versioning, recovery procedures, and Power BI publishing/RLS. Do not declare a real role or transaction accepted without corresponding server readback and denial tests.
