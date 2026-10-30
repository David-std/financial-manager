# Optional AI integration

Financial Manager does not currently use an AI provider, AI agent, or language model in its financial workflows. This folder documents a possible future integration, similar to the optional Microsoft Fabric integration.

An AI integration could help explain financial reports, summarize approved business data, and assist with navigation. It must respect Dataverse permissions, Power BI row-level security, and tenant policies.

**Integration gate:** Review the provider, licensing, costs, data residency, privacy, consent, security, and human approval requirements before enabling any feature. AI must not create, approve, post, void, or delete financial records autonomously.

No API keys, model credentials, customer data, or executable AI integration are stored here. Dataverse remains the source of truth.
