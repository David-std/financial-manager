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

## Supported NuGet backup and package update (2026-10-23)

The previous read of `pluginpackage.content` cannot recover deployed package bytes: the binary lives in the **`pluginpackage.package` file column**. `budget-backup` now downloads exact bytes using `InitializeFileBlocksDownload`/`DownloadBlock`, verifies a NuGet manifest and size, writes a local TEMP backup, and prints the SHA-256 digest. The local backup contains deployed code and must remain outside the public repository.

```powershell
# Read-only, after setting FM_ENV_URL and FM_ORGANIZATION_ID locally:
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- budget-backup
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- budget-inspect
# A package upgrade requires the verified backup, reviewed package ID and intended dev environment:
# pac plugin push --pluginId <existing-package-id> --pluginFile <reviewed-release-nupkg> --environment <verified-dev-url>
```

A first PAC push unintentionally used an old cached NuGet; it reported success but **did not register the new classes**. A subsequent clean output directory generated a distinct checked assembly and the second PAC push successfully registered both new plug-in types while retaining identity/version and previous guard steps. Run `python scripts/check_budget_plugin_package.py <newly-generated-nupkg>` before pushing; a stale NuGet intentionally fails. Do not confuse a successful PAC exit code with proof that every expected class or processing step is registered. **The BAC Create/Update/Delete steps are NOT yet registered**: an attempted follow-up write was blocked by remote execution controls. Accordingly no live Claim guard assurance or approval acceptance exists. Do not register or expose the bound API until the three Claim guards are active and tested, a least-privilege independent approver exists, and actual concurrency is verified. `budget-upgrade` deliberately fails closed to prevent the obsolete binary update path.

## Approval readiness is not role assignment (2026-10-23)

`budget-approval-inspect` is read-only and now reports `APPROVAL_INFRASTRUCTURE_READY`, `APPROVER_ROLE_EXACT_MINIMUM_PRIVILEGES`, `APPROVAL_ROLE_ASSIGNMENT_PRESENT`, `APPROVAL_TWO_IDENTITY_ACCEPTED`, `APPROVAL_CONCURRENCY_ACCEPTED`, and `APPROVAL_READY` separately. It verifies the bound API is **private** as well as restricted. The two acceptance flags deliberately default to `False` until authenticated and documented independent-user and simultaneous conflicting-approval tests succeed. Assigning a role alone must never make the diagnostic report that budget approval is safe to activate. Do not edit the flags without recording the evidence. The role was independently verified with exactly nine intended privileges and no SharePoint grants on 2026-10-24, but it remains unassigned and approval is still unavailable pending two-identity and concurrency acceptance.

## Bound approval API and package build trap (2026-10-23)

The three `BAC_BlockClaim*` synchronous PreValidation steps are now registered. A private, budget-bound `fmg_ApproveBudget` API is registered with main-operation `BG_ApproveBudget`, requiring `prvCreatefmg_BudgetApprovalClaim` and explicit independent role membership. A negative call with a nonexistent budget and no role returned `BAP003` after the final plug-in deployment.

**Never assume `dotnet pack` contains the latest compiled code.** The official Power Apps MSBuild plug-in pack target uses `bin/Release/net48/publish`; `dotnet publish` without `-p:PublishOnBuild=false` hit a circular MSBuild target. The proven sequence is:
```powershell
dotnet publish plugins/FinancialManager.BudgetIntegrity/FinancialManager.BudgetIntegrity.csproj -c Release -p:PublishOnBuild=false
dotnet pack plugins/FinancialManager.BudgetIntegrity/FinancialManager.BudgetIntegrity.csproj -c Release -p:PublishOnBuild=false -p:PackageOutputPath=<fresh-temporary-output-directory>
python scripts/check_budget_plugin_package.py <fresh-temporary-output-directory>/fmg_BudgetIntegrity.1.0.0.nupkg
```
After the downloaded baseline is checksum-verified, upload **that validated NuGet** with `pac plugin push --pluginId <existing-id> --pluginFile <validated-file> --environment <verified-dev-url>`. The artifact validator verifies both required plug-in classes and two approval-security behavior strings. A successful `pac` exit code without a binary-content check is not acceptance.

