# ADR 0001: Use Microsoft Power Platform

- Status: Accepted for initial design
- Decision date: 2026-10-20

## Context

The required product is a compact internal financial management application with category-based transaction entry and management reporting. A separate custom web stack would add deployment and maintenance work without addressing an identified requirement.

## Decision

Use Power Apps for the operational interface, Dataverse for persistent financial data and authorization, and Power BI for analytical reports. Use Power Automate only for justified workflows and authenticated integrations. Keep development and production environments separate.

## Consequences

- Dataverse provides managed storage, identity integration, role-based permissions, and auditing.
- Power BI reports can use a source-controlled PBIP structure.
- Production operation involves Microsoft licensing and tenant administration.
- Platform capabilities and connector limits must be validated before any external-channel integration.
- No custom API or infrastructure is introduced without a documented requirement.

## Alternatives considered

- SharePoint Lists: lower initial platform cost in some tenants but less suitable for relational financial data and centralized authorization.
- Custom web application: more direct code control, but duplicates platform features already available for this scope.
