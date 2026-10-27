using System;
using System.Collections.Generic;
using System.Linq;
using FinancialManager.CashDomain;
using FinancialManager.CashOperations;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

public sealed class DataverseGrantReviewWorkflowTests
{
    private static readonly Guid Maker = Guid.Parse("10000000-0000-4000-8000-000000000010");
    private static readonly Guid Reviewer = Guid.Parse("10000000-0000-4000-8000-000000000011");
    private static readonly Guid Recipient = Guid.Parse("10000000-0000-4000-8000-000000000012");
    private static readonly Guid Account = Guid.Parse("10000000-0000-4000-8000-000000000013");
    private static readonly Guid Branch = Guid.Parse("10000000-0000-4000-8000-000000000014");
    private static readonly DateTime Now = new DateTime(2026, 10, 26, 19, 0, 0, DateTimeKind.Utc);
    private static CashRuntimeEvidence Runtime(Guid user) =>
        new CashRuntimeEvidence(30, 0, true, user, user);

    private static CashGrantChangeRequest Change(CashGrantChangeKind kind = CashGrantChangeKind.Grant,
        Guid? beneficiary = null) =>
        new CashGrantChangeRequest(Guid.NewGuid(), beneficiary ?? Recipient, Account,
            CashOperationKind.InternalTransfer, kind, false,
            "Approved synthetic branch cash permission", Now.AddDays(7));