The separate `Financial Manager Budget Approver` role has **zero assignments**. The previous unsuccessful cleanup attempt is historical: on 2026-10-24, a newly guarded `budget-approval-role-cleanup --apply` verified the role was unassigned, backed up all 13 current privileges to local TEMP, and removed **only** the four SharePoint defaults with individual post-write checks. A separate `budget-approval-inspect` readback confirmed nine intended privileges (all global), zero SharePoint privileges and no assignments. A fresh authenticated PAC export/unpack of the unmanaged solution supplied the exact exported role XML now versioned in Git. The operation refuses unexpected privilege sets or assignments; do not use it again on an already cleaned role. Do not assign this role or enable budget approvals until the second account's licensing/access, independent identities, authentic transactional approval, and concurrent conflict acceptance are demonstrated. `budget-approval-inspect` and `budget-approval-probe --apply` remain read-only and negative-only diagnostics respectively.


## Read-only expense concurrency and audit readiness

Run dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- expense-readiness using the same local, organization-bound FM_ENV_URL and FM_ORGANIZATION_ID environment values as the existing verifier. The command does **not** change data. It inspects expense-table optimistic concurrency, auditing, ownership, required columns and the OData entity-set name.

The existing rollback-only verify --apply now additionally covers the real business rule for both directions of Branch/Shared transition and negative cases. It uses a fresh synthetic row inside each aborted ExecuteTransaction and requires an active shared account fixture. No verified run is claimed until the command actually completes against the intended dev organization.

An HTTP If-Match optimistic-concurrency check in an authenticated session is still required before enabling the React adapter. The generated Power Apps page still has a best-effort stale form pre-read; it does not itself perform a conditional write. Neither test changes the separate approver role or enables budget approvals.


## F5 Dataverse schema and transactional rollback (cash domains)

The idempotent deployer requires the usual organization-bound environment variables and the existing FinancialManager solution.

After the environment has no running solution import:
1. cash-schema-inspect (read-only): report which six cash tables are missing.
2. cash-schema --apply: create receipt, transfer, posting, opening, statement, reconciliation, their fields and nine restrictive lookup relationships, user-owned and audited.
3. cash-schema-publish --apply: publish the six F5 tables.
4. cash-schema-verify (read-only): verify published entity metadata, auditing, ownership, concurrency and whether there are records.
5. cash-keys --apply: create unique idempotency keys and one opening per account/date. cash-keys-inspect must show Active before use.
6. cash-transaction-probe --apply (TEST ONLY): attempt a synthetic draft transfer and two balanced postings within ExecuteTransaction ending in a failing rollback sentinel. It MUST leave no persisted record.

**Not production-ready:** schema provisioning itself does not register the cash guard. The separate `fmg_CashIntegrity` deny-all guard is now deployed; however, final state transitions, server Custom APIs and approved roles are still absent. Do not enable F5 screens, real transfers/receipts, or balances before those controls have been deployed and accepted. Schema alone cannot prove permission and transaction integrity.

CustomizationLockException 0x80071151 means another solution Import is blocking EntityCustomization. Do not cancel/bypass the import; inspect Solution History, then rerun idempotent inspect/apply/verify once the lock clears.


**Key provisioning compatibility:** Dataverse rejects `CreateEntityKeyRequest` without an `EntityKeyMetadata.DisplayName` (0x80040203). The current idempotent provisioning supplies a locale-specific label; a previous rejected request did not create a key. Always inspect all six key index states and require **Active** before executing rollback probes or enabling financial operations.

**F5 development acceptance boundary:** On the verified development organization, the six tables were published and inspected with zero rows, all six key indexes reached Active, and the rollback sentinel test confirmed zero persisted cash transfer/posting rows. The isolated deny-all cash guard plug-in has since been registered with 18 active steps and six connected `F5G001` Create denials. No authorized cash operation API, least-privilege business role or complete production approval has been registered.

## F5 fail-closed server write protection

The independent `fmg_CashIntegrity` package contains `FinancialManager.CashIntegrity.F5_BlockDirectCashWrites`. It denies direct Create, Update and Delete on all six F5 cash tables, including administrators, until separately reviewed authorized transaction APIs are available.

To prepare it, build and pack `plugins/FinancialManager.CashIntegrity/FinancialManager.CashIntegrity.csproj` to a **new empty temporary directory**, then run `python scripts/check_cash_plugin_package.py <fresh-nupkg>`. The validated package must be supplied via `FM_CASH_PACKAGE_PATH`, in addition to the existing organization-bound environment variables. First registration only: `cash-guard-register --apply`, which refuses to overwrite any existing CashIntegrity package.

Next run `cash-guard-inspect` (read-only), then `cash-guard-steps --apply`. The idempotent step provisioner registers 18 synchronous PreValidation steps, three operations for each F5 table, without any client-controlled bypass. Reinspect to require **18/18 active steps**, then run `cash-guard-probe --apply` to verify six direct Creates fail with `F5G001`. This test does not create successful cash records.

