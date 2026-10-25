# Financial Workspace — UI exploration (v1)

An independent, **local, offline interface prototype (read-only with respect to Dataverse)** for Financial Manager, inspired by a quiet workplace interface, accessible data exploration, and contextual operation details. This is **not** a published Fabric App, a Canvas App, or a replacement for the live Power Apps/Dataverse application.

## Try locally

From the Financial Manager repository root:

```powershell
python scripts/export_workspace_demo.py
cd experiments/financial-workspace
npm install
npm run dev
```

Open http://127.0.0.1:5178/. The dev server binds only to loopback. The app needs no account or Fabric subscription to preview.

To validate:

```powershell
npm run build
node smoke.mjs
```

The browser smoke checks use Microsoft Edge installed on Windows (adjust the executable in `smoke.mjs` on other platforms) and capture 1440px desktop and 390px mobile screenshots under the OS temporary directory. Its smoke scenarios exercise year/month selection, reset, search, transaction details, budgets, analytics and dark mode, and check JavaScript errors and mobile horizontal overflow.

## Prototype sections

- **Inicio:** contextual financial overview, year/month selector, scope/category filters, spending trends, category composition, latest movements, spending classification.
- **Movimientos:** reference/category/branch search, mobile-native payment history, read-only detail drawer, synthetic CSV export, and **in-memory Draft creation/edit/discard** with validation. No server writes or posting.
- **Categorías:** proportional treemap with drill-down into payment-source accounts, amounts, counts and filtered detail; keyboard-operable list alternative.
- **Presupuestos:** informational restricted-workflow design. **No approval, reject, submit or financial action** is exposed.
- **Análisis:** lightweight category and cash-outflow exploration, with a link to the governed PBIP source—not a pretended live embedded report.
- Responsive desktop/mobile layout and a user-selectable dark appearance.

## Data and truthfulness

`src/data/demo.json` contains **only** the 310 algorithmically invented payments produced from `scripts/create_power_bi_demo.py`, matching the offline Power BI example: S/ 509,565.12 across 2024–2026. The script `scripts/export_workspace_demo.py` regenerates the fixture from that same implementation and asserts totals before writing. No production data is exported, fetched or versioned.

The prototype is an exploration surface, not an accounting source. No revenue, profit, opening balances, bank balances or approved budgets are implied. The interactive Draft workbench stores local state only in React while the page is mounted; closing/reloading discards these Drafts. It intentionally contains no authentication, live Dataverse client, real user permissions, tokens, or backend endpoints; it **must not be deployed for financial users** as-is.

## Fabric Apps option — *evaluated, not deployed*

Microsoft Fabric Apps (preview) can be built with the Rayfin CLI and uses delegated semantic-model access via a Fabric connector. [Official reference](https://learn.microsoft.com/en-us/fabric/apps/data-apps-template).

A real deployment is gated on:
1. Fabric capacity assigned to an accessible workspace and Fabric Apps preview enabled by a tenant admin.
2. Read/Build privileges for a **published** Financial Manager semantic model and an approved Execute Queries tenant setting.
3. A separate, authenticated Fabric App project created using the official `dataapp` template and generated connector schemas; no fabricated Rayfin project or hard-coded credentials.
4. Verification of data refresh, row visibility, security boundaries and role-specific test users.

**Architecture:** Dataverse/Power Apps remain the system of record and the authorized action channel. Fabric Apps may complement Power BI for exploration and navigation. Fabric data replication, parallel approval mutations, or user-data-function write-back are prohibited pending a separate security review and acceptance.

This UI uses a standard Vite/React stack to validate product UX first. The layout and components can inform a later migration into a *real* Fabric App, but it is not technically the same as creating a Rayfin app.

## Scope boundaries

Never wire New operation, budget approval, posting or voiding buttons directly to Dataverse REST APIs. Use existing server-validated Power Platform paths, with Entra authentication, permission tests, audit trails and transactional invariants preserved. Keep `main`, the Dataverse environment, Power BI semantic definitions, approval roles and customer data unchanged.

See [Architecture decision](../../docs/decisions/0005-exploratory-financial-workspace.md).
