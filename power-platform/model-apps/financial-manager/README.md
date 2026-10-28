# Financial Manager model-driven application

The first operational app is a **Power Apps model-driven application** created using the Power Platform CLI in the developer environment. It belongs to solution `FinancialManager`.

This is not a Canvas app. Power Platform CLI generative pages use React + TypeScript, Fluent UI v9 and Dataverse `dataApi`. The editable source for each page lives here, next to the generated `RuntimeTypes.ts` schema. The actual app/sitemap definitions are also present in `../../solutions/FinancialManager/src/AppModules/` and `.../AppModuleSiteMaps/`.

| Navigation page | Implementation | Current behavior |
| --- | --- | --- |
| Gastos | `expenses.tsx` | Read authorized payment list, filter loaded rows by text/status/scope/class, show Posted totals on loaded rows, create a Draft and open its editor using record-context navigation |
| Presupuestos | `budgets.tsx` | Create and edit Draft budget headers, add and adjust monthly allocations, inspect versions, and submit Drafts with confirmation. Server-enforced approval remains disabled. |
| Datos maestros | `master-data.tsx` | Read-only category/branch/cash-account catalogs and native record links |
| Detalle del gasto | `expense-detail.tsx` | Edit Draft amount/date/scope/category/account and document reference; request privileged Post and Void with reason; read-only after posting |

**Real environment results:** The CLI successfully transpiled four pages, uploaded and published each generated page, and attached all four to the app sitemap. `pac model list` showed Financial Manager published, while `pac model genpage list --app-id` returned the four real page IDs. The expense detail editor was initially created without a sitemap entry, then explicitly attached to make record-context navigation routable. **The latest Dataverse solution export confirms that its sitemap label is now `Detalle del gasto`.**

## How to update source

The CLI must be authenticated to the intended development environment before running anything. Never store tenant URLs, org IDs, tokens, or credentials in Git.

Run from this folder or use absolute paths:

```powershell
pac model genpage generate-types --data-sources "fmg_financialtransaction,fmg_budget,fmg_budgetallocation,fmg_category,fmg_branch,fmg_cashaccount,transactioncurrency" --output-file RuntimeTypes.ts
pac model genpage transpile --code-file expenses.tsx --data-sources "fmg_financialtransaction,fmg_budget,fmg_budgetallocation,fmg_category,fmg_branch,fmg_cashaccount"
```

When updating an existing page, supply its **actual page ID** using `pac model genpage upload --page-id`, not `--add-to-sitemap`. The latter is only for creating a new navigation entry.

App ID and page IDs can be discovered with:

```powershell
pac model list
pac model genpage list --app-id <app-id>
```

## Source formatting and reproducibility

The four application page files (`expenses.tsx`, `expense-detail.tsx`, `budgets.tsx`, `master-data.tsx`) are **editable, hand-maintained React/TypeScript source** consumed by Power Platform CLI. They can and should be formatted consistently without changing their semantics. Run from the repository root:

```powershell
npm ci --ignore-scripts
npm run format:app   # Only the four hand-maintained page files
npm run check:format # CI-enforced
```

Formatting rules are pinned in `package-lock.json`, `.prettierrc.json` and `.prettierignore`. The GitHub Actions quality job validates the results. Do not format or manually modify `RuntimeTypes.ts`: Power Platform generates it from Dataverse table metadata. Do not normalize `power-platform/solutions/` XML or solution exports; they are source-controlled vendor artifacts, not hand-authored application code.

Reformatting code in Git does **not** automatically update the running app. Transpile and upload each changed page to its **existing** page ID and verify by downloading its published source. Avoid page re-creation, sitemap reattachment, or changing budget approvals.

## Safety and known limits