The historical `cash-transaction-probe --apply` ran successfully **before** these deny-all steps. Once the lock is deployed, it is expected to fail at its first Create instead of reaching the final rollback sentinel; do not remove or disable the cash guard to repeat that earlier test. New approved APIs will need their own isolated atomic rollback and negative authorization probes.

**No F5 operational APIs, roles, receipt settlement, bank balances or production transfers have been enabled by these controls.**


## F5 P1 read-only contract inspection

Run `cash-contract-inspect` **without** `--apply` after verifying `FM_ENV_URL` and `FM_ORGANIZATION_ID`. The command inventories actual column types, supported option numeric values, metadata relationships, owner/audit/concurrency flags and the presence or absence of the five proposed Custom APIs. It reads **no account rows or financial data**, and records no tenant IDs or credentials.

```powershell
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- cash-contract-inspect
```

The connected 2026-10-26 inspection found `fmg_cashaccount` and `fmg_branch` are **OrganizationOwned**, not user/team-owned, and both have native auditing disabled. The account has `fmg_isactive`, optional `fmg_branch` lookup and a separate account-type choice; the branch has `fmg_isactive`. This means account owner cannot be used as proof of branch entitlements. A missing branch cannot be treated as a shared account without explicit, server-approved account grants. The six F5 tables remain user-owned and audited; none of the five proposed Custom APIs exists yet.

The source-only `src/FinancialManager.CashDomain/CashAccountScopePolicy.cs` rejects inactive/foreign accounts, missing branch evidence, implied shared accounts and unpermitted operations. The **future** Dataverse adapter must securely load all entitlement inputs from validated server context and authorized reference data, never from client claims. In addition to accepting independent roles and transaction rollback, review reference-table auditing and changes to account/branch membership before operational money is enabled. See [ADR 0008](../../docs/decisions/0008-secure-cash-operation-api.md).


## F5 server transaction runtime verification (read-only diagnostic)

Before implementing server-side cash settlement or two-leg transfers, inspect the Dataverse Custom API MainOperation transaction flag instead of assuming it. `plugins/FinancialManager.CashOperations` provides an isolated **read-only** `F5_RuntimeProbe`. It has no financial service calls and always returns `CashWritesEnabled=false`. It is NOT an operational cash API.

Safeguards: the usual validated `FM_ENV_URL` and `FM_ORGANIZATION_ID` values, one `FinancialManager` solution, all 18 active `fmg_CashIntegrity` deny-all guard steps, and an exact existing `prvReadfmg_CashTransfer` privilege. The private, global action `fmg_CashTransactionReadiness` allows no custom processing extensions, uses only the read privilege, and reports `InTransaction`, `ObservedStage`, `SameIdentity`, and `CashWritesEnabled`. Privacy does not replace privilege authorization.

```powershell
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- cash-runtime-inspect
# Build to a fresh TEMP directory; verify the newly generated .nupkg:
dotnet publish plugins/FinancialManager.CashOperations/FinancialManager.CashOperations.csproj -c Release -p:PublishOnBuild=false
dotnet pack plugins/FinancialManager.CashOperations/FinancialManager.CashOperations.csproj -c Release -p:PublishOnBuild=false -p:PackageOutputPath=<fresh-temp-directory>
python scripts/check_cash_runtime_package.py <fresh-temp-directory>/fmg_CashOperations.1.0.0.nupkg
# Requires validated FM_CASH_RUNTIME_PACKAGE_PATH and explicit change authorization:
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- cash-runtime-register --apply
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- cash-runtime-api --apply
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- cash-runtime-probe --apply
```

The runtime probe performs **zero row writes**. `--apply` gates API/package customization and diagnostic invocation, not financial payments. Even if `IsInTransaction=true`, production transaction safety remains unaccepted until negative second-leg rollback, concurrency/idempotency, identity/scope permissions and audit evidence are proven with separate licensed principals. The existing 18 cash guards must remain in place with no bypass. If `IsInTransaction=false`, a proposed sequential plug-in ledger write must fail closed and use a different reviewed atomicity design.


## F5 P1 immutable operation header metadata

The `cash-operation-contract-inspect` mode retrieves metadata only. `cash-operation-contract --apply` is a **schema-only** change that adds audited `fmg_requestfingerprint` (64 characters) and `fmg_initiatinguserid` (36 characters) to each of `fmg_cashreceipt` and `fmg_cashtransfer` if absent. It validates the expected solution and organization in `Program.cs`, requires all 18 cash guard steps, verifies six-table F5 schema and requires Active request/leg keys and empty receipt/transfer/posting tables before applying. The optional attributes do not give users rights to create ledger rows.

