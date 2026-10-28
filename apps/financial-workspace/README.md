# Financial Workspace — UX prototype

A responsive work-oriented interface for Financial Manager, developed separately from the live Power Apps operational application. This is an **offline frontend application**, not a Microsoft Fabric workload. It demonstrates how expense records, budget authoring, review queues, source-based analytics and activity history can coexist without turning the product into a dashboard-only experience.

**This project runs on synthetic data and local session state.** It is not a deployed Fabric App, an authenticated Dataverse client, or a substitute for the protected accounting workflows.

## Running locally

From the repository root:

```powershell
python scripts/export_workspace_demo.py
cd apps/financial-workspace
npm install
npm run dev
```

Open `http://127.0.0.1:5178/`. The development server binds to loopback. No tenant, Fabric capacity, login or bank credentials are needed to preview. The environment status is shown once in the app header and explained in the About dialog rather than repeated on every control.

For build and browser checks, start the development server and run:

```powershell
npm run build
npm run test:contracts
npm run test:analytics
node smoke.mjs
```

The browser smoke requires Edge at the configured Windows path. It exercises the real rendered UI, session transitions, Draft validations, budget allocations, treemap navigation, activity history, dark mode, refresh reset, and 390/768/1440-pixel layouts. Screenshots are written to the operating system temporary directory, not Git.

## Product areas

| Area         | Job to be done                                                                                                                                                                      | Notes                                                                                                                                    |
| ------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Inicio       | Orient a financial user, find work, view confirmed spending                                                                                                                         | Separates operations from analysis                                                                                                       |
| Mi trabajo   | Continue open expense and budget Drafts                                                                                                                                             | Computed from local records; no invented outstanding approvals                                                                           |
| Movimientos  | Prepare/edit Drafts and inspect/CSV-export historical Posted payments                                                                                                               | Drafts exist only in session; Posted payments are read-only                                                                              |
| Presupuestos | Prepare a version with editable category/month allocations, calendar and computed annual total                                                                                      | Draft only; no Submit/Approve                                                                                                            |
| Categorías   | Explore proportional treemap, switch between map/list/exact matrix, drill category → source account → confirmed payments                                                            | Tile labels adapt to measured pixel dimensions; narrow tiles show index only and use matching external legend; no invented subcategories |
| Análisis     | Compare same calendar months year over year, explain category deltas, inspect monthly heatmap, cash-account/branch concentration, category ranking, and navigate to source payments | Only confirmed expenses, no revenue/profit/cash balances or unapproved budgets                                                           |
| Actividad    | Understand the sequence of operations made in this session                                                                                                                          | This is **not** Dataverse's authoritative audit ledger                                                                                   |

In the home view, selecting a single month changes the trend to **weekly buckets of that month** instead of showing 11 artificial zeros; future months beyond available synthetic data cannot be selected. All analytics compare equivalent selected months and show absence of a prior-year baseline explicitly.
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
- Run npm run format:workspace and npm run check:workspace from the repository root to format/verify React TS/TSX, CSS, browser tests and configuration. Deterministic fixture JSON and vendor-generated code are excluded.
- The infrastructure/data/financial-repository.ts module supplies an immutable synthetic read model; conditional-draft.ts is the non-connected, fail-closed future authenticated ETag write contract. Neither grants any Dataverse privileges.
- Microsoft Fabric remains an optional integration (see integrations/fabric/README.md). Naming the frontend apps/financial-workspace avoids implying Fabric capacity or a deployed app before authorization.
- Keep data fixtures reproducible and version only deterministic synthetic data.
- Keep architectural contracts and tests in Git, but put temporary acceptance screenshots, execution logs, handoffs and progress plans outside the repository.
- Security and scope: [Product scope](../../docs/scope.md), [Server security](../../docs/security.md), [UI acceptance](../../docs/ui-acceptance.md), [ADR 0005](../../docs/decisions/0005-exploratory-financial-workspace.md).

## Delegated Dataverse transport boundary

The dormant infrastructure/data/dataverse-transport.ts adapter validates the Dataverse HTTPS origin, caller-verified user and organization using WhoAmI, and requires a token acquired by an actual Entra delegated flow; it never persists a token or permits caller-provided Authorization headers. It is not connected to this synthetic workspace. Run npm run test:transport for its mocked network security tests. Connecting it requires tenant-authorized SPA registration, actual entity metadata and fully accepted roles. Never claim successful live access based on a mocked test.

### Financial draft scope and version safety

Changing a synthetic Draft from branch to shared or back clears both branch and cash-account selections. Changing a branch also clears the account. The example dataset does not define authoritative account ownership, so the frontend cannot claim account eligibility; Dataverse must validate any real selection.

For a future delegated integration, use loadDraftForEdit to retrieve the opaque ETag before editing and conditionalDraftSave to PATCH with If-Match. test:concurrency simulates two separate sessions reading the same ETag and proves the later stale PATCH receives 412 and leaves the first edit intact. This is a deterministic simulation, **not** evidence that two real Dataverse users have completed UAT.

The repository CI now builds the offline React app, runs the simulated two-session ETag contract and launches a headless Chromium acceptance flow (navigation, local Draft Shared/Branch changes, analysis, mobile widths). This browser acceptance is synthetic; it does not certify live Dataverse permissions, audit trails or independent licensed identities.