- **Server is authoritative:** Do not implement financial state changes by bypassing the payment/budget plug-ins. The app does not grant extra Dataverse permissions.
- `FT_EnforcePaymentIntegrity` permits only System Administrators to Post/Void until roles are configured. `BG_EnforceBudgetIntegrity` deliberately refuses approvals.
- No test payments, budgets, or master data are embedded in the pages or committed to Git.
- **Gastos list scope:** visible filters operate on the Dataverse-authorized loaded subset, never all server records; a matching count is displayed and large histories explicitly direct users to native Dataverse search. Shared scope is labeled without pretending it belongs to a branch. This does **not** implement or prove branch-level authorization.
- **Expense detail safety:** Draft save checks optional category/branch/account/date validity, pre-reads editable fields to detect records changed in another session, verifies full field and lookup persistence, and resets the local baseline after a successful readback. Reload warns before discarding unsaved fields, and editing is disabled while a request is in flight. Post/Void retain permission checks, current-state reads and final server readbacks, including the void reason. **The stale-editor pre-read is best-effort only:** the generated-page `dataApi.updateRow` path does not demonstrate atomic ETag/If-Match; a second identity and competing-write acceptance remain necessary. The synchronous per-page guard only blocks rapid duplicate clicks in the same page. Direct updateRow transitions still depend on the installed synchronous Dataverse plug-in.
- **Gastos** creates a minimal Draft and opens `expense-detail.tsx`. Interactive record navigation, saving, clearing fields and Branch-to-Shared behavior were verified with a synthetic Draft and authenticated Dataverse readback. The reverse Shared-to-Branch transition, Post/Void permissions and non-admin identity checks still require acceptance.
- The budget page shows aggregate allocations over loaded rows only. No approved budget exists for reliable budget-vs-actual reporting. Avoid presenting this as a complete analytical report.
- Generated pages are compiled and published independently of Git. A successful CLI upload does not prove each interaction or access boundary; refer to the acceptance checklist. Each page has a maximum of six linked Dataverse tables (the detail page uses five); `RuntimeTypes.ts` is a unified *source typing* reference containing seven tables, not a seven-table runtime binding.
- Data-source access, roles, app sharing and non-admin identity testing are not complete.
- The source-controlled solution still emits plug-in-assembly warnings when repacked and has not been import-tested into a separate tenant.

Specification: [screen responsibilities](../../../docs/screens.md), [security](../../../docs/security.md) and [UI acceptance checklist](../../../docs/ui-acceptance.md).

## Scrolling and expense transition acceptance

All four model-driven generative pages use an explicitly bounded
vertical scroll area so the outer Power Apps shell can keep its fixed
height while the body scrolls on touch/mobile and desktop. Budget
tables remain independently horizontally scrollable and keyboard
focusable. Actual wheel/touch behavior must still be tested inside
the published app, not inferred from CSS contracts.

The **Post** button on a Draft now requires an explicitly saved,
reloaded/confirmed Draft: finish amount, date, flow class, scope,
branch if applicable, active leaf category, eligible account and PEN;
select **Guardar borrador**, verify the record, and only then choose
**Contabilizar**. Unsaved editor changes are rejected rather than
being auto-saved while posting. The app re-reads the Draft fields
before the state transition and re-reads the resulting status. Server
plug-in FTV007 remains the authority: non-admin finance users are
still denied privileged transitions until the role gate is accepted.
The generated-page dataApi pre-read does not prove atomic ETag
concurrency, and does not replace the server plug-in.

**Void** is offered only when the record is Posted; enter a nonempty
reason, confirm, and verify the resulting Voided status/reason. Posted
financial fields remain immutable, including via direct table/API
access. Never use a fictitious role selector or administrator bypass
to claim a production user authorization test passed.

Read-only metadata checks: `expense-readiness`,
`app-nav-inspect`, `budget-approval-inspect` (all in the
DataverseDeployment CLI). The existing `verify --apply` executes
controlled synthetic rollback-only Create/Post/Void and negative
mutation scenarios in DEV, with a deliberately failing final request
for each ExecuteTransaction; it must not be mistaken for UI UAT or
user-role/segregation acceptance. Keep evidence and screenshots
outside Git, and never run on production.
