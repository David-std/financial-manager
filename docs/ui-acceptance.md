# Financial Manager — UI acceptance and release gates

Status: **planned manual/integration verification**, not a completed browser acceptance test.
Environment: `financial-manager-dev`. Branch: `develop`. Test payment records must be explicitly identifiable, created only in development and cleaned up if still Draft. Do not enter actual business payments during this exercise.

The four pages are published and their source/sitemap consistency is checked in CI. That does **not** establish that a page accepts record context, renders correctly, writes the expected Dataverse payload or enforces authorization for a real user.

## Acceptance scenarios

| ID | Page / action | Required observation | Test status |
| --- | --- | --- | --- |
| UI-01 | Open Financial Manager | Gastos, Presupuestos, Datos maestros and Detalle del gasto are present; no page shows a blank error | Not executed |
| UI-02 | Gastos list | Reference, date, status, class, category/branch and PEN amount render from accessible rows; a blank dataset has an explanatory state | Not executed |
| UI-03 | Search / status filter | A Draft, Posted and Voided record (when available) appears only in its appropriate filter; query truncation at 200 rows is disclosed | Not executed |
| UI-04 | Create Draft | Creates **Draft**, never Posted; reference is assigned by Dataverse; response supplies a GUID and opens the intended detail page | **Observed 2026-10-23:** User-created `MOV-000008` appeared in Gastos and was confirmed as Draft by authenticated FetchXML. |
| UI-05 | Record context | Clicking Abrir passes the selected `recordId` and `entityName` to the generative detail page; the page loads the exact record, not a blank or previous one | **Passed (user-observed 2026-10-23):** Authenticated runtime screenshot shows `MOV-000008`, matching FetchXML, Draft status and the original description; navigation URL contains record context. |
| UI-06 | Edit Draft | Date, amount, payment class, scope, active leaf category, branch (where relevant), cash account, description and reference persist after Save and refresh | **Passed 2026-10-23:** After correcting generated-page `dataApi` lookup payload keys, the user saved and refreshed `MOV-000008`; authenticated FetchXML joined to the referenced catalog tables and confirmed Date `2026-10-23`, Amount PEN 1.23, OperatingPayment, Branch, `LM` (La Merced), Category `601` (Energía Eléctrica), Cash Account `Caja La Merced`, `TEST-UI-008`, and Draft state. The earlier lookup regression is resolved in this tested path. |
| UI-07 | Clear Draft fields | Clearing a previously saved optional field must clear its persisted value or explicitly explain that it cannot be removed; the UI must not silently retain old amounts/relations | **Passed 2026-10-23:** User cleared saved document reference and Category from `MOV-000008`, saved and refreshed; authenticated FetchXML confirmed both values absent while Branch remained La Merced, Cash Account remained Caja La Merced, and Status remained Draft. |
| UI-08 | Branch / Shared transition | Changing to Shared removes the branch from the persisted record and uses a shared account; changing back to Branch requires a valid active branch | **Partial pass 2026-10-23:** User saved and refreshed `MOV-000008` as Shared using `Cuenta bancaria principal`. Authenticated FetchXML confirmed Scope Shared, a null Branch, a shared Cash Account, and Status Draft. **Reverse Shared → Branch is not yet tested.** |
| UI-09 | Post valid Draft | On authorized admin confirmation, the record transitions to Posted exactly once; the page becomes read-only; a stale-edit retry is rejected | Not executed |
| UI-10 | Reject invalid post | Zero/invalid amount, missing mandatory data, inactive/group/999 category, mismatched/inactive cash account and non-PEN currency are rejected by Dataverse | Server-side tests passed previously; UI not executed |
| UI-11 | Void valid Posted | Admin must provide a reason, no financial fields change, status becomes Voided and remains immutable | Server-side tests passed previously; UI not executed |
| UI-12 | Budgets | View annual versions and allocations without an actionable Approve control; an unauthorized direct status change to Approved remains blocked | Server-side approval block verified; UI not executed |
| UI-13 | Datos maestros | Active/inactive, group/leaf categories, branches and cash accounts render correctly; no accidental creation or mutation occurs in the consultative page | Not executed |
| UI-14 | Error and loading states | API failure preserves user input, shows an actionable readable error and never reports success for a failed write | Not executed |
| UI-15 | Responsive and keyboard use | Desktop, narrow/mobile width, tab navigation, form labels, focus and contrast remain usable | Not executed |
| UI-16 | Non-admin identity | Owner/team and finance role permissions, restrictions on Post/Void and branch access | **Deferred by project decision** |

## Highest-risk test sequence