## F4 delegated identity verification — opt-in, read-only (2026-10-27)

An isolated Microsoft Entra / Dataverse identity **test page** is now available.
This is **not a connected Workspace**, not an expense repository, not role
acceptance, and not authorization to post, void or approve anything.

1. Visit [Microsoft Entra App registrations](https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade),
   choose the **same tenant as financial-manager-dev**, and register a
   single-tenant **Single-page application (SPA)**.
2. In **Authentication → Add a platform → Single-page application**, add
   exactly `http://localhost:5178/`. Do **not** select Web, add a client
   secret, or enable implicit access/ID token flows.
3. In **API permissions → Add a permission → APIs my organization uses**,
   find **Dynamics CRM** (Dataverse), and grant the **delegated**
   `user_impersonation` permission. An administrator must grant consent
   when tenant policy requires it.
4. Copy `.env.example` to `.env.local` in this workspace directory.
   Set `VITE_FM_ENABLE_IDENTITY_CHECK=true`, the new **client ID**,
   **directory/tenant ID**, and the Dataverse **Organization URL** and
   **Organization ID**, independently verified using `pac env who`.
   Do **not** commit `.env.local`, screenshots with tokens, or secrets.
5. Run `npm ci` then `npm run dev -- --host localhost` and open
   `http://localhost:5178/?verify=identity`. Click **Iniciar sesión y
   verificar WhoAmI**. On success, the page confirms the Dataverse user
   and exact organization, without displaying the token or altering rows.

The existing `http://127.0.0.1:5178/` offline demo is unchanged. The
test page is inaccessible from the app unless the Vite flag is set and
the query `verify=identity` is supplied. Localhost is deliberately used
for the Entra SPA redirect; HTTP `127.0.0.1` can require manual manifest
editing in Entra and should not be used here.

This page uses the `@azure/msal-browser` authorization-code + PKCE
flow with an in-memory token cache, reads only Web API `WhoAmI`, and
checks the returned organization GUID. An ordinary user's successful
WhoAmI does **not** prove access to finance records, successful CORS in
every browser, role segregation or readiness of F3/F4 financial commands.

## F4 connected Workspace and visual acceptance, 2026-10-27

The Workspace now has two **explicitly separated data sources**:

- `/` is the interactive reporting and planning experience using local illustrative
  records. It identifies their provenance discreetly through **Datos de muestra**
  and a detailed **Sobre los datos de esta vista** dialog. Its session drafts
  are not persisted to Dataverse and must never be passed off as live records.
- `/?mode=live` is the opt-in connected operations screen, enabled only with
  `VITE_FM_ENABLE_LIVE_READ=true`. It uses the Entra delegated user session,
  verifies WhoAmI against the expected Dataverse organization and identity
  on every query, and fetches real authorized rows from seven exact entity
  sets (`fmg_financialtransactions`, `fmg_budgets`,
  `fmg_budgetallocations`, `fmg_activityevents`, `fmg_categories`,
  `fmg_branchs`, and `fmg_cashaccounts`). Each tab is independently
  paginated, with page-size 25; the loaded count is not a ledger total.
  A denied query stays denied and never falls back to sample records.

Only direct Draft create/read/conditional update/delete operations are exposed
to the user on the connected page. Create is explicitly Draft-only; updates
use the existing ETag `If-Match` contract and server re-read; deletion demands
confirmation, current Draft state and ETag. The UI provides no Post, Void
or approval action. The financial permissions, validation plugins and roles
in Dataverse still determine whether any requested mutation is allowed.

The **visual product pass** retains the successful Inicio and Análisis while
reworking Categorías (no floating year ornament, category concentration,
monthly distribution and spend per payment), Presupuestos (plan coverage,
amounts and assignments), Mi trabajo (readiness and unfinished fields),
Actividad (actual local event counts), and the connected experience.
Screen texts and tab metadata avoid recurring prototype jargon without
misrepresenting the source of financial records.

Metadata was verified in the authenticated DEV organization using
`workspace-metadata-inspect`; the listed entity sets and expense lookup
navigation names are read back from the server, not inferred by pluralization.
No financial records were created or approved during metadata verification.

**Remaining F4 gates:** interactive Entra browser login and row-level access
with genuine, non-admin identities; full category/branch/account lookup
selection and conditional persistence, end-to-end 401/403/412 behavior
against a connected user, cross-branch isolation, real activity permissions,
and Post/Void/budget approval through server-approved APIs. Do not report F4
accepted solely because TypeScript, mocked transports or visuals passed.

Verification commands from the repo root: `npm run check:workspace`,
`python -m unittest discover -s tests -q`; in
`apps/financial-workspace`: `npm run build`, `npm run test:analytics`,
`npm run test:contracts`, `npm run test:transport`,
`npm run test:concurrency`, `npm run test:live` and `npm run test:ui`
(with a Vite server at `127.0.0.1:5178`). `test:ui` captures desktop
and mobile screenshots to the Windows temporary directory, and tests
category hero overlap, horizontal overflow, keyboard navigation, and
the interactive planning and analytics flows.
