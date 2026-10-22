# Financial Manager — Dataverse validation plug-in

This folder contains the official Power Platform CLI **.NET Framework 4.8** plug-in project scaffold (`pac plugin init --skip-signing`).

**No business plug-in class or processing step is registered yet.** A successful build verifies the project tooling only; it does **not** enforce transaction integrity.

## Validation contract

Implement `FT_EnforcePaymentIntegrity` for Dataverse table `fmg_financialtransaction`, using synchronous server-side steps after confirming the exported solution metadata and permissions:

| Step name | Message | Stage | Purpose |
| --- | --- | --- | --- |
| `FT_ValidateCreate` | Create | PreValidation | Accept Draft creation, reject invalid direct postings |
| `FT_ValidateUpdate` | Update | PreValidation | Validate Draft → Posted and authorized Posted → Voided transitions, reject edits to confirmed financial fields |
| `FT_ProtectDeletion` | Delete | PreValidation | Reject deletion of Posted or Voided payments |

Use a **restricted Pre Image** on Update/Delete to evaluate existing Status, owner, date, amount, category, scope, branch, cash account, and void reason. Do not register steps against unrelated tables.

## Before implementation

- Export the actual `FinancialManager` solution and confirm all Dataverse logical names and **numeric Choice option values**; never assume the generated option IDs.
- Confirm user/team ownership, table privileges, branch-team authorization, and handling of shared accounts.
- Validate category `IsActive`, `IsGroup`, and restriction for code `999`.
- Require positive two-decimal PEN amount, Date Only payment date, valid class, compatible Scope/Branch, and active authorized Cash Account.
- Restrict state transitions and historical mutations for all clients, including direct Web API calls.
- Use `IPluginExecutionContext.InitiatingUserId` and service calls under appropriate user privileges; do not trust a client-supplied actor or bypass privileges with a service-account flow.
- Add automated unit tests and negative-case tests before registration. Register and test only against the intended development environment.
- Follow Microsoft registration guidance for pre-images and synchronous execution.

## Local build

```powershell
dotnet build plugins/FinancialManager.Validation/FinancialManager.Validation.csproj --configuration Debug
```

Generated `bin/`, `obj/`, and `.vs/` outputs are excluded from source control. Do not commit credentials, environment-specific endpoints, or exported business records.

Related specification: [docs/data-model.md](../../docs/data-model.md) and [docs/validation-strategy.md](../../docs/validation-strategy.md).
