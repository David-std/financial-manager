using System;
using System.Collections.Generic;
using System.Linq;

namespace FinancialManager.CashDomain
{
    /// <summary>
    /// Fail-closed bridge between verified server authorization evidence and
    /// TrustedCashActor. The real Dataverse adapter is NOT implemented yet.
    /// Never accept these parameters from a browser, report, Power Apps
    /// custom page or other untrusted financial client.
    /// </summary>
    public static class CashAccountScopePolicy
    {
        public static TrustedCashActor BuildActor(
            Guid subjectId,
            IEnumerable<CashAccountSnapshot> accountMetadataFromServer,
            IEnumerable<Guid> authorizedBranchIdsFromServer,
            IEnumerable<Guid> approvedSharedAccountIdsFromServer,
            IEnumerable<CashOperationKind> permittedOperationsFromServer)
        {
            if (subjectId == Guid.Empty)
                throw new CashPlanningException("F5P002", "A verified principal is required.");
            if (accountMetadataFromServer == null)
                throw new CashPlanningException("F5P002", "Verified account metadata is required.");

            // fmg_cashaccount and fmg_branch are OrganizationOwned in the
            // inspected environment. OwnerId is NOT a branch authorization.
            // Branch scope and shared scope must be granted explicitly.
            var branches = new HashSet<Guid>(authorizedBranchIdsFromServer ?? Array.Empty<Guid>());
            var sharedAccounts = new HashSet<Guid>(approvedSharedAccountIdsFromServer ?? Array.Empty<Guid>());
            var allowed = new HashSet<Guid>();
            var seen = new HashSet<Guid>();
            foreach (var account in accountMetadataFromServer)
            {
                if (account == null || account.AccountId == Guid.Empty || !seen.Add(account.AccountId))
                    throw new CashPlanningException("F5P004", "Malformed or duplicate server account evidence.");
                if (!account.IsActive)
                    continue;
                if (account.BranchId.HasValue)
                {
                    if (account.BranchId.Value != Guid.Empty &&
                        account.BranchIsActive == true &&
                        branches.Contains(account.BranchId.Value))
                        allowed.Add(account.AccountId);
                }
                else if (sharedAccounts.Contains(account.AccountId))
                {
                    // Null branch does NOT automatically mean shared.
                    allowed.Add(account.AccountId);
                }
            }

            return new TrustedCashActor(subjectId, allowed,
                permittedOperationsFromServer ?? Array.Empty<CashOperationKind>());
        }
    }

    public sealed class CashAccountSnapshot
    {
        public CashAccountSnapshot(Guid accountId, Guid? branchId, bool isActive, bool? branchIsActive)
        {
            AccountId = accountId;
            BranchId = branchId;
            IsActive = isActive;
            BranchIsActive = branchIsActive;
        }
        public Guid AccountId { get; }
        public Guid? BranchId { get; }
        public bool IsActive { get; }
        public bool? BranchIsActive { get; }
    }
}
