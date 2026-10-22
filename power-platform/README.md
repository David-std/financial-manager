# Power Platform solution

This directory contains the **actual unmanaged Dataverse solution source** exported from the `financial-manager-dev` environment with Microsoft Power Platform CLI. It is not a handwritten schema reconstruction.

## Source

- Solution unique name: `FinancialManager` (version `0.1.0.0` at initial export)
- Publisher prefix: `fmg`
- Source project: [`solutions/FinancialManager/`](solutions/FinancialManager/)
- Current exported components: `Branch`, `Category`, `Cash Account`, `Financial Transaction`, nine table-scoped business rules, `Initialize Category Hierarchy` flow, relationships, choices, forms and views, the `fmg_FinancialManagerValidation` plug-in package, and its three synchronous PreValidation steps
- The enforcement plug-in **is deployed** in `financial-manager-dev` and passed six rolled-back integration checks on 2026-10-22. Budget tables and the expense app are **not yet** deployed. This source has not been import-tested into a second Dataverse environment.

## Update source from Dataverse

Authenticate to the **correct** developer environment using your own Microsoft login; never store credentials in Git.

```powershell
pac auth create --name FinancialManagerDev --environment "financial-manager-dev"
pac auth select --name FinancialManagerDev
pac env who
```

From the repository root, use a **new temporary directory** for a fresh clone and carefully review changes before replacing the tracked solution project:

```powershell
pac solution clone --name FinancialManager --packagetype Unmanaged --outputDirectory "$env:TEMP\financial-manager-export"
```

The CLI creates a `FinancialManager/` child project inside that directory. Compare its generated `src/` against `power-platform/solutions/FinancialManager/src/` and commit only reviewed changes to `develop`. Do **not** run an import to perform source control.

## Verify the extracted source

The unpacked `src/` is an inspectable representation, not a copy of Dataverse table rows. Repack it locally when checking changes:

```powershell
pac solution pack --folder "power-platform/solutions/FinancialManager/src" --zipfile "$env:TEMP\FinancialManager-unmanaged-verify.zip" --packagetype Unmanaged
```

Only import packages to an explicitly approved target environment after dependency and security checks.

## Security and limits

- The solution is **publicly visible on GitHub**. Inspect every export for secrets, personally identifiable information, tokens, endpoints, and unexpected components before committing.
- A solution export includes configuration and flow/connection **references**, but not usable connection credentials. Configure and authorize connections separately in each environment.
- Table rows (two branches, 41 categories, three cash accounts, payments, audit history) are **not** backed up or versioned by this source export. Use a reviewed data migration strategy for non-sensitive reference data.
- The plug-in enforces server-side Posted/Voided immutability and cross-record validity. **Only System Administrators may Post or Void** until actual finance roles and branch teams are designed and verified with a second identity. See [`plugins/FinancialManager.Validation`](../plugins/FinancialManager.Validation/) and [`tools/DataverseDeployment`](../tools/DataverseDeployment/).
- Local `pac solution pack` logs a **PluginAssembly root-component warning** despite producing a ZIP. Treat the exported files as versioned source, not as independently import-verified release artifacts.
- `Initialize Category Hierarchy` is a one-time setup flow: keep it disabled in normal operations and confirm its state after deployment.

See [Data model](../docs/data-model.md), [Development](../docs/development.md), and [Screens](../docs/screens.md).