1. Open Financial Manager through the authenticated Power Apps application, not a manually constructed URL.
2. From **Gastos**, create one clearly marked disposable Draft. Confirm a reference is created; test whether the editor opens with the same record ID.
3. Save complete but fictitious data with a valid active leaf category and branch/account pair. Refresh, compare every field and confirm the selected currency is PEN.
4. Remove one previously saved optional value and refresh. Record whether it truly cleared; if it did not, do not accept UI-07.
5. Test both the Branch and Shared routes, including cleanup of an old branch association.
6. Test posting and voiding only when the development account's privileges are known and after verifying the record is explicitly disposable. Posted and Voided records are intentionally immutable and cannot be deleted via ordinary operations, so avoid creating them casually.
7. Confirm errors and success notifications reflect server responses, not just button clicks.

## Known limitations and immediate release blockers

- Microsoft supports `Xrm.Navigation.navigateTo({pageType:"generative", pageId, recordId, entityName})`, but the target page **must be configured to accept** these input parameters. Verify this inside the running app. Reference: [Microsoft navigateTo](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto).
- The editor currently assembles an `updateRow` payload in two stages: save Draft, then request Posted. Confirm the request really persists and that a failed Post never shows a false success.
- Clearing the document reference and Category from an existing Draft passed UI-07 in the running app and was confirmed by FetchXML. Other optional fields and the Branch/Shared transition still require targeted tests; do not generalize UI-07 to every field without evidence.
- All summary amounts on generated pages are restricted to loaded records; they are **not** complete financial reports or approved budget-versus-actual metrics.
- The budget server plug-in deliberately forbids approval. Do not enable it through a button, flow, or modified user role.
- Direct Activity Event writes are blocked until a trusted server-side event writer is designed and tested. Dataverse native audit remains authoritative.
- The alternative full user/team role test is explicitly deferred.
- Solution export packs with known PluginAssembly component warnings; independent cross-environment import verification is still pending.

**Release decision:** no production acceptance until UI-01 through UI-15 pass with evidence, the required authorization tests are eventually completed, and an independently imported solution has been verified. Keep source changes exclusively on `develop` until review.

## Development deployment evidence — 2026-10-23

- Confirmed the `FinancialManagerDev` PAC profile was already authenticated to `financial-manager-dev`. No interactive sign-in or profile reset was needed.
- Updated **Detalle del gasto** and **Gastos** in the development model-driven app using their existing generated-page IDs; did not create or reattach pages.
- Power Platform CLI reported successful publish of both pages. Immediately downloaded each deployed page and compared its normalized TSX content with the source tree; both matched. The app still has four generated pages.
- All nine model-app source tests passed and both edited pages transpiled. A read-only FetchXML request verified connectivity, without reading business-level financial records.
- **Not an interactive acceptance pass.** UI-04 through UI-08 still require a disposable Draft, running-app navigation, saving/refreshing, clearing optional values, and checking Branch/Shared persistence. The CLI publish and source round-trip do not establish that `dataApi.updateRow` accepts every nullable lookup binding.
- No Posted or Voided records were changed, no budget approvals were enabled, and no Activity Event writes were made. Second-user role verification remains deferred.
- A fresh **unmanaged solution export** completed successfully to a local temporary ZIP (17 archive entries, including `solution.xml` and `customizations.xml`); this is not an independent import test and the ZIP was not added to the public repository.

## UI copy and observed Draft — 2026-10-23

- The user supplied screenshots of the **Power Apps designer previews**: Gastos displayed one Draft, `MOV-000008`; a separate standalone preview of Detalle del gasto displayed the no-record-context message. This standalone preview is not evidence that record-context navigation failed. Live `Abrir` navigation remains untested.
- A **read-only authenticated FetchXML** query confirmed `MOV-000008` in `fmg_financialtransaction` with description `Energia, octubre`, status `Draft`, and no payment date or amount. No record was created, posted, voided, or edited by the verification script. The user's already-created Draft can be used for UI-05 through UI-08.
- Removed redundant scope/development badges, internal technical commentary, and long disclaimer text from Gastos and Detalle del gasto. The loaded-row limit remains visibly described in KPI text and the existing `hasMore` notice.
- Both pages transpiled, were uploaded to their **existing page IDs**, and were published in `financial-manager-dev`. Fresh downloads of each page matched the repository source after normalizing BOM/end-of-file whitespace. The app still has exactly four generated pages.
- **Acceptance status unchanged** for UI-05–UI-08: run the model-driven application (not individual designer previews), open `MOV-000008` through `Abrir`, save fictitious fields, refresh and compare Dataverse, clear optional fields, then test Branch/Shared. Do not Post or Void.

## Live UI-05 acceptance and navigation-label blocker — 2026-10-23

