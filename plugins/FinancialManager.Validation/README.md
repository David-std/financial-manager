# Financial Manager — Dataverse payment integrity

Actual .NET Framework 4.8 Dataverse plug-in code; source values verified against the unmanaged FinancialManager export on 2026-10-22.

## Implementation

- Plugin: `FinancialManager.Validation.FT_EnforcePaymentIntegrity`
- Entity: `fmg_financialtransaction`
- Messages: `Create`, `Update`, `Delete`
- Execution: **synchronous PreValidation** (stage 10, mode 0)
- Update and Delete require a restricted pre-image named `Before`.
- No Update filtering attributes: changes to *any* financial field must be intercepted.

### Immutable workflow

- `Create` accepts only Draft.
- Draft updates are allowed by native Dataverse permissions.
- Draft → Posted requires complete payment data and a System Administrator.
- Posted → Voided requires a System Administrator, a non-empty reason, and no changes to the payment fields.
- Posted and Voided records cannot be deleted; Voided cannot be edited or voided twice.

### Validations

- Valid positive amount with two decimals; Date Only; PEN transaction currency; four expense Flow Class options.
- Active expense leaf category with a parent, excluding `999` and group categories.
- Scope Branch requires an active Branch; Scope Shared requires no Branch.
- Cash Account must be active; a branch-bound account cannot be used by another branch or for Shared.
- Reference lookups must match the target Dataverse table.
- Invalid status changes and missing pre-images fail closed.

**Temporary least-privilege gate:** The intended Finance Manager / Treasury Clerk / branch-team role model is **not configured** in Dataverse yet. Only the standard `System Administrator` role (verified via its Microsoft-defined RoleTemplateId) can Post or Void. This is not a permanent authorization model; ordinary staff are intentionally blocked from these operations until roles and team membership are implemented and tested.

## Tests

```powershell
dotnet test tests/FinancialManager.Validation.Tests/FinancialManager.Validation.Tests.csproj -c Release
```

Current automated suite has 32 local scenarios with no Dataverse access. These tests do not prove production permissions or actual server registration. Do not consider the plugin deployed without live tests against the correct dev environment.

## Deployment requirements

1. Verify environment name and Organization ID.
2. Build NuGet package using `dotnet build plugins/FinancialManager.Validation/FinancialManager.Validation.csproj -c Release`.
3. Register package/assembly and plug-in class in `financial-manager-dev`.
4. Register three synchronous PreValidation steps: `FT_ValidateCreate`, `FT_ValidateUpdate`, `FT_ProtectDeletion`.
5. Register `Before` pre-images on Update (status, payment fields, transactioncurrencyid, Void Reason) and Delete (status).
6. Add the package/assembly and processing steps to the **FinancialManager** solution; re-export to `develop`.
7. Verify draft create, valid post/void, invalid posting, immutable Posted, protected Delete, and non-admin denial from an independent Dataverse entry route.

Keep connection credentials, personal tenant IDs, production records, and generated binaries out of Git.

Specs: [data-model.md](../../docs/data-model.md) and [validation-strategy.md](../../docs/validation-strategy.md).
