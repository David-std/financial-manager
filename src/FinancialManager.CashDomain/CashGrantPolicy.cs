using System;
using System.Collections.Generic;
using System.Linq;

namespace FinancialManager.CashDomain
{
    /// <summary>
    /// Exact-user/account/operation grants, exclusively loaded from trusted
    /// server-side Dataverse rows. A global administrator role and a UI-selected
    /// branch are never implicit business approval. No team inheritance yet.
    /// </summary>
    public static class CashGrantPolicy
    {
        public static TrustedCashActor BuildActor(Guid platformUserId,
            IEnumerable<CashAccountSnapshot> accountsFromServer,
            IEnumerable<CashGrantSnapshot> grantsFromServer)
        {
            if (platformUserId == Guid.Empty ||
                accountsFromServer == null || grantsFromServer == null)
                throw new CashPlanningException("F5A001", "Verified principal and grant evidence are required.");

            var accounts = accountsFromServer.ToArray();
            var grants = grantsFromServer.ToArray();
            if (accounts.Length == 0 || accounts.Any(x =>
                x == null || x.AccountId == Guid.Empty) ||
                accounts.Select(x => x.AccountId).Distinct().Count() != accounts.Length)
                throw new CashPlanningException("F5A001", "Untrusted or duplicate cash account records.");

            if (grants.Any(g => g == null || g.GrantId == Guid.Empty ||
                g.AccountId == Guid.Empty || g.PrincipalId == Guid.Empty ||
                !Enum.IsDefined(typeof(CashOperationKind), g.Operation)))
                throw new CashPlanningException("F5A001", "Malformed server cash authorization evidence.");
            if (grants.Select(x => x.GrantId).Distinct().Count() != grants.Length ||
                grants.Select(x => (x.PrincipalId, x.AccountId, x.Operation))
                    .Distinct().Count() != grants.Length)
                throw new CashPlanningException("F5A001", "Duplicate cash authorization evidence.");

            var allowed = new HashSet<Guid>();
            var kinds = new HashSet<CashOperationKind>();
            foreach (var a in accounts)
            {
                if (!a.IsActive)
                    continue;
                foreach (var g in grants)
                {
                    if (!g.IsActive || g.PrincipalId != platformUserId ||
                        g.AccountId != a.AccountId)
                        continue;
                    if (a.BranchId.HasValue)
                    {
                        if (a.BranchId.Value == Guid.Empty || a.BranchIsActive != true ||
                            g.AllowSharedAccount)
                            continue; // shared permission cannot leak into branch accounts
                    }
                    else if (!g.AllowSharedAccount)
                        continue; // null branch is never automatically Shared
                    allowed.Add(a.AccountId);
                    kinds.Add(g.Operation);
                }
            }

            // TrustedCashActor's existing account+operation sets represent a
            // Cartesian product; this can widen rights across accounts. A
            // specialized actor with per-account operation grants is mandatory.
            return new TrustedCashActor(platformUserId, allowed, kinds,
                grants.Where(g => g.IsActive && g.PrincipalId == platformUserId)
                    .Where(g => accounts.Any(a => a.AccountId == g.AccountId &&
                        a.IsActive && (a.BranchId.HasValue
                            ? a.BranchId.Value != Guid.Empty && a.BranchIsActive == true && !g.AllowSharedAccount
                            : g.AllowSharedAccount)))
                    .Select(g => new CashAccountPermission(g.AccountId, g.Operation)));
        }
    }

    public sealed class CashGrantSnapshot
    {
        public CashGrantSnapshot(Guid grantId, Guid principalId, Guid accountId,
            CashOperationKind operation, bool isActive, bool allowSharedAccount)
        {
            GrantId = grantId;
            PrincipalId = principalId;
            AccountId = accountId;
            Operation = operation;
            IsActive = isActive;
            AllowSharedAccount = allowSharedAccount;
        }
        public Guid GrantId { get; }
        public Guid PrincipalId { get; }
        public Guid AccountId { get; }
        public CashOperationKind Operation { get; }
        public bool IsActive { get; }
        public bool AllowSharedAccount { get; }
    }

    public sealed class CashAccountPermission
    {
        public CashAccountPermission(Guid accountId, CashOperationKind operation)
        {
            AccountId = accountId;
            Operation = operation;
        }
        public Guid AccountId { get; }
        public CashOperationKind Operation { get; }
    }
}
