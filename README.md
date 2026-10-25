# Financial Manager

Financial Manager is a Microsoft Power Platform project for internal cash-oriented financial control, budget allocation, and management reporting. It is designed for a small business with multiple operating locations. Customer management, electronic invoicing, and formal accounting remain outside the application.

## Technology

- Power Apps: financial movement and budget workflows
- Microsoft Dataverse: operational records, authorization, and auditing
- Power BI: financial analysis, comparisons, and budget reporting
- Experimental financial workspace: [Financial Workspace](experiments/financial-workspace/README.md), synthetic reporting plus temporary expense/budget Drafts, task queue, category explorer and session activity; no Dataverse writes
- Power Automate: approved workflow actions and optional future integrations


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
| `experiments/financial-workspace/` | Offline React UX exploration with synthetic data and in-memory Draft interactions; **not** a live Fabric or financial service |

## Working principles

Keep application source in supported Microsoft formats; do not fabricate generated files. Validate financial metrics using controlled synthetic transactions, test role permissions independently of UI visibility, and exclude actual company records, personal data, credentials, and production exports from Git.

Read [Development](docs/development.md) before building platform components.
