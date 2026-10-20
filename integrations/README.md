# Optional integrations

Integrations are separate from the first operational release. They may be added only after the Dataverse authorization model and transaction validation are tested.

## iOS Shortcuts

First iteration: open an authenticated Power Apps expense form using a deep link, prefill supported parameters, and require confirmation. This does not require a public ingestion endpoint.

A future direct-submission flow must authenticate the caller, verify the user's permissions, enforce the same posting rules as the app, and deduplicate requests with a unique request ID. Never distribute an anonymous write-enabled flow URL.

## Conversational reporting

Expose only approved, read-only reporting operations with typed filters. The source system calculates totals; a model may interpret the user's question but must not supply fabricated numeric answers or arbitrary Dataverse queries.

A WhatsApp channel requires a separate identity-binding and licensing review. No messaging channel or AI endpoint is deployed yet.

See [Security](../docs/security.md) and [Architecture](../docs/architecture.md).
