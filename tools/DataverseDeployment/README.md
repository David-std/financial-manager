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


## Deploying the core spending-budget schema (2026-10-22)

The read-only `inspect-schema` checks whether the expected tables already exist. `schema --apply` **creates missing metadata only** and does not overwrite existing definitions or touch business rows. Each operation verifies the target Dataverse Organization ID and the `FinancialManager` solution first.

```powershell
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- inspect-schema
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- schema --apply
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- publish-schema --apply
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- verify-schema
```

The commands require the same `FM_ENV_URL` and `FM_ORGANIZATION_ID` environment variables as the plug-in verification (set locally, never committed).

### Actual Dataverse schema introduced

- `fmg_budget`: Name, Year, Scope (Branch/Shared), Branch, Version, Effective From Month, Status (Draft/Submitted/Approved/Rejected/Superseded), Approved By and Approved On.
- `fmg_budgetallocation`: Name, Budget, Month (1–12), Category, Amount (PEN, 2 decimals, nonnegative).
- `fmg_activityevent`: Name, Event Type, Record Type, Record ID, Summary, Actor and Event Time.
- Five lookup relationships: `fmg_BG_Branch`, `fmg_BG_Approver`, `fmg_BA_Budget`, `fmg_BA_Category`, `fmg_AE_Actor`.
- All three tables use Dataverse User/Team ownership and have table-level auditing enabled.
- Budget Scope choice values: `Branch=797280300`, `Shared=797280301`.
- Budget Status values: `Draft=797280400`, `Submitted=797280401`, `Approved=797280402`, `Rejected=797280403`, `Superseded=797280404`.
- Activity Event Type values `797280500`–`797280505`, in the order `TransactionCreated`, `TransactionPosted`, `TransactionVoided`, `BudgetSubmitted`, `BudgetApproved`, `CategoryDeactivated`.

On 2026-10-22, `verify-schema` verified **21 expected columns, five related Lookups and 13 Choice values** against published Dataverse metadata. A fresh solution export included all seven Financial Manager custom tables. A local ZIP repack succeeded, but the pre-existing PluginAssembly root-component warning persists; **cross-environment import remains unverified**.

**IMPORTANT:** These three tables are *structural only*. The budget approval lifecycle, duplicate monthly allocation protection, authorization inheritance, immutable approved versions, and curated Activity Event write authorization are **not yet implemented**. Do not populate financial approvals or interpret an `Approved` option as trusted until synchronous server-side controls and tests are in place. The second-identity authorization tests remain deferred by project decision.


## Budget integrity package and enforcement

The independently packaged `fmg_BudgetIntegrity` plug-in enforces the budget and allocation lifecycle in the **same** `financial-manager-dev` environment; it does not modify or replace the payment plug-in. The `fmg_BA_UniqueMonthlyCategory` Dataverse alternate key must report `Active` before registration.

Commands (set `FM_ENV_URL` and `FM_ORGANIZATION_ID` in the **local** shell first):

```powershell
dotnet test tests/FinancialManager.BudgetIntegrity.Tests/FinancialManager.BudgetIntegrity.Tests.csproj -c Release
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- budget-inspect
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- budget-key-status
# Initial creation only: budget-key --apply; budget-register --apply; budget-steps --apply
# Controlled same-version update only (FM_BUDGET_PACKAGE_PATH required): budget-upgrade --apply
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- budget-verify --apply
```

The initial NuGet package registration is `fmg_BudgetIntegrity` version `1.0.0`. Dataverse rejects attempts to update an already registered `PluginPackage.version` from 1.0.0 to 1.0.1. The approved update mechanism replaces **only the package contents**, preserving registered package identity and version, then verifies the three classes and nine active steps. Do not delete/recreate the package: that can remove dependent processing steps.

Budget approvals intentionally remain **disabled**; ordinary direct writes to Activity Event are blocked. A passing `budget-verify` run means the nine rollback-only development integration tests succeeded. **Live authorization for non-admin identities and version-based approvals remain unverified.**

## Model-driven app navigation labels

The existing `Financial Manager` app sitemap uses a **single es-ES (LCID 3082)** environment and must not contain `SitemapDesigner.NewArea/NewGroup/NewSubArea` placeholder resources. The `app-nav-inspect` command reads published and unpublished records independently, validates the one Area, one Group and four original page IDs, and reports localization status without writing anything.

```powershell
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- app-nav-inspect
# Run only against the verified dev environment when correction is needed:
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- app-nav-fix --apply
```

The guarded fix takes a local TEMP backup before changing an unpublished sitemap, keeps existing Area/Group/SubArea and generated page IDs, writes Spanish titles (3082), and verifies unpublished readback. **It publishes the existing app module using `PublishXmlRequest` `<appmodules>`**, not just the `<sitemaps>` element; then verifies the published sitemap. A sitemap-only publish previously returned success but did not make this app's changes effective. Never copy stale, pre-publish sitemap XML back to the repository.

After an actual correction, run a fresh unmanaged solution export, unpack into TEMP, and update **only** `AppModuleSiteMaps/financial_manager_bee9f134/AppModuleSiteMap.xml` in source control. Validate all four page IDs; no new pages or full solution import are required. The running browser view is a separate final-acceptance check.

## Budget approval infrastructure (development only)

The read-only command `budget-approval-inspect` reports whether the explicit approver role, direct/team assignments, Claim table, ScopeKey and ACTIVE unique index exist, and whether the bound Custom API has been registered. The preliminary table and unassigned role were added to `financial-manager-dev` through individually guarded, idempotent `budget-approval-schema --apply` and `budget-approval-role --apply` commands. The latter **does not** assign users or remove Dataverse's nine built-in default role privileges; review those privileges before any assignment.

```powershell
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- budget-approval-inspect
# Development-only and only after independent authorization and review:
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- budget-approval-schema --apply
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- budget-approval-role --apply
```

All commands still require `FM_ENV_URL` and `FM_ORGANIZATION_ID`, and abort if the organization or solution do not match. `budget-approval-inspect` is read-only. The approval Custom API is **not** registered; `BGV009` is still active in the deployed plug-in. See [ADR 0004](../../docs/decisions/0004-safe-budget-approval.md).

## Claim guard upgrade safety

`BudgetApprovalProvisioning` created the Claim table/lookup and Active alternate key, and an unassigned finance approver role. The exported role has nine Dataverse platform default privileges and must not be assigned before role security review.

`BudgetDeployment` contains unregistered plans for `BAC_BlockClaimCreate`, `BAC_BlockClaimUpdate` and `BAC_BlockClaimDelete`; no steps exist yet in Dataverse. The `budget-upgrade --apply` mode now requires a recoverable backup of the currently deployed NuGet plug-in package. Its attempt to retrieve `pluginpackage.content` returned an empty value and safely aborted **without** updating the package. This is an intentional fail-closed gate. Microsoft documents plugin package contents as file storage, and supports updates through the Plug-in Registration Tool or `pac plugin push` with an existing package ID. Do not bypass this backup requirement merely to publish the new type.

See [Microsoft plug-in package management](https://learn.microsoft.com/en-us/power-platform/developer/howto/cli-create-package) and [ADR 0004](../../docs/decisions/0004-safe-budget-approval.md).
