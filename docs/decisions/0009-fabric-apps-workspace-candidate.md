# ADR 0009 — Fabric Apps as a candidate analytical frontend, not the cash ledger

Date: 2026-10-26
Status: **Proposed evaluation only; no Fabric capacity, app or Dataverse link provisioned**

## What changed in the platform

Microsoft Fabric includes Power BI as its reporting workload, and now also offers **Fabric Apps (Preview)** powered by the **Rayfin** TypeScript SDK and CLI. A Fabric App can host custom frontend assets with managed Entra SSO, an app database, generated GraphQL APIs, item-level permissions and optional Fabric data connectors. Microsoft's analytical **data app template** can create custom web analytics interfaces against a Power BI semantic model. Fabric Apps is not merely a new Power BI visual and is not equivalent to Power Apps.

This preview is a genuine candidate for Financial Manager's **analytical** React workspace. It is **not** accepted as a general replacement for the Dataverse financial system or the operational Power Apps application: Microsoft's preview documentation cautions about complex multistep transactions and stored procedures.

## Retain source-of-truth boundaries

- Dataverse stays authoritative for receipt settlement, transfers, postings, opening balances, bank evidence, budget approvals, actor entitlements, audit and reversal.
- The deployed `fmg_CashIntegrity` package must keep all 18 synchronous `F5G001` Create/Update/Delete guards until reviewed server-side Custom APIs with real permissions and transaction rollback exist. Fabric Apps/GraphQL/generated SQL can NEVER directly become a second mutable ledger.
- Keep the versioned PBIP/Power BI semantic model as the analytical contract. The existing React workspace remains a useful portable implementation and UX baseline. Use a synthetic model to evaluate whether Fabric Apps could host a tailored read-only financial analysis surface.
- A future approved operation button in an analytics app must use a trusted, delegated Dataverse transaction API; do not treat Fabric SSO, an app role, a report button, or a user data function as sufficient authorization to move money.

## Comparison for this project

| Decision axis | Power Apps | React standalone | Fabric Apps (Preview) |
| --- | --- | --- | --- |
| Most appropriate | Operational forms with Dataverse guards | Bespoke UX and multiple approved transports | Managed analytical apps connected to Fabric data |
| UI control | Canvas high; model-driven more templated | High | High, full web frontend |
| Authentication | Entra/Power Platform, subject to licensing | Must be explicitly implemented/tested | Fabric Entra SSO and item permission layer |
| Backend | Dataverse, Power Fx and approved Custom APIs | Existing Dataverse APIs through delegated adapter | Managed Fabric app services, GraphQL, SQL and data connectors |
| Transfer/ledger write authority | Only via Dataverse secure server API | Only via Dataverse secure server API | **Not approved** for complex financial transaction coordination |
| Dependency | Power Apps premium user rights | Hosting/auth/operations cost | Supported region, Fabric capacity, tenant preview opt-in and workspace permissions |

## Trial gates; no automatic provisioning

1. Check eligible tenant, supported region, Fabric capacity (or permitted trial) and administrator opt-in to **Fabric Apps (Preview)**. Do not buy capacity, activate a trial or adjust tenant security without approval.
2. Check the analytical model's Build/Read rights, API Execute Queries setting, workspace item rights, RLS, metadata freshness and segregation across business branches. Fabric item sharing does not automatically prove branch-level data isolation.
3. Use an explicitly synthetic, non-financial-data prototype: one summary/analysis view from the existing Financial Workspace. Compare usability at 390/768/1440, accessibility, rendering, identity propagation and browser console, and compare with React/Power BI already working.
4. Validate capacity consumption and estimated recurring cost using the actual tenant region and workload. Do not equate Fabric Free, Power BI Pro, PPU and Fabric F capacity.
5. Keep Power Apps/React/Power BI baseline until the preview-specific operational dependencies, ALM, monitoring, supportability, tenant policy and costs have been accepted. No automatic migration or duplicate tables.

## Licensing assumptions to verify with actual tenant

Power Apps model-driven apps require qualifying Premium use rights; Power BI Pro/PPU governs report publishing/consumption according to workspace SKU; non-Power BI Fabric workloads need F capacity or Trial. F64+ can permit Free viewers under appropriate Power BI workspace access, but report authors still require qualifying author licenses. Fabric Apps uses capacity and is a preview; its deployment requires workspace access and tenant administrator enablement. Prices vary by region and contract.

## Official sources (reviewed 2026-10-26)

- [Fabric Apps overview](https://learn.microsoft.com/en-us/fabric/apps/overview)
- [Create your first Fabric App](https://learn.microsoft.com/en-us/fabric/apps/create-app)
- [Fabric data app template](https://learn.microsoft.com/en-us/fabric/apps/data-apps-template)
- [Power BI data in Fabric Apps](https://learn.microsoft.com/en-us/power-bi/create-reports/fabric-apps-analytics)
- [Fabric Apps FAQ](https://learn.microsoft.com/en-us/fabric/apps/faq)
- [Link Dataverse to Fabric](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/azure-synapse-link-view-in-fabric)
- [Fabric licensing](https://learn.microsoft.com/en-us/fabric/enterprise/licenses)
- [Power Apps app types](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/model-driven-app-overview)

**Acceptance:** research and proposal only. No data was exported, no live account or report was linked, and no entitlement was granted.
