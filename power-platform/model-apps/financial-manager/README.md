# Financial Manager model-driven application

The first operational app is a **Power Apps model-driven application** created using the Power Platform CLI in the developer environment. It belongs to solution `FinancialManager`.

This is not a Canvas app. Power Platform CLI generative pages use React + TypeScript, Fluent UI v9 and Dataverse `dataApi`. The editable source for each page lives here, next to the generated `RuntimeTypes.ts` schema. The actual app/sitemap definitions are also present in `../../solutions/FinancialManager/src/AppModules/` and `.../AppModuleSiteMaps/`.

| Navigation page | Implementation | Current behavior |
| --- | --- | --- |
| Gastos | `expenses.tsx` | Query expenses with Dataverse permissions, search/filter, show posted totals over loaded rows, create minimal Draft, open native record |
| Presupuestos | `budgets.tsx` | Query budget versions and allocations, filter, open native record, clearly disable approval |
| Datos maestros | `master-data.tsx` | Read-only category/branch/cash-account catalogs and native record links |

**Real environment results:** The CLI successfully transpiled all three pages, uploaded and published each generated page project, attached each page to the app sitemap, and registered the six required Dataverse data sources as app components. `pac model list` showed Financial Manager without the unpublished warning. `pac model genpage list` showed the three page IDs.

## How to update source

The CLI must be authenticated to the intended development environment before running anything. Never store tenant URLs, org IDs, tokens, or credentials in Git.

Run from this folder or use absolute paths:

```powershell
pac model genpage generate-types --data-sources "fmg_financialtransaction,fmg_budget,fmg_budgetallocation,fmg_category,fmg_branch,fmg_cashaccount" --output-file RuntimeTypes.ts
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
- Initial **Gastos** creates a minimal draft only. Its default model-driven record form from the initial export currently contains **Name and Owner**, not the complete payment fields. This must be replaced with a reviewed edit form before the app is considered ready for full operational expense entry. Do not advertise end-to-end posting from this UI.
- The budget page shows aggregate allocations over loaded rows only. No approved budget exists for reliable budget-vs-actual reporting. Avoid presenting this as a complete analytical report.
- All pages have been transpiled and published, but **interactive browser testing remains pending**. A successful CLI upload is not proof that all actions function in the rendered UI.
- Data-source access, roles, app sharing and non-admin identity testing are not complete.
- The source-controlled solution still emits plug-in-assembly warnings when repacked and has not been import-tested into a separate tenant.

Specification: [screen responsibilities](../../../docs/screens.md) and [security](../../../docs/security.md).
