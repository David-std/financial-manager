# Dataverse deployment and live integrity verification

A small .NET 8 utility for Financial Manager. The tool uses a Microsoft Entra sign-in cached on the authorized Windows machine. It **never embeds credentials, tenant identifiers, or environment URLs** in source control.

## Safety controls

- Set the target via `FM_ENV_URL` (HTTPS Dynamics environment URL) and `FM_ORGANIZATION_ID` (Dataverse Organization ID). They must match the live connection.
- The target must contain the existing `FinancialManager` solution.
- `inspect` is read-only. Every other command requires `--apply`.
- `register` adds an initial NuGet plug-in package to the solution and refuses to overwrite an existing package.
- `steps` registers the three synchronous PreValidation steps once, with restrictive pre-images. It skips any existing step of the same name.
- `verify` checks the three steps and their images, then runs six live `ExecuteTransaction` checks that are **deliberately rolled back** using a guaranteed-invalid sentinel operation.
- `verify` performs no persistent insertions when Dataverse's atomic ExecuteTransaction rollback works as designed. Tests fail if the rollback sentinel unexpectedly succeeds.

The utility uses an Entra public-client application ID (not a secret). Permissions are still enforced by Dataverse.

## Build

```powershell
dotnet build tools/DataverseDeployment/DataverseDeployment.csproj -c Release
```

## Inspect the authenticated development environment

Set `FM_ENV_URL` and `FM_ORGANIZATION_ID` **in your local session only**, using values from `pac env who`. Do not commit a local `.env` or paste credentials into Git.

```powershell
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- inspect
```

## Register initial package and steps (controlled dev only)

Build the plug-in in Release and set `FM_PACKAGE_PATH` to the resulting `fmg_FinancialManagerValidation.1.0.0.nupkg`. Review code and target before running:

```powershell
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- register --apply
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- steps --apply
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- verify --apply
```

Do **not** run `register` for updates: the current tool intentionally does not replace existing plug-in packages. To update the binary later, create a reviewed upgrade mechanism and rerun live tests.

## Verified on 2026-10-22

- Developer environment `financial-manager-dev`, `FinancialManager` solution version `0.1.0.0`.
- Package registered as `fmg_FinancialManagerValidation`.
- `FT_ValidateCreate`, `FT_ValidateUpdate`, `FT_ProtectDeletion` each active in synchronous PreValidation.
- Required `Before` pre-images exist for Update and Delete.
- 32 automated unit test cases passed.
- 6 Dataverse integration checks passed: direct Posted create rejected; Draft → Posted → Voided allowed; Posted mutation and delete rejected; group category and inactive cash account rejected.
- Integration transactions were rolled back. No persisted synthetic payment/account records were created by this verification.
- **Unverified:** real non-admin identity, branch-team access control, other tenants/environments, import of the packed solution into a separate environment, longer-running race cases.
- Pack/repack generated a **PluginAssembly root-component warning** despite producing a ZIP. This needs independent import validation before treating the Git export as a deployment-ready release artifact.

Source of truth: [Dataverse plug-in](../../plugins/FinancialManager.Validation/) and [validation strategy](../../docs/validation-strategy.md).
