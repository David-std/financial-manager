# Financial Workspace — UX prototype

A responsive work-oriented interface for Financial Manager, developed separately from the live Power Apps operational application. It demonstrates how expense records, budget authoring, review queues, source-based analytics and activity history can coexist without turning the product into a dashboard-only experience.

**This project runs on synthetic data and local session state.** It is not a deployed Fabric App, an authenticated Dataverse client, or a substitute for the protected accounting workflows.

## Running locally

From the repository root:

```powershell
python scripts/export_workspace_demo.py
cd experiments/financial-workspace
npm install
npm run dev
```

Open `http://127.0.0.1:5178/`. The development server binds to loopback. No tenant, Fabric capacity, login or bank credentials are needed to preview. The environment status is shown once in the app header and explained in the About dialog rather than repeated on every control.

For build and browser checks, start the development server and run:

```powershell
npm run build
node smoke.mjs
```

The browser smoke requires Edge at the configured Windows path. It exercises the real rendered UI, session transitions, Draft validations, budget allocations, treemap navigation, activity history, dark mode, refresh reset, and 390/768/1440-pixel layouts. Screenshots are written to the operating system temporary directory, not Git.

## Product areas

| Area | Job to be done | Notes |
| --- | --- | --- |
| Inicio | Orient a financial user, find work, view confirmed spending | Separates operations from analysis |
| Mi trabajo | Continue open expense and budget Drafts | Computed from local records; no invented outstanding approvals |
| Movimientos | Prepare/edit Drafts and inspect/CSV-export historical Posted payments | Drafts exist only in session; Posted payments are read-only |
| Presupuestos | Prepare a version with editable category/month allocations, calendar and computed annual total | Draft only; no Submit/Approve |
| Categorías | Explore proportional treemap, switch to readable list, drill category → source account → expense transactions | Uses filtered actual synthetic records, no invented subcategories |
| Análisis | Examine classified outgoing payments and follow PBIP source | Only expenses; no income/balances |
| Actividad | Understand the sequence of operations made in this session | This is **not** Dataverse's authoritative audit ledger |

Navigation, filters, controls and layouts adapt to mobile and tablet. The neutral petroleum/sage theme is designed for a financial operations workspace, not marketing or purely decorative cards.

## Session and financial semantics

- `src/data/demo.json` is generated from `scripts/create_power_bi_demo.py` by `scripts/export_workspace_demo.py`. It holds exactly **310 invented Posted payments / S/ 509,565.12** for 2024–2026, matching the Power BI offline demonstration.
- `WorkspaceStore.tsx` is the **single in-memory source** for user-created expense Drafts, budget Drafts and local activity events; it survives navigation within the SPA and intentionally resets on browser refresh. No IndexedDB, localStorage, sessionStorage, web API call or Dataverse write occurs.
- Expenses can be incomplete while Draft. Their data should not change the amount reported as **Posted**. Saving an Expense Draft does not create a confirmed payment.
- Budget totals are sums of lines for that selected plan. No approved allocation, executed spend control, or authoritative budget version is inferred.
- Activity events are generated **only when a local user creates, edits or discards** a session record. They are not presented as official approvals or a replacement for Dataverse auditing.
- The app has no real user role/branch authorization. **Never deploy this preview to business users** and never introduce a role selector that suggests server privileges.

## Required production architecture

**Dataverse remains the system of record.** The existing model-driven Power Apps UI and server-side transaction/approval validations continue to own actual finance commands. The Workspace must not become a parallel ledger, a duplicate role system, or an independent custom approval service.

A production Workspace would require Microsoft Entra delegated sign-in, real authorization and record ownership checks, secure endpoint access, server-based state transitions, authoritative audit entries, protection against stale writes and concurrency, and tests with separate real users. Microsoft documents [MSAL and OAuth for Dataverse](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/authenticate-web-api).

The protected `fmg_ApproveBudget` path must remain inaccessible until the independent approver and concurrency gates are completed. Read-only analytics must respect data-level permissions, cannot infer bank balances from spending, and must distinguish effective approved budget versions.

Fabric Apps is **optional** for the future analytics surface and may require Fabric capacity, tenant enablement, and appropriate semantic-model rights. It is not a requirement for local UX work and is not the present finance backend.

## Maintenance

- View-layer components are in `src/`; no real financial credentials or tenant endpoints belong in this repository.
- Keep data fixtures reproducible and version only deterministic synthetic data.
- Keep architectural contracts and tests in Git, but put temporary acceptance screenshots, execution logs, handoffs and progress plans outside the repository.
- Security and scope: [Product scope](../../docs/scope.md), [Server security](../../docs/security.md), [UI acceptance](../../docs/ui-acceptance.md), [ADR 0005](../../docs/decisions/0005-exploratory-financial-workspace.md).
