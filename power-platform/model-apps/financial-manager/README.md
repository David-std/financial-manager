# Financial Manager model-driven application

The first operational app is a **Power Apps model-driven application** created using the Power Platform CLI in the developer environment. It belongs to solution `FinancialManager`.

This is not a Canvas app. Power Platform CLI generative pages use React + TypeScript, Fluent UI v9 and Dataverse `dataApi`. The editable source for each page lives here, next to the generated `RuntimeTypes.ts` schema. The actual app/sitemap definitions are also present in `../../solutions/FinancialManager/src/AppModules/` and `.../AppModuleSiteMaps/`.

| Navigation page | Implementation | Current behavior |
| --- | --- | --- |
| Gastos | `expenses.tsx` | Read payment list, search/filter, show Posted totals on loaded rows, create a Draft and open its editor using record-context navigation |
| Presupuestos | `budgets.tsx` | Query budget versions and allocations, filter, open native record, clearly disable approval |
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

## Safety and known limits

- **Server is authoritative:** Do not implement financial state changes by bypassing the payment/budget plug-ins. The app does not grant extra Dataverse permissions.
- `FT_EnforcePaymentIntegrity` permits only System Administrators to Post/Void until roles are configured. `BG_EnforceBudgetIntegrity` deliberately refuses approvals.
- No test payments, budgets, or master data are embedded in the pages or committed to Git.
- **Gastos** creates a minimal Draft and navigates to the separate `expense-detail.tsx` generative page to complete its fields. The native model-driven payment form still contains only **Name and Owner**. The new editor includes Save Draft, Post and Void actions, but **interactive testing of input binding, updateRow payloads, navigation, and permissions is still pending**. Do not treat the full workflow as production-ready until this verification passes.
- The budget page shows aggregate allocations over loaded rows only. No approved budget exists for reliable budget-vs-actual reporting. Avoid presenting this as a complete analytical report.
- All four pages have been transpiled and published, but **interactive browser testing remains pending**. A successful CLI upload is not proof that all actions work in the rendered UI. Each page has a maximum of six linked Dataverse tables (the detail page uses five); `RuntimeTypes.ts` is a unified *source typing* reference containing seven tables, not a seven-table runtime binding.
- Data-source access, roles, app sharing and non-admin identity testing are not complete.
- The source-controlled solution still emits plug-in-assembly warnings when repacked and has not been import-tested into a separate tenant.

Specification: [screen responsibilities](../../../docs/screens.md), [security](../../../docs/security.md) and [UI acceptance checklist](../../../docs/ui-acceptance.md).
