# Financial Manager budget integrity plug-in

Dataverse package: `fmg_BudgetIntegrity`. Includes three separately registered plug-in classes:

- `BG_EnforceBudgetIntegrity` on `fmg_budget`
- `BA_EnforceAllocationIntegrity` on `fmg_budgetallocation`
- `AE_BlockDirectEventWrites` on `fmg_activityevent`

Each class is registered for **Create, Update, Delete in synchronous PreValidation**, with a required `Before` pre-image for Budget and Budget Allocation Update/Delete. All nine steps must be active. The original `fmg_FinancialManagerValidation` payment plug-in is unchanged.

## Server-side rules

**Budget:** Create only Draft. Draft edits allowed by Dataverse permissions; the owner (or System Administrator) can submit a completed budget only when it has at least one allocation and a unique year/scope/branch/version. Submitted budgets are immutable except for System Administrator rejection. Approved, Rejected and Superseded budgets cannot be edited or deleted. Client-supplied Approved By/On fields are forbidden.

**Approvals intentionally fail closed:** Submitted → Approved is always rejected (BGV009) until finance roles, separation of duties, and concurrent budget-version exclusivity are implemented and tested. There is no server-approved budget yet. This is intentional; an Approved option in a Dataverse Choice is not authorization.

**Budget Allocation:** Parent must be an editable Draft budget, controlled by its user owner or a System Administrator. Month must be 1–12, amount nonnegative with at most two decimals and **explicit transactioncurrencyid in PEN**. Category must be active, a leaf with parent, and not 999. Submitted or approved allocations cannot be modified or removed.

**Concurrent uniqueness:** `fmg_BA_UniqueMonthlyCategory` is an **Active** Dataverse alternate key on `fmg_budget`, `fmg_month`, `fmg_category`. The server-side pre-check provides a readable error; the Dataverse index is the actual concurrency barrier.

**Activity Event:** Direct Create/Update/Delete is denied, including for System Administrators. A future trusted server-side event writer must be introduced, authenticated, and tested before enabling entries. Native Dataverse audit continues to be the authoritative change log.

## Build and tests

```powershell
dotnet test tests/FinancialManager.BudgetIntegrity.Tests/FinancialManager.BudgetIntegrity.Tests.csproj -c Release
dotnet build plugins/FinancialManager.BudgetIntegrity/FinancialManager.BudgetIntegrity.csproj -c Release
```

On 2026-10-22, **39 unit cases and 9 rolled-back Dataverse integration scenarios** passed, using a separate package from the payment validator. Those live tests verify direct approved creation is denied, valid draft/allocation submission, empty budget rejection, approval closure, duplicate monthly allocation rejection, Submitted immutability, direct Activity Event write denial, and authorized admin rejection.

The live test program `tools/DataverseDeployment/BudgetLiveVerifier.cs` uses `ExecuteTransaction` with a deliberately invalid final create operation so successful cases are rolled back; it must only run in the verified developer environment.

**Unverified:** actual non-admin second-user access, concurrent competing version approvals (approvals deliberately disabled), multi-environment import of the exported solution, design of trusted Activity Event writer, and branch/team finance permissions.

Related specs: [data model](../../docs/data-model.md), [security](../../docs/security.md), [validation strategy](../../docs/validation-strategy.md).