```powershell
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- cash-operation-contract-inspect
# Only after verified dev environment, review and a passing CI:
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- cash-operation-contract --apply
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- cash-operation-contract-inspect
```

`CashExecutionCoordinator` is source-only. Its fake transaction simulator does not prove connected Dataverse rollback; do not register any operational payment/transfer Custom API or relax F5G001 until the server actor/branch adapter, uniqueness, concurrency and rollback acceptance are proven.


## F5 cash access entitlement boundary (deny-all until approved grant service)

The initial grants schema and package are **not permission assignments**. The table must be created only on the verified developer solution; all direct Create/Update/Delete must be guarded. Inspect the state first using `cash-grant-inspect` and `cash-grant-guard-inspect`.

Order of operations for first installation:

1. Build a fresh `fmg_CashAccessIntegrity.1.0.0.nupkg` using `dotnet publish/pack plugins/FinancialManager.CashAccessIntegrity`, verify with `python scripts/check_cash_access_guard_package.py <path>` and record its SHA-256. Keep it outside Git.
2. After checking `FM_ENV_URL`, `FM_ORGANIZATION_ID`, `cash-guard-inspect` (18/18) and F5 empty tables, set `FM_CASH_ACCESS_PACKAGE_PATH` to the verified NuGet. Run `cash-grant-guard-register --apply` **once** to pre-register the fail-closed plugin.
3. Run `cash-grant-schema --apply` to idempotently create the empty audited user-owned entitlement table, two lookups, three explicit-permission fields and an alternate key. The newly created index may be Pending: inspect until **Active** before go-live.
4. Immediately run `cash-grant-guard-steps --apply` and verify `cash-grant-guard-inspect` is exactly 3/3 active. Run `cash-grant-guard-probe --apply` and demand `F5_GRANT_CREATE_DENIED_F5A009`. Re-verify F5 18/18, all six existing keys Active, six F5 tables empty, and zero grant rows.
5. Stop without assigning any business grant, creating a transfer or unlocking a UI. Any partial deployment must be recovered idempotently and with the grant table access still restricted.

**Security warning:** Dataverse grants-table CRUD is sensitive authorization configuration, not a substitute for API authorizations. During initial schema customization the table is not yet guarded, so restrict the installer to the already authorized development organization and immediately register the three steps. This is not a production migration procedure; the future production rollout must be packaged/tested with no open mutation window and a reviewed dual-approval grant creation workflow.


## F5 live grant rollout and ledger adapter checkpoint

The dev grant guard was installed and independently tested with `F5A009` on direct Create; 3/3 `F5_DenyGrant_*` steps remain active alongside all 18 `F5G001` cash guards. The empty grant table has two lookups, scoped operation Choice values `797282000..797282002` and a composite alternate key. Its index may report `Pending`; run `cash-grant-inspect` to check status until it becomes `Active`. Do not create any grant row before an independent audited approval API has been deployed.

`plugins/FinancialManager.CashOperations/DataverseCashLedgerTransaction.cs` is **source only**. It translates already-authorized domain plans to real Dataverse field types and uses row-version-aware completion. Calling it without a reviewed trusted Custom API context will fail at `F5G001` (as intended). Its unit-test FakeDataverse implements the SDK surface but does not prove Dataverse transactions, roles, or rollback. Existing `fmg_CashTransactionReadiness` remains read-only and should not be overwritten by this new source.


## Validated read-only F5 contract checks (2026-10-26)

`cash-runtime-probe` is a **read-only** invocation of the existing `fmg_CashTransactionReadiness` Custom API. It does not need `--apply`, does not create cash rows, and emits observed stage, IsInTransaction, identity parity and `CashWritesEnabled=False`. Dev evidence observed stage 30, in-transaction True and matching identities. This evidence does not relax F5G001.

`cash-ledger-contract-inspect` is read-only. It checks that receipt/transfer/posting tables have exactly the expected SDK field types, PEN money precision 2, cash status/operation choices, lookup targets, active idempotency keys, audit/concurrency, and no stored money rows. Run before any new API test; if drift is reported, repair schema separately, do not try a cash write. `F5_CashOperationEndpoint` remains **unregistered source**, no operational API should be deployed by this command.


## F5 cash-grant expiry safety migration