- A user-provided authenticated, running-app screenshot (not the isolated maker preview) shows the `Detalle de gasto` page opened with record context for `MOV-000008`. The rendered reference, status Draft and description match the authenticated Dataverse FetchXML read. UI-05 is accepted on that evidence; UI-06 to UI-08 remain unverified.
- Read-only FetchXML confirmed the same record has no payment date or amount yet. Active catalog lookup identifies group `600 Servicios y operación fija`, leaf `601 Energía Eléctrica`, active `La Merced` branch and branch-specific `Caja La Merced` cash account. Use a fictitious `1.23` PEN, `2026-10-23`, `Pago operativo`, scope `Por sede`, and document reference `TEST-UI-008` to test Save Draft and reload. Do not Post or Void.
- **Navigation defect:** the authenticated app displays four items labeled `Nueva subárea` and a group `Nuevo grupo`, despite the source sitemap containing the intended titles. The live sitemap was read by the existing DataverseDeployment SDK utility; it has exactly the four expected page IDs and **six** default `SitemapDesigner.NewArea/NewGroup/NewSubArea` resource IDs.
- A guarded experiment attempted to remove those default resource IDs and add localized titles while keeping all page identifiers fixed. A backup XML was created in the computer's TEMP folder, but **the post-publish readback did not match**, and a second read-only inspection still found all six default IDs. The experiment is not considered successful. Its unverified source changes were reverted before commit. Do not retry blindly or use a full solution import just to update navigation labels; use the app's navigation designer, save/publish, then re-export and confirm the actual labels.
- No Financial Transaction record or catalog row was modified by these inspections. Approval of budgets and direct Activity Event writes remain blocked.

## Lookup persistence repair deployment — 2026-10-23

- Root cause: `expense-detail.tsx` used raw Web API `@odata.bind` keys inside the generative-page `dataApi.updateRow` call. Those keys can be ignored as unknown properties, leaving lookups unassigned without a write failure. The actual generated `RuntimeTypes.ts` contract defines `_fmg_category_value`, `_fmg_branch_value`, `_fmg_cashaccount_value`, and `_transactioncurrencyid_value` as writable `/<logical-singular>(<guid>)` references.
- Replaced the payload with typed FK fields and explicit `null` for cleared optional relationships. Added a post-write `dataApi.retrieveRow` check that verifies category, branch, account, and PEN currency, and reports mismatches instead of claiming success.
- All **18 Python repository tests** passed; the generated detail page transpiled successfully. The corrected page was published to the **existing** `Detalle del gasto` ID, then freshly downloaded and compared: deployed source matches `develop`.
- A fresh unmanaged `FinancialManager` solution export succeeded (17 entries, valid `solution.xml` and `customizations.xml`) to a local temporary ZIP, not the public repository. The correction's runtime Lookup persistence remains **pending explicit user Save/Refresh and authenticated Dataverse readback**; no test record was edited by the deployment.
- Sources: Microsoft Power Platform generated-page guidance on writable `_<field>_value` fields and local `RuntimeTypes.ts`; standard Web API `@odata.bind` is a different interface. Do not label UI-06 passed before the live retest.

## Expanded scope and budget authoring — 2026-10-23

- The project owner authorized **phased financial-module expansion** beyond the existing expense-and-budget baseline. See [ADR 0003](decisions/0003-expanded-financial-scope.md). This is not permission to bypass budget approval controls or to treat income as an expense transaction.
- A budget-authoring revision adds creation of a **Draft** annual budget (year, scope, active branch where relevant, version and effective month) and creation of nonnegative two-decimal PEN **monthly allocations** against active leaf expense categories, excluding code `999`. Creation requests use generated-page typed Dataverse foreign keys; server-side plug-ins remain authoritative.
- The existing budget Approval action is **not** enabled. No write paths for Approved status, Activity Event, master catalogs, or changes/deletion of existing budget records were added. The native `Abrir` form can still be limited to its system-generated fields and is not treated as full editing of an existing budget.
- The budget revision passed the repository's 19 static Python tests and generated-page TypeScript transpilation. **Interactive creation, uniqueness rejection, branch/scope authorization and monthly allocation readback remain untested** and must be evidenced before marking UI-12 accepted.
- UI-08 passed its **Branch → Shared** half with authenticated FetchXML confirming no Branch lookup and a valid shared bank account on `MOV-000008`; the reverse path remains pending. No payment was Posted or Voided.

## Budget-authoring deployment evidence — 2026-10-23

- The existing **Presupuestos** generated page was updated in place to expose **New Draft Budget** and **New Monthly Allocation** forms using typed Dataverse `dataApi.createRow` calls. Added active-branch and active-leaf selectors, PEN currency lookup and basic format validation. No approval button, history rewrite or Activity Event mutation was added.
- The page was transpiled successfully and published to the original budget page ID; a fresh download from the environment exactly matched the source committed on `develop`.
- The authenticated development table had **zero budget headers** before this rollout (aggregate read only). A successful publish therefore **does not verify the new Create UI workflows**. Test a clearly labeled synthetic Draft and one allocation with Save/Refresh/FetchXML before marking UI-12 passed.
- A new unmanaged `FinancialManager` solution export completed successfully with `solution.xml` and `customizations.xml`; it was held in TEMP and not committed. This is not an independent import/migration acceptance test.
- Power BI Desktop was installed locally using the official Microsoft Store package and launched. Authentication inside Power BI is a separate user action; no PBIP report was created or published in this rollout.
