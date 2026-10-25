# Verification and acceptance

This specification defines the acceptance criteria for Dataverse data integrity, workflow permissions, expense reporting, budget approval, auditing, and deployment. A passing repository check does not substitute for functional testing of Power Platform components.

## Transaction and data integrity

| ID | Scenario | Expected result |
| --- | --- | --- |
| TX-01 | Post a complete expense payment | Saved with correct payment class, period, branch scope, account, and creator |
| TX-02 | Submit zero, negative, excessively precise or invalid amount | Rejected with a clear message; no posted row |
| TX-03 | Post under a group code, inactive category, or unclassified code `999` | Rejected |
| TX-04 | Post to an unauthorized branch | Rejected by Dataverse security, not only the UI |
| TX-05 | Edit amount/date/classification of a posted movement | Rejected; corrections follow void and re-entry |
| TX-06 | Void without permission or an explanation | Rejected and traceable |
| TX-07 | Attempt to post `999` unclassified | Rejected pending a valid category |
| TX-08 | Submit a repeated integration request ID | Exactly one persisted movement |
| TX-09 | Submit a future amount through an alternate connector | Same server-side integrity checks as the model-driven app |
| TX-10 | Post payment of an equipment asset | Included in cash outflow, excluded from operating expense |

## Budget control

| ID | Scenario | Expected result |
| --- | --- | --- |
| BG-01 | Allocate approved categories by month and location | Annual totals equal monthly detail totals |
| BG-02 | Enter negative allocation or duplicate category/month/version | Rejected |
| BG-03 | Edit an approved allocation | Rejected; new revision required |
| BG-04 | Compare posted payments and approved budget | Exact match in relevant month, branch and category |
| BG-05 | Exceed approved allocation | Flagged as overspent; posting follows agreed override control |
| BG-06 | Record a payment without approved budget | Marked Unbudgeted; never silently treated as 0% use |
| BG-07 | Revise a budget in a later month | Only one effective approved version per month; no double count |
| BG-08 | Submit and approve the same budget without an approved exception | Rejected or flagged for compensating review |

## Security and audit

| ID | Scenario | Expected result |
| --- | --- | --- |
| SEC-01 | Branch user reads another branch's restricted record | Denied through direct Dataverse and application access |
| SEC-02 | Viewer tries to create, post, void, or approve | Denied |
| SEC-03 | Flow executes under elevated service credentials on behalf of an unauthorized user | Operation denied, not silently elevated |
| SEC-04 | Posted movement is voided or budget approval changes | Actor, time, and relevant before/after state appear in audit history |
| SEC-05 | User without permission accesses Power BI detail, export or underlying model | Denied |
| SEC-06 | Disable a category used by past transactions | Past transactions remain intact; new postings cannot select it |
| SEC-07 | Try to alter curated activity records | Denied to ordinary users |

## Financial reporting

| ID | Scenario | Expected result |
| --- | --- | --- |
| BI-01 | Reconcile reports against a fixed transaction fixture | Every measure agrees exactly with source data |
| BI-02 | Include draft and voided transactions | Excluded from all approved actuals |
| BI-03 | Compare month and prior-year counterpart | Correct dates and denominator handling; PY=0 displays N/A |
| BI-04 | Filter a specific branch vs consolidated scope | Shared movements counted only once; unauthorized rows excluded |
| BI-05 | Report operating expense and capital payment | Classified separately; combined only in total cash payments |
| BI-06 | Visualize spending exceeding budget | Text and formatting indicate overrun; not color alone |
| BI-07 | Request a period before the latest dataset refresh | Clear refresh timestamp; no implied real-time guarantee |
| BI-08 | Attempt to derive invoice revenue, profit, margin, or actual bank balances from expense payments | Metric is unavailable and not presented under an inaccurate accounting label |

## Operations

| ID | Scenario | Expected result |
| --- | --- | --- |
| OPS-01 | Export/import a solution between controlled environments | Components and required dependencies deploy correctly |
| OPS-02 | Recover a documented sample dataset | Recovery completes and reconciles within the approved target |
| OPS-03 | Re-run repository workflow | Formatting, required files and docs links pass |
| OPS-04 | Create new financial master-data values | Role access, validity and historical referential integrity preserved |

## Model-driven app acceptance

The following checks belong to the four-page **Financial Manager** model-driven application. Deployment and source validation alone do not satisfy browser-level or role-based acceptance.

| ID | Scenario | Expected result |
| --- | --- | --- |
| UI-01 | Open Financial Manager navigation | Gastos, Presupuestos, Datos maestros and Detalle del gasto appear with the expected labels |
| UI-02 | Create a minimal Draft in Gastos | The Dataverse record is created once and opens in Detalle del gasto with its `recordId` |
| UI-03 | Save a Draft with PEN amount, date, class, category, scope, branch and cash account | Values persist when reopening; lookup values correspond to the chosen records |
| UI-04 | Change a Draft from Branch to Shared | Branch lookup is cleared and only appropriate shared accounts are selectable |
| UI-05 | Attempt to Post without a valid category, date, account or other required fields | Dataverse rejects the action and preserves the Draft for correction |
| UI-06 | Post a fully valid Draft as an authorized administrator | Status becomes Posted and the editor no longer offers draft mutation |
| UI-07 | Try to modify a Posted payment through the interface or direct API | Server rejects the update; record remains unchanged |
| UI-08 | Void a Posted payment with and without a reason | Missing reason is rejected; authorized request with reason is accepted and immutable |
| UI-09 | Open Presupuestos | Versions and draft allocation totals are labeled appropriately; Approve is not offered and remains denied by the server |
| UI-10 | Open Datos maestros | Categories, branch and accounts display without catalog mutation actions in this page |
| UI-11 | Open as a non-administrator or branch-restricted user | App access and financial privileges are enforced by Dataverse, not visibility only; **deferred until a second test user is available** |

Do not mark UI-02 through UI-11 as passed merely because `pac model genpage transpile` or `upload` succeeds. The repository has automated **static** tests for UI source and sitemap consistency under `tests/test_model_app.py`, and C# plug-in tests for server-side rules.

## Evidence

Store fixture definitions, dates, execution results, screenshots, defects and remediation references in controlled external release records, not Git. CI should run repository/source contracts and plug-in unit tests; it does **not** replace authenticated Dataverse checks, DAX reconciliation or browser acceptance.

See [Data model](data-model.md), [Reporting](reporting.md), and [Security](security.md).
