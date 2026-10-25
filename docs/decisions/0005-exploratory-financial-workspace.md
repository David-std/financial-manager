# ADR 0005 — Read-only Financial Workspace prototype and Fabric Apps assessment

Date: 2026-10-25
Status: Experimental — decision to prototype; no production migration

## Context

Financial Manager already has a model-driven Power Apps operational interface, protected Dataverse business rules and an executive-v9 Power BI report with a mobile layout. The product owner requested a friendlier, more expressive interface resembling high-quality workspace products rather than reproducing a dense dashboard. Financial operations must retain independent server authorization and immutable audit trails.

Microsoft Fabric Apps (preview) can produce code-driven data apps with authentication and semantic-model connectivity. However, its deployment requires Fabric capacity, an enabled workload, suitable workspace permissions, model Read/Build permission and an Execute Queries tenant configuration. The existing Git-tracked PBIP is not evidence that a published Fabric semantic model or app capacity is available.

## Decision

- Build an isolated Vite + React prototype under `experiments/financial-workspace/`, read-only and local. Use the *same deterministic synthetic* expense dataset as Power BI.
- Provide a session-only work queue, budget Draft allocation editor, category explorer and activity feed. Local events are real user interactions within the session, **not** authoritative audit records. The top bar is the single visible environment marker, avoiding repeated prototype labels on finance controls.
- Focus on daily user orientation: period navigation, personalizable filters, readable expense summaries, recent activity, contextual record inspection, and adaptive layout. Keep the dashboard's deep analytical job in Power BI.
- Treat the prototype as an **alternative UI/UX reference** and possible future frontend, not as a new finance backend or a real Fabric Apps deployment.
- In-memory Draft capture, modification and discard are permitted solely to demonstrate UX. Drafts survive navigation while the session state is mounted and reset on full reload. They have session-only references, and the environment-wide notice and About panel explicitly say no Dataverse write occurs. This is not a server authorization test.
- Keep sensitive operations disabled and visibly contextualized. Show budget readiness/explanation without fabricating budget figures or approvals.
- No second SQL database, authentication store, ownership model, financial ledger or write-back path. No auto-approval.
- Consider migrating the frontend patterns to a genuine Fabric Apps `dataapp` template only after workspace capacity, tenant approval, semantic model publication, access policies and licensing are validated.
- Continue to test Power Apps server actions under separate identities irrespective of the frontend's appearance.

## Consequences

- Product UX can evolve without replacing the secured business workflow.
- The demo can run freely and be reviewed on desktop and mobile, but its output is **not production-ready**.
- A live Fabric deployment is **not claimed** and requires a separate integration/release gate.
- A production frontend must use delegated identity and existing Dataverse authorization and audit checks; read-only Fabric semantic queries must not bypass model or row security.
- Any later user-data-function financial write-back requires security review, transactional tests and non-admin user acceptance before it is implemented.

## Sources

- https://learn.microsoft.com/en-us/fabric/apps/create-app
- https://learn.microsoft.com/en-us/fabric/apps/data-apps-template
- https://learn.microsoft.com/en-us/fabric/apps/pricing
- https://learn.microsoft.com/en-us/power-bi/create-reports/fabric-apps-analytics
