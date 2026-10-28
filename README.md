# Financial Manager

Financial Manager is a Microsoft Power Platform project for internal cash-oriented financial control, budget allocation, and management reporting. It is designed for a small business with multiple operating locations. Customer management, electronic invoicing, and formal accounting remain outside the application.

## Technology

- Power Apps: financial movement and budget workflows
- Microsoft Dataverse: operational records, authorization, and auditing
- Power BI: financial analysis, comparisons, and budget reporting
- Financial workspace (React): [Financial Workspace](apps/financial-workspace/README.md), with separate synthetic DEMO and delegated Dataverse LIVE modes; LIVE Draft operations and protected actions are subject to server authorization
- Power Automate: approved workflow actions and optional future integrations


## Preview

<table>
  <tr>
    <th width="50%" align="center">React</th>
    <th width="50%" align="center">Power BI</th>
  </tr>
  <tr>
    <td align="center"><strong>Inicio</strong><p><img src="docs/images/react-workspace.png" alt="Inicio de React" width="100%"></p></td>
    <td align="center"><strong>Resumen de gastos</strong><p><img src="docs/images/power-bi-report.png" alt="Resumen de gastos en Power BI" width="100%"></p></td>
  </tr>
  <tr>
    <td align="center"><strong>Categorías</strong><p><img src="docs/images/react-categories.png" alt="Categorías en React" width="100%"></p></td>
    <td align="center"><strong>Análisis de categorías</strong><p><img src="docs/images/power-bi-categories.png" alt="Categorías de gasto en Power BI" width="100%"></p></td>
  </tr>
  <tr>
    <td align="center"><strong>Análisis</strong><p><img src="docs/images/react-analysis.png" alt="Análisis en React" width="100%"></p></td>
    <td align="center"><strong>Tendencias</strong><p><img src="docs/images/power-bi-trends.png" alt="Tendencias en Power BI" width="100%"></p></td>
  </tr>
</table>

<table>
  <tr><th align="center">Power Apps</th></tr>
  <tr>
    <td align="center"><strong>Gestión de gastos</strong><p><img src="docs/images/power-apps-gastos.png" alt="Gestión de gastos en Power Apps" width="78%"></p></td>
  </tr>
</table>

<sub>Capturas del proyecto: React en modo DEMO con datos sintéticos; Power BI en Desktop; Power Apps en entorno DEV. </sub>

## Documentation

| Topic | Document |
| --- | --- |
| Scope and exclusions | [Scope](docs/scope.md) |
| Components and data flow | [Architecture](docs/architecture.md) |
| Tables and integrity rules | [Data model](docs/data-model.md) |
| Validation implementation options | [Validation strategy](docs/validation-strategy.md) |
| Internal expense codes | [Category catalog](docs/category-catalog.md) |
| Indicators and Power BI report pages | [Reporting](docs/reporting.md) |
| Power Apps navigation and interactions | [Screens](docs/screens.md) |
| Roles and audit requirements | [Security](docs/security.md) |
| Acceptance and reconciliation tests | [Verification](docs/verification.md) |
| Environment and repository workflow | [Development](docs/development.md) |
| Architecture decisions | [ADRs](docs/decisions/) |

## Repository

| Path | Purpose |
| --- | --- |
| `docs/` | Product and technical specifications |
| `power-platform/` | Real solution source after exporting from Dataverse |
| `power-bi/` | Real PBIP source after saving in Power BI Desktop |
| `integrations/` | Optional iOS and conversational reporting specifications |
| `scripts/` | Repository checks |
| `tests/` | Automated repository validation |
| `apps/financial-workspace/` | Responsive React workspace with isolated DEMO data and delegated Dataverse LIVE operations; not a Fabric app |

## Working principles

Keep application source in supported Microsoft formats; do not fabricate generated files. Validate financial metrics using controlled synthetic transactions, test role permissions independently of UI visibility, and exclude actual company records, personal data, credentials, and production exports from Git.

Read [Development](docs/development.md) before building platform components.
