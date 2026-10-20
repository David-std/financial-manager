# Security baseline

Financial information is business-sensitive even if no banking credentials are stored.

## Identity and authorization

- Use Microsoft Entra ID for organizational sign-in and require MFA under the tenant policy.
- Configure Dataverse security roles rather than relying on Power Apps visibility rules.
- Use least-privilege access for application users, flow connections, and report consumers.
- Do not introduce a custom user/password table.
- Verify record access through direct Dataverse and integration paths, not only through the app.
- Do not expose an anonymous HTTP-triggered flow that creates or reads transactions.

## Proposed permissions

| Operation | Administrator | Operator | Manager |
| --- | --- | --- | --- |
| Read authorized transactions | Yes | Yes | Yes |
| Create draft | Yes | Yes | No |
| Post permitted draft | Yes | Yes, if assigned | No |
| Void posted transaction | Yes | No | No |
| Manage categories and accounts | Yes | No | No |
| Configure roles and integration identities | Yes | No | No |
| View authorized Power BI reports | Yes | As approved | Yes |

Scope of record visibility, posting rights, and separation of duties must be confirmed with stakeholders before creating roles.

## Data handling

- Enable Dataverse auditing for financial transactions, category changes, and security-relevant configuration.
- Prevent hard deletion of posted financial records through role permissions and business rules.
- Keep development data synthetic and separate from live data.
- Keep tenant identifiers, connection references with secrets, tokens, personal details, and real financial exports out of Git.
- Configure backups, retention, restore exercises, and access reviews for production.
- Apply Power Platform data policies to limit unauthorized connector combinations.
- Limit Power BI workspace permissions and use row-level security when different audiences need different views.

## External channels

A deep link retains the interactive Power Apps authentication boundary. For a future direct iOS shortcut, require an authenticated identity, token management, request validation, replay protection, and an idempotency key.

Conversational access must use explicit allowed queries, validate parameters, apply row-level authorization, limit the detail returned, and reject unsupported questions. WhatsApp numbers are not sufficient proof of identity by themselves.

## Production gate

Perform authorization, auditing, restoration, data-leakage, and report-access tests before the first real financial record is entered. This document is a policy baseline, not evidence that controls are already configured.