    private sealed class Fake : IOrganizationService
    {
        public readonly Dictionary<Guid, Entity> Rows = new Dictionary<Guid, Entity>();
        public readonly List<string> Writes = new List<string>();
        public bool AllowPropose = true;
        public bool AllowDecide = true;
        public bool ApproveGlobal = true;
        public bool DisabledUser;
        public bool NonInteractiveUser;
        public bool AppUser;
        public bool NonInteractiveRecipient;
        public bool AppRecipient;
        public bool InactiveAccount;
        public bool InactiveBranch;
        public bool RejectVersionedUpdate;
        public string? LastPrivilege;
        public RemoteExecutionContext? TrustedParent;
        public int ScopedWrites;
        private void RequireIssuedTicket(string verb, Entity target)
        {
            if (TrustedParent == null) return;
            var child = new RemoteExecutionContext
            {
                ParentContext = TrustedParent, Depth = TrustedParent.Depth + 1,
                Stage = 10, Mode = 0, IsInTransaction = true,
                UserId = TrustedParent.UserId,
                InitiatingUserId = TrustedParent.InitiatingUserId,
                CorrelationId = TrustedParent.CorrelationId,
                OperationId = TrustedParent.OperationId,
                MessageName = verb, PrimaryEntityName = target.LogicalName
            };
            child.InputParameters["Target"] = target;
            F5_ServerWriteScope.Require(child);
            ScopedWrites++;
        }
        public Entity Retrieve(string name, Guid id, ColumnSet columns)
        {
            if (name == "systemuser")
            {
                var u = new Entity(name, id);
                u["isdisabled"] = DisabledUser;
                u["accessmode"] = new OptionSetValue(
                    NonInteractiveUser || (NonInteractiveRecipient && id == Recipient) ? 4 : 0);
                if (AppUser || (AppRecipient && id == Recipient))
                    u["applicationid"] = Guid.NewGuid();
                return u;
            }
            if (name == "fmg_cashaccount")
            {
                var a = new Entity(name, id);
                a["fmg_isactive"] = !InactiveAccount;
                a["fmg_branch"] = new EntityReference("fmg_branch", Branch);
                return a;
            }
            if (name == "fmg_branch")
            {
                var b = new Entity(name, id);
                b["fmg_isactive"] = !InactiveBranch;
                return b;
            }
            if (!Rows.TryGetValue(id, out var value) || value.LogicalName != name)
                throw new InvalidOperationException("No " + name);
            value.RowVersion = "39";
            return value;
        }
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = Assert.IsType<QueryExpression>(query);
            Assert.Equal("fmg_cashaccessgrant", q.EntityName);
            var filtered = Rows.Values.Where(e => e.LogicalName == q.EntityName);
            foreach (var condition in q.Criteria.Conditions)
            {
                var name = condition.AttributeName;
                var test = condition.Values.Single();
                filtered = filtered.Where(e =>
                    e.Contains(name) &&
                    (e[name] is EntityReference reference
                        ? Equals(reference.Id, test)
                        : e[name] is OptionSetValue choice
                            ? Equals(choice.Value, test)
                            : Equals(e[name], test)));
            }
            var result = new EntityCollection();
            foreach (var e in filtered) result.Entities.Add(e);
            return result;
        }
        public Guid Create(Entity row)
        {
            RequireIssuedTicket("Create", row);
            Writes.Add("Create:" + row.LogicalName);
            var id = row.Id != Guid.Empty ? row.Id : Guid.NewGuid();
            row.Id = id;
            if (row.LogicalName == "fmg_cashgrantrequest")
                row["createdon"] = Now;
            Rows.Add(id, row);
            return id;
        }
        public OrganizationResponse Execute(OrganizationRequest input)
        {
            if (input is RetrieveUserPrivilegeByPrivilegeNameRequest privilege)
            {
                LastPrivilege = privilege.PrivilegeName;
                Assert.True(privilege.UserId == Maker || privilege.UserId == Reviewer ||
                    privilege.UserId == Recipient);
                var permitted = privilege.PrivilegeName ==
                    DataverseGrantReviewPrivileges.ProposePrivilege ? AllowPropose :
                    privilege.PrivilegeName == DataverseGrantReviewPrivileges.DecidePrivilege &&
                    AllowDecide;
                var response = new RetrieveUserPrivilegeByPrivilegeNameResponse();
                response.Results["RolePrivileges"] = permitted
                    ? new[] { new RolePrivilege
                        {
                            PrivilegeName = privilege.PrivilegeName,
                            PrivilegeId = Guid.NewGuid(),
                            Depth = privilege.PrivilegeName ==
                                DataverseGrantReviewPrivileges.DecidePrivilege && !ApproveGlobal
                                    ? PrivilegeDepth.Basic : PrivilegeDepth.Global
                        } }
                    : Array.Empty<RolePrivilege>();
                return response;
            }
            if (input is UpdateRequest update)
            {
                Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches,
                    update.ConcurrencyBehavior);
                Assert.Equal("39", update.Target.RowVersion);
                if (RejectVersionedUpdate)
                    throw new InvalidOperationException("Simulated optimistic concurrency collision.");
                RequireIssuedTicket("Update", update.Target);
                Writes.Add("ETagUpdate:" + update.Target.LogicalName);
                foreach (var pair in update.Target.Attributes)
                    Rows[update.Target.Id][pair.Key] = pair.Value;
                return new UpdateResponse();
            }
            throw new InvalidOperationException("Unexpected SDK request.");
        }
        public void Update(Entity entity) =>
            throw new InvalidOperationException("Unversioned update forbidden.");
        public void Delete(string name, Guid id) =>
            throw new InvalidOperationException("Delete forbidden.");
        public void Associate(string name, Guid id, Relationship relationship,
            EntityReferenceCollection references) =>
            throw new InvalidOperationException("Associate forbidden.");
        public void Disassociate(string name, Guid id, Relationship relationship,
            EntityReferenceCollection references) =>
            throw new InvalidOperationException("Disassociate forbidden.");
    }

    private static Guid Submit(Fake db, CashGrantChangeRequest request)
        => new DataverseGrantReviewWorkflow(db).Submit(Runtime(Maker), request, Now);

    [Fact]
    public void TwoDifferentPlatformCallersMintOnlyTheirOwnScopedReviewWrites()
    {
        var db = new Fake();
        var submission = new RemoteExecutionContext
        {
            MessageName = F5_GrantReviewEndpoint.SubmitMessage,
            Stage = 30, Mode = 0, Depth = 1, IsInTransaction = true,
            UserId = Maker, InitiatingUserId = Maker,
            CorrelationId = Guid.NewGuid(), OperationId = Guid.Empty
        };
        db.TrustedParent = submission;
        var planned = Change();
        var id = new DataverseGrantReviewWorkflow(db, submission)
            .Submit(Runtime(Maker), planned, Now);
        Assert.Equal(1, db.ScopedWrites);
        var decision = new RemoteExecutionContext
        {
            MessageName = F5_GrantReviewEndpoint.DecideMessage,
            Stage = 30, Mode = 0, Depth = 1, IsInTransaction = true,
            UserId = Reviewer, InitiatingUserId = Reviewer,
            CorrelationId = Guid.NewGuid(), OperationId = Guid.Empty
        };
        db.TrustedParent = decision;
        new DataverseGrantReviewWorkflow(db, decision)
            .Decide(Runtime(Reviewer), id, true, Now.AddMinutes(5));
        Assert.Equal(3, db.ScopedWrites); // grant create + conditional request update
        Assert.Equal(797283001,
            db.Rows[id].GetAttributeValue<OptionSetValue>("fmg_reviewstatus").Value);
        Assert.Single(db.Rows.Values, x => x.LogicalName == "fmg_cashaccessgrant");
    }

    [Fact]
    public void ProposalWritesAuditablePendingHeaderUnderInitiatingUser()
    {
        var db = new Fake();
        var request = Change();
        var id = Submit(db, request);
        Assert.Equal(request.RequestId, id);
        var e = db.Rows[id];
        Assert.Equal("fmg_cashgrantrequest", e.LogicalName);
        Assert.Equal(new CashGrantChangeRequest(id, Recipient, Account,
                CashOperationKind.InternalTransfer, CashGrantChangeKind.Grant,
                false, request.Reason, request.ExpiresUtc).RequestId, id);
        Assert.Equal(Maker,
            e.GetAttributeValue<EntityReference>("fmg_proposer").Id);
        Assert.Equal(Recipient,
            e.GetAttributeValue<EntityReference>("fmg_beneficiary").Id);
        Assert.Equal(797283000,
            e.GetAttributeValue<OptionSetValue>("fmg_reviewstatus").Value);
        Assert.Equal(DataverseGrantReviewPrivileges.ProposePrivilege, db.LastPrivilege);
        Assert.Equal(new[] { "Create:fmg_cashgrantrequest" }, db.Writes);
    }

    [Fact]
    public void ApprovalGeneratesOneActiveGrantAndVersionedDecision()
    {
        var db = new Fake();
        var id = Submit(db, Change());
        new DataverseGrantReviewWorkflow(db).Decide(Runtime(Reviewer), id, true,
            Now.AddMinutes(5));
        Assert.Equal(DataverseGrantReviewPrivileges.DecidePrivilege, db.LastPrivilege);
        Assert.Equal(1, db.Rows.Values.Count(e => e.LogicalName == "fmg_cashaccessgrant"));
        var grant = db.Rows.Values.Single(e => e.LogicalName == "fmg_cashaccessgrant");
        Assert.True(grant.GetAttributeValue<bool>("fmg_isactive"));
        Assert.Equal(Recipient, grant.GetAttributeValue<EntityReference>("fmg_principal").Id);
        Assert.Equal(Account, grant.GetAttributeValue<EntityReference>("fmg_cashaccount").Id);
        Assert.Equal(797282001, grant.GetAttributeValue<OptionSetValue>("fmg_operation").Value);
        Assert.Equal(797283001,
            db.Rows[id].GetAttributeValue<OptionSetValue>("fmg_reviewstatus").Value);
        Assert.Equal(Reviewer,
            db.Rows[id].GetAttributeValue<EntityReference>("fmg_approver").Id);
        Assert.Contains("ETagUpdate:fmg_cashgrantrequest", db.Writes);
    }

    [Fact]
    public void RejectionChangesOnlyReviewDecisionNeverIssuesGrant()
    {
        var db = new Fake();
        var id = Submit(db, Change());
        new DataverseGrantReviewWorkflow(db).Decide(Runtime(Reviewer), id, false,
            Now.AddMinutes(2));
        Assert.Equal(0, db.Rows.Values.Count(x => x.LogicalName == "fmg_cashaccessgrant"));
        Assert.Equal(797283002,
            db.Rows[id].GetAttributeValue<OptionSetValue>("fmg_reviewstatus").Value);
    }

    [Fact]
    public void UnprivilegedMakerCannotSubmitAndDisqualifiedApproverCannotDecide()
    {
        var db = new Fake { AllowPropose = false };
        Assert.Equal("F5Q010", Assert.Throws<CashPlanningException>(() =>
            Submit(db, Change())).Code);
        Assert.Empty(db.Writes);
        db = new Fake();
        var id = Submit(db, Change());
        db.AllowDecide = false;
        Assert.Equal("F5Q010", Assert.Throws<CashPlanningException>(() =>
            new DataverseGrantReviewWorkflow(db).Decide(Runtime(Reviewer), id, true,
                Now.AddMinutes(1))).Code);
        Assert.DoesNotContain(db.Rows.Values, e => e.LogicalName == "fmg_cashaccessgrant");
    }

    [Fact]
    public void NonGlobalReviewerPrivilegeCannotApproveOtherPeoplesCashAccess()
    {
        var db = new Fake();
        var id = Submit(db, Change());
        db.ApproveGlobal = false;
        Assert.Equal("F5Q010", Assert.Throws<CashPlanningException>(() =>
            new DataverseGrantReviewWorkflow(db).Decide(Runtime(Reviewer), id, true,
                Now.AddMinutes(1))).Code);
        Assert.DoesNotContain(db.Rows.Values, e => e.LogicalName == "fmg_cashaccessgrant");
    }

    [Fact]
    public void SelfApprovalAndBeneficiaryApprovalCannotWriteGrant()
    {
        var db = new Fake();
        var id = Submit(db, Change());
        foreach (var user in new[] { Maker, Recipient })
            Assert.Equal("F5A011", Assert.Throws<CashPlanningException>(() =>
                new DataverseGrantReviewWorkflow(db).Decide(Runtime(user), id, true,
                    Now.AddMinutes(1))).Code);
        Assert.DoesNotContain(db.Rows.Values, e => e.LogicalName == "fmg_cashaccessgrant");
    }

    [Fact]
    public void WrongTransactionOrDisabledBeneficiaryCannotSubmit()
    {
        var db = new Fake();
        var op = Change();
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            new DataverseGrantReviewWorkflow(db).Submit(
                new CashRuntimeEvidence(30, 0, false, Maker, Maker), op, Now)).Code);
        db.DisabledUser = true;
        Assert.Equal("F5Q010", Assert.Throws<CashPlanningException>(() =>
            Submit(db, op)).Code);
        Assert.Empty(db.Writes);
    }

    [Fact]
    public void AppAndNonInteractiveAccountsCannotCreateOrReceiveCashGrants()
    {
        foreach (var db in new[] {
            new Fake { NonInteractiveUser = true },
            new Fake { AppUser = true } })
        {
            Assert.Equal("F5Q010", Assert.Throws<CashPlanningException>(() =>
                Submit(db, Change())).Code);
            Assert.Empty(db.Writes);
        }
        foreach (var db in new[] {
            new Fake { NonInteractiveRecipient = true },
            new Fake { AppRecipient = true } })
        {
            Assert.Equal("F5Q020", Assert.Throws<CashPlanningException>(() =>
                Submit(db, Change())).Code);
            Assert.Empty(db.Writes);
        }
    }

    [Fact]
    public void InactiveCashAccountOrBranchCannotReceiveGrant()
    {
        foreach (var db in new[] {
            new Fake { InactiveAccount = true },
            new Fake { InactiveBranch = true } })
        {
            Assert.Equal("F5Q020", Assert.Throws<CashPlanningException>(() =>
                Submit(db, Change())).Code);
            Assert.Empty(db.Writes);
        }
    }

    [Fact]
    public void RevocationRequiresPreviouslyActiveGrantAndVersionedMutation()
    {
        var db = new Fake();
        var grant = new Entity("fmg_cashaccessgrant", Guid.NewGuid());
        grant["fmg_principal"] = new EntityReference("systemuser", Recipient);
        grant["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", Account);
        grant["fmg_operation"] = new OptionSetValue(797282001);
        grant["fmg_isactive"] = true;
        grant.RowVersion = "39";
        db.Rows[grant.Id] = grant;
        var id = Submit(db, Change(CashGrantChangeKind.Revoke));
        new DataverseGrantReviewWorkflow(db).Decide(Runtime(Reviewer), id, true,
            Now.AddMinutes(3));
        Assert.False(db.Rows[grant.Id].GetAttributeValue<bool>("fmg_isactive"));
        Assert.Contains("ETagUpdate:fmg_cashaccessgrant", db.Writes);
    }

    [Fact]
    public void DuplicateApprovalCannotOverwriteAlreadyApprovedRequest()
    {
        var db = new Fake();
        var id = Submit(db, Change());
        new DataverseGrantReviewWorkflow(db).Decide(Runtime(Reviewer), id, true,
            Now.AddMinutes(2));
        Assert.Equal("F5Q001", Assert.Throws<CashPlanningException>(() =>
            new DataverseGrantReviewWorkflow(db).Decide(Runtime(Reviewer), id, true,
                Now.AddMinutes(3))).Code);
    }

    [Fact]
    public void ConditionalRequestUpdateNeverUsesUnversionedWrite()
    {
        var db = new Fake();
        var id = Submit(db, Change());
        db.RejectVersionedUpdate = true;
        Assert.Throws<InvalidOperationException>(() =>
            new DataverseGrantReviewWorkflow(db).Decide(Runtime(Reviewer), id,
                false, Now.AddMinutes(5)));
        Assert.Equal(797283000,
            db.Rows[id].GetAttributeValue<OptionSetValue>("fmg_reviewstatus").Value);
    }
}
