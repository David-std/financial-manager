using System;
using System.Collections.Generic;
using System.Linq;
using FinancialManager.CashDomain;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// Read-only Dataverse-backed authorization adapter, no privileged writes.
    /// Requires an independently protected fmg_cashaccessgrant table; until
    /// that table is deployed and populated through a reviewed approval
    /// workflow, all business operations are denied.
    /// </summary>
    public sealed class DataverseCashAuthorization : IServerCashAuthorization
    {
        private const string Grants = "fmg_cashaccessgrant";
        private readonly IOrganizationService service;

        public DataverseCashAuthorization(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }

        // Explicit publisher-range Dataverse Choice option IDs.
        // Never cast arbitrary picklist values directly to domain enums.
        public static CashOperationKind DecodeOperation(int option)
        {
            switch (option)
            {
                case 797282000: return CashOperationKind.SettleReceipt;
                case 797282001: return CashOperationKind.InternalTransfer;
                case 797282002: return CashOperationKind.Reversal;
                default: throw new CashPlanningException("F5A001",
                    "Unknown Dataverse cash operation grant.");
            }
        }

        public TrustedCashActor Authorize(Guid platformInitiatingUserId,
            CashOperationRequest request)
        {
            if (platformInitiatingUserId == Guid.Empty || request == null)
                throw new CashPlanningException("F5A001", "Verified platform user and request required.");

            // Dataverse owns this identity. Never use a userId or branch ID
            // provided by the calling Canvas app, Fabric, or React transport.
            var user = service.Retrieve("systemuser", platformInitiatingUserId,
                new ColumnSet("isdisabled"));
            if (user == null || user.Id != platformInitiatingUserId ||
                !user.Contains("isdisabled") ||
                user.GetAttributeValue<bool>("isdisabled"))
                throw new CashPlanningException("F5A001", "User not active or unverifiable.");

            var query = new QueryExpression(Grants)
            {
                ColumnSet = new ColumnSet("fmg_principal", "fmg_cashaccount",
                    "fmg_operation", "fmg_isactive", "fmg_allowshared"),
                TopCount = 100
            };
            query.Criteria.AddCondition("fmg_principal", ConditionOperator.Equal,
                platformInitiatingUserId);
            var matches = service.RetrieveMultiple(query);
            if (matches == null || matches.MoreRecords || matches.Entities.Count >= 100)
                throw new CashPlanningException("F5A001", "Unbounded or incomplete grant evidence.");

            var grants = new List<CashGrantSnapshot>();
            foreach (var row in matches.Entities)
            {
                var principal = row.GetAttributeValue<EntityReference>("fmg_principal");
                var account = row.GetAttributeValue<EntityReference>("fmg_cashaccount");
                var kind = row.GetAttributeValue<OptionSetValue>("fmg_operation");
                if (row.Id == Guid.Empty || principal == null ||
                    principal.LogicalName != "systemuser" ||
                    principal.Id != platformInitiatingUserId || account == null ||
                    account.LogicalName != "fmg_cashaccount" ||
                    account.Id == Guid.Empty || kind == null ||
                    !row.Contains("fmg_isactive") || !row.Contains("fmg_allowshared"))
                    throw new CashPlanningException("F5A001", "Malformed grant from Dataverse.");
                // The choices must be the exact operation codes in the schema,
                // not another table's status or client-selected labels.
                var operation = DecodeOperation(kind.Value);
                grants.Add(new CashGrantSnapshot(row.Id, principal.Id, account.Id,
                    operation,
                    row.GetAttributeValue<bool>("fmg_isactive"),
                    row.GetAttributeValue<bool>("fmg_allowshared")));
            }

            // No grants means no effective cash permissions, even for admins.
            // Only requested account(s) need validation but ALL relevant rows
            // must be checked for duplicates and malformed evidence first.
            var required = new HashSet<Guid> { request.SourceAccountId };
            if (request.DestinationAccountId.HasValue)
                required.Add(request.DestinationAccountId.Value);
            var accounts = new List<CashAccountSnapshot>();
            foreach (var accountId in required)
            {
                if (accountId == Guid.Empty)
                    continue;
                var row = service.Retrieve("fmg_cashaccount", accountId,
                    new ColumnSet("fmg_isactive", "fmg_branch"));
                if (row == null || row.Id != accountId ||
                    !row.Contains("fmg_isactive"))
                    throw new CashPlanningException("F5A001", "Cash account state unavailable.");
                var branch = row.GetAttributeValue<EntityReference>("fmg_branch");
                if (branch != null && (branch.Id == Guid.Empty ||
                    branch.LogicalName != "fmg_branch"))
                    throw new CashPlanningException("F5A001", "Malformed account branch relation.");
                bool? activeBranch = null;
                if (branch != null)
                {
                    var branchRow = service.Retrieve("fmg_branch", branch.Id,
                        new ColumnSet("fmg_isactive"));
                    if (branchRow == null || branchRow.Id != branch.Id ||
                        !branchRow.Contains("fmg_isactive"))
                        throw new CashPlanningException("F5A001", "Branch state unavailable.");
                    activeBranch = branchRow.GetAttributeValue<bool>("fmg_isactive");
                }
                accounts.Add(new CashAccountSnapshot(accountId, branch?.Id,
                    row.GetAttributeValue<bool>("fmg_isactive"), activeBranch));
            }
            return CashGrantPolicy.BuildActor(platformInitiatingUserId, accounts, grants);
        }
    }
}
