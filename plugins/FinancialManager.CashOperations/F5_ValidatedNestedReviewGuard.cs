using System;
using System.Linq;
using FinancialManager.CashDomain;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// F5 review cutover candidate. A signed-in caller cannot manufacture a
    /// MainOperation step identity. Parent lineage is only the origin check:
    /// the guard additionally rechecks privileges and immutable request data.
    /// Register as shadow PreValidation steps while F5Q009/F5A009 still deny.
    /// Never use client tags, SharedVariables, administrator bypass or SYSTEM.
    /// </summary>
    public sealed class F5_ValidatedNestedReviewGuard : IPlugin
    {
        private readonly Guid submitPin;
        private readonly Guid decidePin;

        public F5_ValidatedNestedReviewGuard(string unsecureConfiguration,
            string secureConfiguration)
        {
            var parts = unsecureConfiguration?.Split('|');
            if (parts == null || parts.Length != 2 ||
                !Guid.TryParseExact(parts[0], "D", out submitPin) ||
                !Guid.TryParseExact(parts[1], "D", out decidePin) ||
                submitPin == Guid.Empty || decidePin == Guid.Empty ||
                submitPin == decidePin)
                throw Deny("Missing two independent MainOperation pins.");
        }

        public void Execute(IServiceProvider provider)
        {
            var ctx = provider?.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext;
            var factory = provider?.GetService(typeof(IOrganizationServiceFactory))
                as IOrganizationServiceFactory;
            if (ctx == null || factory == null || ctx.UserId == Guid.Empty ||
                ctx.UserId != ctx.InitiatingUserId)
                throw Deny("Missing platform user or services.");
            var service = factory.CreateOrganizationService(ctx.UserId);
            if (service == null) throw Deny("Missing actor-scoped service.");
            Require(ctx, submitPin, decidePin, service);
        }

        public static void Require(IPluginExecutionContext child,
            Guid verifiedSubmitStep, Guid verifiedDecisionStep,
            IOrganizationService actorService)
        {
            if (child == null || actorService == null ||
                verifiedSubmitStep == Guid.Empty || verifiedDecisionStep == Guid.Empty ||
                verifiedSubmitStep == verifiedDecisionStep ||
                child.Stage != 10 || child.Mode != 0 || !child.IsInTransaction ||
                child.UserId == Guid.Empty ||
                child.UserId != child.InitiatingUserId)
                throw Deny("Unverified nested review transaction.");

            var parent = child.ParentContext;
            if (parent == null || parent.Stage != 30 || parent.Mode != 0 ||
                !parent.IsInTransaction || parent.Depth < 1 ||
                child.Depth != parent.Depth + 1 ||
                parent.OwningExtension?.LogicalName != "sdkmessageprocessingstep" ||
                parent.CorrelationId == Guid.Empty ||
                child.CorrelationId != parent.CorrelationId ||
                child.OperationId != parent.OperationId ||
                parent.UserId == Guid.Empty ||
                parent.UserId != parent.InitiatingUserId ||
                parent.UserId != child.UserId ||
                parent.InitiatingUserId != child.InitiatingUserId)
                throw Deny("Unverified pinned review origin.");

            var submit = parent.MessageName == F5_GrantReviewEndpoint.SubmitMessage &&
                parent.OwningExtension.Id == verifiedSubmitStep;
            var decide = parent.MessageName == F5_GrantReviewEndpoint.DecideMessage &&
                parent.OwningExtension.Id == verifiedDecisionStep;
            if (!submit && !decide) throw Deny("Unexpected MainOperation identity.");
            if (child.MessageName == "Delete") throw Deny("Delete never authorized.");

            Entity? target = child.InputParameters?.ContainsKey("Target") == true
                ? child.InputParameters["Target"] as Entity : null;
            if (target == null || target.Id == Guid.Empty ||
                target.LogicalName != child.PrimaryEntityName)
                throw new InvalidPluginExecutionException("F5Q040: Exact target identity required.");

            var privileges = new DataverseGrantReviewPrivileges(actorService);
            if (submit && child.MessageName == "Create" &&
                child.PrimaryEntityName == "fmg_cashgrantrequest")
            {
                privileges.RequireCanPropose(child.InitiatingUserId);
                var proposed = CashGrantApprovalPolicy.Prepare(
                    F5_GrantReviewEndpoint.ParseSubmission(parent.InputParameters),
                    child.InitiatingUserId, DateTime.UtcNow);
                RequireFields(target, "fmg_name", "fmg_externalrequestid",
                    "fmg_requestfingerprint", "fmg_proposer", "fmg_beneficiary",
                    "fmg_cashaccount", "fmg_operation", "fmg_changekind",
                    "fmg_allowshared", "fmg_reason", "fmg_expiresatutc",
                    "fmg_reviewstatus");
                if (target.Id != proposed.RequestId ||
                    target.GetAttributeValue<string>("fmg_name") !=
                        "F5-review-" + proposed.RequestId.ToString("N") ||
                    target.GetAttributeValue<string>("fmg_externalrequestid") !=
                        proposed.RequestId.ToString("D") ||
                    target.GetAttributeValue<string>("fmg_requestfingerprint") != proposed.Fingerprint ||
                    !SameReference(target, "fmg_proposer", "systemuser", proposed.ProposerId) ||
                    !SameReference(target, "fmg_beneficiary", "systemuser", proposed.BeneficiaryId) ||
                    !SameReference(target, "fmg_cashaccount", "fmg_cashaccount", proposed.AccountId) ||
                    target.GetAttributeValue<OptionSetValue>("fmg_operation")?.Value !=
                        Operation(proposed.Operation) ||
                    target.GetAttributeValue<OptionSetValue>("fmg_changekind")?.Value !=
                        (proposed.Change == CashGrantChangeKind.Grant ? 797283100 : 797283101) ||
                    target.GetAttributeValue<bool>("fmg_allowshared") != proposed.ExplicitShared ||
                    target.GetAttributeValue<string>("fmg_reason") != proposed.Reason ||
                    target.GetAttributeValue<DateTime>("fmg_expiresatutc").ToUniversalTime() !=
                        proposed.ExpiresUtc ||
                    target.GetAttributeValue<OptionSetValue>("fmg_reviewstatus")?.Value != 797283000)
                    throw Deny("Proposal fields differ from authenticated parent request.");
                return;
            }

            if (decide)
            {
                privileges.RequireCanDecide(child.InitiatingUserId);
                var args = F5_GrantReviewEndpoint.ParseDecision(parent.InputParameters);
                var pending = new DataverseCashGrantRequestReader(actorService).ReadPending(args.Item1);
                var approved = CashGrantApprovalPolicy.Approve(
                    pending.Proposal, child.InitiatingUserId, DateTime.UtcNow);
                if (child.PrimaryEntityName == "fmg_cashgrantrequest" &&
                    child.MessageName == "Update")
                {
                    RequireFields(target, "fmg_reviewstatus", "fmg_approver", "fmg_decidedatutc");
                    var timestamp = target.GetAttributeValue<DateTime>("fmg_decidedatutc");
                    if (target.Id != args.Item1 ||
                        target.RowVersion != pending.RowVersion ||
                        target.GetAttributeValue<OptionSetValue>("fmg_reviewstatus")?.Value !=
                            (args.Item2 ? 797283001 : 797283002) ||
                        !SameReference(target, "fmg_approver", "systemuser", approved.ApproverId) ||
                        timestamp.Kind != DateTimeKind.Utc ||
                        timestamp > DateTime.UtcNow.AddMinutes(2) ||
                        timestamp < DateTime.UtcNow.AddMinutes(-2))
                        throw Deny("Decision is not an exact versioned reviewer write.");
                    return;
                }
                if (args.Item2 && child.PrimaryEntityName == "fmg_cashaccessgrant")
                {
                    var req = pending.Proposal;
                    if (child.MessageName == "Create" &&
                        req.Change == CashGrantChangeKind.Grant)
                    {
                        RequireFields(target, "fmg_name", "fmg_principal", "fmg_cashaccount",
                            "fmg_operation", "fmg_isactive", "fmg_allowshared",
                            "fmg_expiresatutc");
                        if (target.GetAttributeValue<string>("fmg_name") !=
                                "Approved F5 " + req.RequestId.ToString("N") ||
                            !target.GetAttributeValue<bool>("fmg_isactive"))
                            throw Deny("Invalid new grant.");
                    }
                    else if (child.MessageName == "Update")
                    {
                        RequireFields(target, "fmg_isactive", "fmg_allowshared",
                            "fmg_expiresatutc");
                        if (string.IsNullOrWhiteSpace(target.RowVersion))
                            throw Deny("Grant update must be conditional.");
                        var current = actorService.Retrieve("fmg_cashaccessgrant",
                            target.Id, new ColumnSet("fmg_principal", "fmg_cashaccount",
                                "fmg_operation", "fmg_isactive"));
                        if (current == null || current.Id != target.Id ||
                            string.IsNullOrWhiteSpace(current.RowVersion) ||
                            target.RowVersion != current.RowVersion ||
                            current.GetAttributeValue<bool>("fmg_isactive") ==
                                (req.Change == CashGrantChangeKind.Grant))
                            throw Deny("Grant state transition or ETag mismatch.");
                        if (!SameReference(current, "fmg_principal", "systemuser", req.BeneficiaryId) ||
                            !SameReference(current, "fmg_cashaccount", "fmg_cashaccount", req.AccountId) ||
                            current.GetAttributeValue<OptionSetValue>("fmg_operation")?.Value !=
                                Operation(req.Operation))
                            throw Deny("Grant identity differs from approved request.");
                    }
                    else throw Deny("Unsupported grant mutation.");
                    if ((child.MessageName == "Create" &&
                            (!SameReference(target, "fmg_principal", "systemuser", req.BeneficiaryId) ||
                             !SameReference(target, "fmg_cashaccount", "fmg_cashaccount", req.AccountId) ||
                             target.GetAttributeValue<OptionSetValue>("fmg_operation")?.Value !=
                                 Operation(req.Operation))) ||
                        target.GetAttributeValue<bool>("fmg_isactive") !=
                            (req.Change == CashGrantChangeKind.Grant) ||
                        target.GetAttributeValue<bool>("fmg_allowshared") != req.ExplicitShared ||
                        target.GetAttributeValue<DateTime>("fmg_expiresatutc").ToUniversalTime() !=
                            req.ExpiresUtc)
                        throw Deny("Grant fields differ from approved immutable request.");
                    return;
                }
            }
            throw Deny("Mutation is outside the review state machine.");
        }

        private static int Operation(CashOperationKind op) =>
            op == CashOperationKind.SettleReceipt ? 797282000 :
            op == CashOperationKind.InternalTransfer ? 797282001 :
            op == CashOperationKind.Reversal ? 797282002 :
            throw new InvalidPluginExecutionException("F5Q040: Unsupported operation.");

        private static bool SameReference(Entity row, string name, string table, Guid id)
        {
            var item = row.GetAttributeValue<EntityReference>(name);
            return item?.LogicalName == table && item.Id == id && id != Guid.Empty;
        }

        private static void RequireFields(Entity target, params string[] names)
        {
            if (target.Attributes.Count != names.Length ||
                target.Attributes.Keys.Any(key => !names.Contains(key, StringComparer.Ordinal)) ||
                names.Any(key => !target.Contains(key)))
                throw Deny("Extra or missing fields in financial review mutation.");
        }

        private static InvalidPluginExecutionException Deny(string reason) =>
            new InvalidPluginExecutionException("F5Q040: " + reason);
    }
}