`cash-grant-expiry-inspect` is read-only. `cash-grant-expiry --apply` is a guarded, idempotent **metadata-only** migration that adds `fmg_cashaccessgrant.fmg_expiresatutc` (audited, TimeZoneIndependent DateAndTime) and publishes only this table. It requires the grant unique key to be `Active`, 18/18 F5G001 cash and 3/3 F5A009 grant guards, all six F5 cash tables empty, and no grant rows. It assigns no roles and creates no authorizations. The SDK reader requires expiry when any grant eventually exists and denies expired grants. No grant creation API is registered or used as part of this migration.


## F5 locked grant request review records

Read-only: `cash-review-inspect`, `cash-review-guard-inspect`. Developer deployment only: first build/verify the isolated `fmg_CashGrantRequestIntegrity.1.0.0.nupkg`, set `FM_CASH_GRANT_REQUEST_PACKAGE_PATH`, run `cash-review-guard-register --apply` to preregister the deny-all plugin, then `cash-review-schema --apply` to create an empty audited request table and `fmg_CGR_ExternalRequest` key, then immediately `cash-review-guard-steps --apply`. Verify all three request guards Active and `cash-review-guard-probe --apply` rejects a direct Create with `F5Q009`. Read back the new key until Active, no grant rows, all 18 cash and 3 existing cash-grant guards intact, six cash tables empty. Never treat a pending proposal as a financial grant. There are no user-role assignments or functional approval APIs in this deployment.


## Source-only F5 review workflow and effective privileges

`cash-review-privileges-inspect` validates generated role privilege names and all 24 unchanged guards; it is **read-only**. `DataverseGrantReviewPrivileges` checks exact effective SDK privileges for proposal and Global-depth review; `DataverseGrantReviewWorkflow` prepares transactional request/grant writes, but **none can succeed on live Dataverse while the deny-all F5Q009/F5A009 guards remain active**. `F5_GrantReviewEndpoint` is unregistered and rejects all client identity/privilege override arguments. No request/grant data, financial movements, roles or approvers were inserted in this work. The next controlled deployment must validate the original SDK security principal, reviewer/proposer role assignments, platform-only nested write ticket and real rollback before any guard changes.


## Development-only nested transaction rollback probe

`cash-trust-inspect` is read-only. Controlled testing only on `financial-manager-dev`: pack `plugins/FinancialManager.CashTrustProbe` as `fmg_CashTrustProbe.1.0.0.nupkg`; verify a single expected plugin DLL and SHA-256 out of Git; set `FM_CASH_TRUST_PROBE_PACKAGE_PATH`. `cash-trust-setup --apply` checks original 24 F5 guards and all six empty ledger tables plus no live grants/reviews, then installs a **separate** two-plugin package, creates/publishes the isolated `fmg_cashtrustprobe` synthetic table, registers its own PreValidation CRUD guards, registers the synthetic Custom API with exact Create privilege and required `RequestKey` string parameter, and calls `cash-trust-probe` to verify direct-write denial and actual rollback of both synthetic records. Failure at any phase must be investigated; do not run operational financial Custom APIs. The mode does not change any existing 24 F5 guard or create rows in cash/grants/reviews. For partial provisioning, inspect state then use only the necessary `cash-trust-package`, `cash-trust-schema`, `cash-trust-steps`, `cash-trust-api`, `cash-trust-probe` modes with `--apply`.


### Synthetic origin-step diagnostic (development only; not financial authorization)

The original `SharedVariables` scope protocol was rejected by the real synchronous Dataverse nested pipeline: parent/child transaction, identity, correlation and execution stages match, but server-minted tickets were absent from BOTH parent and child variables. The source helper is **not accepted** for production guards. `cash-trust-mainstep-inspect` independently finds the single active stage-30 main SDK step for the synthetic API and `cash-trust-pin --apply` updates `configuration` on only its three synthetic guard steps to the verified MainOperation ID (one-off SDK registration pin, not a cryptographic credential). `F5_SyntheticServerOriginPolicy` matches that pin, exact origin and strict test table/record/verb, with independent negative unit tests. The isolated diagnostic package may be updated with `pac plugin push --pluginId <verified synthetic package GUID> --pluginFile <verified NuGet path>` after retaining hash-verified recovery copies out of Git. Never use direct SDK `PluginPackage.content` reads as a backup strategy; that content is file-backed and was not returned by the query. A pinned-origin transaction/rollback probe has **not been accepted** in live Dataverse because the latest execution attempt was blocked by tool safety controls. Production F5 guards remain 24/24 deny-all and cash endpoints unregistered.
