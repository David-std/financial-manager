# Development guide

## Prerequisites

- Git
- Power Platform Developer environment with Dataverse
- Power Platform CLI (`pac`)
- Power Apps maker access in the development environment
- Power BI Desktop with Power BI Project (PBIP) support

Do not store a tenant URL, environment ID, user email, or authentication artifacts in the repository.

## Naming

| Resource | Convention | Example |
| --- | --- | --- |
| Repository | kebab-case | `financial-manager` |
| Solution unique name | PascalCase | `FinancialManager` |
| Publisher prefix | lowercase, consistent | `fmg` |
| Dataverse table/column | publisher-prefixed schema name | `fmg_financialtransaction` |
| Power Apps screen | descriptive prefix and PascalCase | `scrTransactionList` |
| Power Apps component | descriptive prefix and PascalCase | `cmpPageHeader` |
| Power Automate flow | action-oriented display name | `Notify on High Expense` |
| DAX measure | readable title | `Total Expenses` |
| Git branch | topic-based kebab-case | `feature/transaction-entry` |
| Git commit | Conventional Commits | `docs: define financial data model` |

Do not rename generated solution files to satisfy a separate style preference; keep the platform's canonical source format.

## Initial environment setup

1. Create a Dataverse publisher with prefix `fmg`.
2. Create an unmanaged solution with unique name `FinancialManager` using that publisher.
3. Add only real components to the solution after the functional and security contracts have been reviewed.
4. Authenticate locally using the Power Platform CLI and your development environment.
5. Clone the solution into the repository using the supported CLI command:

```powershell
pac solution clone --name FinancialManager --outputDirectory power-platform
```

If the installed CLI version or environment requires a different command sequence, consult the current Microsoft CLI reference before proceeding. Inspect generated files and dependencies before committing; a complete solution source tree must come from Dataverse rather than handwritten placeholders.

## Power BI

Save reports as Power BI Project (`*.pbip`) under `power-bi/` to preserve editable text definitions. Keep local cache and local settings out of version control. Do not commit downloaded financial data, PBIX exports, or embedded credentials.

## Working process

1. Implement one reviewed slice of the application in the development environment.
2. Export or clone the actual solution source and save PBIP changes.
3. Review diffs for secrets, unintended data, and missing dependencies.
4. Run `python -m unittest discover -s tests -v` and `python scripts/check_repository.py`.
5. Open a pull request, review solution and report changes, and merge after checks pass.

Future pipeline jobs should use supported solution validation and explicit deployment identities. Automatic production deployment is intentionally not configured at this stage.

## Microsoft references

- [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/reference/solution)
- [Solution YAML format](https://learn.microsoft.com/power-platform/alm/solution-source-control-yaml-format)
- [Power BI projects](https://learn.microsoft.com/power-bi/developer/projects/projects-overview)
