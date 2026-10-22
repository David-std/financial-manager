# Validation and workflow enforcement

## Principle

Power Apps validates for user experience. Dataverse must independently enforce data integrity and permissions for every entry route, including direct Dataverse access and approved cloud flows.

## Mechanisms

| Requirement | Preferred mechanism | Additional note |
| --- | --- | --- |
| Nonempty required fields, field types, positive values | Dataverse columns and table-scoped business rules | Confirm where a rule executes, particularly in Canvas Apps |
| Code uniqueness and external request deduplication | Dataverse alternate keys where compatible | Test duplicate concurrent submissions, not only sequential saves |
| Basic role and branch access | Dataverse security roles and tested owner/team privileges | UI conditions do not authorize access |
| Active category, leaf status, and expense-payment class validation across lookups | Server-side validation when posting | Cross-record invariants are not assured by a dropdown filter |
| Posted transactions cannot be modified | Security permissions and synchronous server-side guards | A status-based rule must withstand direct table updates |
| Budget approval and immutable approved versions | Privileged operation, enforced transition rules and auditing | Do not trust a client-supplied approval flag |
| Approved-versus-actual reporting | Validated DAX measures and controlled fixtures | Power BI is read-only; never use report colors as authorization |
| Alerts and optional notifications | Power Automate after successful write | Asynchronous flow is not the only posting integrity control |

Dataverse table-level business rules can validate common conditions without custom code. Complex cross-table and state-transition enforcement may require a **small synchronous Dataverse plug-in** or a secured Dataverse Custom API implemented within the same Power Platform solution. This is not a separate backend or an arbitrary public HTTP endpoint.

Do not assume Power Automate (asynchronous processing) prevents an invalid row from being committed. For monetary, ownership, and approval transitions, blocking invalid writes before completion is preferable. The exact mechanism must be selected after testing Microsoft-supported capabilities in the development environment.

## Transaction commands

1. `SaveDraft`: authenticate; validate types, role, and draft ownership.
2. `PostTransaction`: verify caller privileges; verify amount and currency; confirm active leaf category, permitted flow classification, valid branch/team, internal account, and permitted date. Commit movement and event trace in a consistent operation.
3. `VoidTransaction`: verify caller privileges and required reason; reject repeated voids and mutation of amount/date/category; preserve audit history.
4. `SubmitBudget`: check annual/monthly allocation validity, unique categories, and role.
5. `ApproveBudget`: check financial authority, workflow state, authorized scope, version effective period, and approval separation; forbid edits to approved lines.

Operations should be idempotent where retried through external clients. Avoid storing event history solely in user-generated activity rows: native Dataverse auditing remains the authoritative technical audit.

## High-value edge cases

- Simultaneous approvals for the same year/location cannot produce conflicting effective budget versions.
- Two identical shortcut requests cannot create duplicate posted transactions.
- A category cannot be deactivated between a form selection and a posting without revalidation.
- A user moved out of a branch team cannot continue posting to that branch from a stale application session.
- A posted item cannot be altered by direct Dataverse update even if Canvas controls are hidden.
- An over-budget warning is not a hard spend limit unless a concurrency-safe approval/reservation design is explicitly implemented.
- A service-account flow must attribute the authorized initiating user and not bypass the user's business privileges.

## Release gate

A business capability is not complete until negative cases are executed through the application **and** at least one independent data-access route. Do not report an integration as secure based on UI behavior alone.

Microsoft references:
- [Dataverse business rules](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-create-business-rule)
- [Dataverse Custom APIs](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api)
- [Dataverse auditing](https://learn.microsoft.com/en-us/power-platform/admin/manage-dataverse-auditing)

See [Verification](verification.md), [Security](security.md), and [Data model](data-model.md).
