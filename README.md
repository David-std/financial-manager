# Financial Manager

Financial Manager is an internal financial control application built with Microsoft Power Platform. It is intended to record income and expenses, maintain a consistent category structure, and provide management reporting without replacing accounting or billing software.

## Technology

- Power Apps: transaction entry and administration
- Microsoft Dataverse: operational data, access control, and auditing
- Power BI: financial reporting
- Power Automate: approved integrations and notifications

## Project status

**Foundation stage.** This repository contains the initial requirements, design decisions, security baseline, and repository checks. It does not yet contain a deployed application, a Dataverse solution export, or a Power BI project.

## Repository

| Path | Purpose |
| --- | --- |
| [docs](docs/) | Scope, architecture, data model, security, and acceptance criteria |
| [power-platform](power-platform/) | Source-controlled Power Platform solution, once generated from a development environment |
| [power-bi](power-bi/) | Power BI Project (PBIP), once saved from Power BI Desktop |
| [integrations](integrations/) | iOS shortcut and conversational reporting integration specifications |
| [scripts](scripts/) | Repository validation |
| [tests](tests/) | Validation tests |

## Development

See [Development guide](docs/development.md) for environment setup, naming conventions, and source-control procedures. Changes to the application must be made through an unmanaged development solution and exported or cloned into this repository; generated platform files must not be fabricated.

## Security and data

The repository must not contain business transaction data, account credentials, connection secrets, tenant identifiers, or personal information. Demonstrations should use synthetic data in a separate development environment. See [Security](docs/security.md).

## Scope

This is a management reporting tool, not a general ledger or a certified profit-and-loss accounting system. Current scope and assumptions are documented in [Project scope](docs/scope.md).
