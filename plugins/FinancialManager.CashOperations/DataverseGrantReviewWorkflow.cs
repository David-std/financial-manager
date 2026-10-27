using System;
using System.Linq;
using FinancialManager.CashDomain;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// SOURCE ONLY, NOT DEPLOYED as an executable Custom API.
    /// All writes currently fail F5Q009/F5A009 by design. This orchestrator
    /// is intended to run only in a validated synchronous MainOperation,
    /// after a separately reviewed nested-write guard policy is accepted.
    /// Never supply a privileged service account or bypass bit.
    /// </summary>
    public sealed class DataverseGrantReviewWorkflow
    {
        private readonly IOrganizationService service;
        private readonly DataverseGrantReviewPrivileges privileges;
        private readonly DataverseCashGrantRequestReader requestReader;

        public DataverseGrantReviewWorkflow(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            privileges = new DataverseGrantReviewPrivileges(service);
            requestReader = new DataverseCashGrantRequestReader(service);
        }

        private static void RequireRuntime(CashRuntimeEvidence runtime)
        {
            CashRuntimePolicy.Require(runtime);
        }

        private void RequireAccount(Guid accountId)
        {
            var account = service.Retrieve("fmg_cashaccount", accountId,
                new ColumnSet("fmg_isactive", "fmg_branch"));
            if (account == null || account.Id != accountId ||
                !account.Contains("fmg_isactive") ||
                !account.GetAttributeValue<bool>("fmg_isactive"))
                throw new CashPlanningException("F5Q020", "Inactive or unknown cash account.");
            var branch = account.GetAttributeValue<EntityReference>("fmg_branch");
            if (branch != null)
            {
                if (branch.LogicalName != "fmg_branch" || branch.Id == Guid.Empty)
                    throw new CashPlanningException("F5Q020", "Malformed cash branch relation.");
                var current = service.Retrieve("fmg_branch", branch.Id,
                    new ColumnSet("fmg_isactive"));
                if (current == null || current.Id != branch.Id ||
                    !current.Contains("fmg_isactive") ||
                    !current.GetAttributeValue<bool>("fmg_isactive"))
                    throw new CashPlanningException("F5Q020", "Cash account branch is inactive.");
            }
        }

        private void RequireBeneficiary(Guid userId)
        {
            var user = service.Retrieve("systemuser", userId,
                new ColumnSet("isdisabled", "accessmode", "applicationid"));
            if (user == null || user.Id != userId ||
                !user.Contains("isdisabled") || !user.Contains("accessmode") ||
                user.GetAttributeValue<bool>("isdisabled") ||
                user.GetAttributeValue<OptionSetValue>("accessmode")?.Value != 0 ||
                user.GetAttributeValue<Guid>("applicationid") != Guid.Empty)
                throw new CashPlanningException("F5Q020",
                    "Grant beneficiary must be an active interactive user.");
        }

        private static int OperationOption(CashOperationKind kind)
        {
            switch (kind)
            {
                case CashOperationKind.SettleReceipt: return 797282000;
                case CashOperationKind.InternalTransfer: return 797282001;
                case CashOperationKind.Reversal: return 797282002;
                default: throw new CashPlanningException("F5Q020", "Unknown permission kind.");
            }
        }

        public Guid Submit(CashRuntimeEvidence runtime, CashGrantChangeRequest request,
            DateTime serverUtcNow)
        {
            RequireRuntime(runtime);
            privileges.RequireCanPropose(runtime.InitiatingUserId);
            var proposed = CashGrantApprovalPolicy.Prepare(request,
                runtime.InitiatingUserId, serverUtcNow);
            RequireBeneficiary(proposed.BeneficiaryId);
            RequireAccount(proposed.AccountId);

            // Alternative key prevents two simultaneous submissions with
            // the same request GUID. A collision MUST fail transactionally,
            // not update or silently overwrite the first request.
            var entity = new Entity("fmg_cashgrantrequest", proposed.RequestId);
            entity["fmg_name"] = "F5-review-" + proposed.RequestId.ToString("N");
            entity["fmg_externalrequestid"] = proposed.RequestId.ToString("D");
            entity["fmg_requestfingerprint"] = proposed.Fingerprint;
            entity["fmg_proposer"] = new EntityReference("systemuser", proposed.ProposerId);
            entity["fmg_beneficiary"] = new EntityReference("systemuser", proposed.BeneficiaryId);
            entity["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", proposed.AccountId);
            entity["fmg_operation"] = new OptionSetValue(OperationOption(proposed.Operation));
            entity["fmg_changekind"] = new OptionSetValue(
                proposed.Change == CashGrantChangeKind.Grant ? 797283100 : 797283101);
            entity["fmg_allowshared"] = proposed.ExplicitShared;
            entity["fmg_reason"] = proposed.Reason;
            entity["fmg_expiresatutc"] = proposed.ExpiresUtc;
            entity["fmg_reviewstatus"] = new OptionSetValue(797283000);
            var created = service.Create(entity);
            if (created != proposed.RequestId)
                throw new CashPlanningException("F5Q020", "Unexpected immutable request identity.");
            return created;
        }

        public void Decide(CashRuntimeEvidence runtime, Guid requestId,
            bool approved, DateTime serverUtcNow)
        {
            RequireRuntime(runtime);
            privileges.RequireCanDecide(runtime.InitiatingUserId);
            var pending = requestReader.ReadPending(requestId);
            // This requires distinct, server-authenticated persons; the user
            // may defer separate-account manual UAT, but not this invariant.
            var decision = CashGrantApprovalPolicy.Approve(pending.Proposal,
                runtime.InitiatingUserId, serverUtcNow);
            if (approved)
            {
                RequireBeneficiary(decision.Request.BeneficiaryId);
                RequireAccount(decision.Request.AccountId);
                ApplyGrant(decision);
            }

            // This ETag conditional write must share the same Dataverse
            // transaction as any grant Create/Update above. If it fails,
            // Dataverse must roll back the grant change, not leave split state.
            var update = new Entity("fmg_cashgrantrequest", requestId)
            {
                RowVersion = pending.RowVersion
            };
            update["fmg_reviewstatus"] =
                new OptionSetValue(approved ? 797283001 : 797283002);
            update["fmg_approver"] =
                new EntityReference("systemuser", runtime.InitiatingUserId);
            update["fmg_decidedatutc"] = serverUtcNow;
            service.Execute(new UpdateRequest
            {
                Target = update,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            });
        }

        private void ApplyGrant(ApprovedCashGrantChange decision)
        {
            var plan = decision.Request;
            var query = new QueryExpression("fmg_cashaccessgrant")
            {
                ColumnSet = new ColumnSet("fmg_principal", "fmg_cashaccount",
                    "fmg_operation", "fmg_isactive", "fmg_allowshared",
                    "fmg_expiresatutc"),
                TopCount = 2
            };
            query.Criteria.AddCondition("fmg_principal", ConditionOperator.Equal,
                plan.BeneficiaryId);
            query.Criteria.AddCondition("fmg_cashaccount", ConditionOperator.Equal,
                plan.AccountId);
            query.Criteria.AddCondition("fmg_operation", ConditionOperator.Equal,
                OperationOption(plan.Operation));
            var result = service.RetrieveMultiple(query);
            if (result == null || result.MoreRecords || result.Entities.Count > 1)
                throw new CashPlanningException("F5Q021", "Ambiguous existing financial grant.");

            var row = result.Entities.SingleOrDefault();
            if (plan.Change == CashGrantChangeKind.Revoke)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.RowVersion) ||
                    !row.Contains("fmg_isactive") ||
                    !row.GetAttributeValue<bool>("fmg_isactive"))
                    throw new CashPlanningException("F5Q021", "No active grant exists to revoke.");
                UpdateGrant(row, false, plan.ExplicitShared, plan.ExpiresUtc);
                return;
            }
            if (row == null)
            {
                var created = new Entity("fmg_cashaccessgrant");
                created["fmg_name"] = "Approved F5 " + plan.RequestId.ToString("N");
                created["fmg_principal"] =
                    new EntityReference("systemuser", plan.BeneficiaryId);
                created["fmg_cashaccount"] =
                    new EntityReference("fmg_cashaccount", plan.AccountId);
                created["fmg_operation"] = new OptionSetValue(OperationOption(plan.Operation));
                created["fmg_isactive"] = true;
                created["fmg_allowshared"] = plan.ExplicitShared;
                created["fmg_expiresatutc"] = plan.ExpiresUtc;
                if (service.Create(created) == Guid.Empty)
                    throw new CashPlanningException("F5Q021", "Grant creation not acknowledged.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(row.RowVersion) ||
                    !row.Contains("fmg_isactive") ||
                    row.GetAttributeValue<bool>("fmg_isactive"))
                    throw new CashPlanningException("F5Q021", "Already-active grant cannot be overwritten.");
                UpdateGrant(row, true, plan.ExplicitShared, plan.ExpiresUtc);
            }
        }

        private void UpdateGrant(Entity row, bool isActive,
            bool allowShared, DateTime expiry)
        {
            var update = new Entity("fmg_cashaccessgrant", row.Id)
            {
                RowVersion = row.RowVersion
            };
            update["fmg_isactive"] = isActive;
            update["fmg_allowshared"] = allowShared;
            update["fmg_expiresatutc"] = expiry;
            service.Execute(new UpdateRequest
            {
                Target = update,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            });
        }
    }
}
