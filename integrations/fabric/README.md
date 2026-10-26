# Optional Microsoft Fabric integration

Financial Manager does not currently use Fabric capacity, Fabric Apps, a Fabric semantic model or Fabric authentication. Its React frontend lives in apps/financial-workspace as a standalone offline UX preview.

**Integration gate:** Verify tenant entitlement, capacity, workload availability, delegated authentication, model-level Read/Build permissions, Power BI RLS, export/security policies, approved ALM and costs before enabling any adapter. Fabric is for governed analytics; it must not become a second financial ledger or a way around Dataverse rights.

Until then, this directory deliberately contains **no credentials, tokens, data connection config or runnable Fabric write path**. If enabled, add an explicit adapter behind the financial workspace data-access contract, with isolated testable entitlements and authorization checks. No fallback to synthetic records when real auth fails.
