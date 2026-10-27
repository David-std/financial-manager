using System;
using FinancialManager.CashDomain;
using FinancialManager.CashOperations;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

public sealed class NestedGrantReviewGuardTests
{
    private static readonly Guid SubmitPin = Guid.Parse("10000000-0000-4000-8000-000000000501");
    private static readonly Guid DecidePin = Guid.Parse("10000000-0000-4000-8000-000000000502");
    private static readonly Guid Maker = Guid.Parse("10000000-0000-4000-8000-000000000503");
    private static readonly Guid Reviewer = Guid.Parse("10000000-0000-4000-8000-000000000504");
    private static readonly Guid Recipient = Guid.Parse("10000000-0000-4000-8000-000000000505");
    private static readonly Guid Account = Guid.Parse("10000000-0000-4000-8000-000000000506");
    private static readonly Guid Correlation = Guid.Parse("10000000-0000-4000-8000-000000000507");
    private static readonly Guid RequestId = Guid.Parse("10000000-0000-4000-8000-000000000508");
    private static readonly DateTime Created = DateTime.UtcNow.AddMinutes(-2);
    private static readonly DateTime Expiry = DateTime.UtcNow.AddDays(7);
    private static readonly CashGrantChangeRequest Request = new CashGrantChangeRequest(
        RequestId, Recipient, Account, CashOperationKind.InternalTransfer,
        CashGrantChangeKind.Grant, false, "Synthetic audit justification for cash grant", Expiry);

    private static RemoteExecutionContext Parent(bool decision = false, Guid? user = null)
    {
        var actor = user ?? (decision ? Reviewer : Maker);
        var parent = new RemoteExecutionContext
        {
            Stage = 30, Mode = 0, Depth = 1, IsInTransaction = true,
            MessageName = decision ? F5_GrantReviewEndpoint.DecideMessage :
                F5_GrantReviewEndpoint.SubmitMessage,
            OwningExtension = new EntityReference("sdkmessageprocessingstep",
                decision ? DecidePin : SubmitPin),
            UserId = actor, InitiatingUserId = actor, CorrelationId = Correlation
        };
        if (decision)
        {
            parent.InputParameters["RequestId"] = RequestId;
            parent.InputParameters["Approved"] = true;
        }
        else
        {
            parent.InputParameters["RequestId"] = RequestId;
            parent.InputParameters["Beneficiary"] = new EntityReference("systemuser", Recipient);
            parent.InputParameters["CashAccount"] = new EntityReference("fmg_cashaccount", Account);
            parent.InputParameters["Operation"] = new OptionSetValue(797282001);
            parent.InputParameters["ChangeKind"] = new OptionSetValue(797283100);
            parent.InputParameters["AllowShared"] = false;
            parent.InputParameters["Reason"] = Request.Reason;
            parent.InputParameters["ExpiresAtUtc"] = Expiry;
        }
        return parent;
    }

    private static Entity Pending()
    {
        var prepared = CashGrantApprovalPolicy.Prepare(Request, Maker, Created);
        var row = new Entity("fmg_cashgrantrequest", RequestId) { RowVersion = "12" };
        row["fmg_name"] = "F5-review-" + RequestId.ToString("N");
        row["fmg_externalrequestid"] = RequestId.ToString("D");
        row["fmg_requestfingerprint"] = prepared.Fingerprint;
        row["fmg_proposer"] = new EntityReference("systemuser", Maker);
        row["fmg_beneficiary"] = new EntityReference("systemuser", Recipient);
        row["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", Account);
        row["fmg_operation"] = new OptionSetValue(797282001);
        row["fmg_changekind"] = new OptionSetValue(797283100);
        row["fmg_allowshared"] = false;
        row["fmg_reason"] = Request.Reason;
        row["fmg_expiresatutc"] = Expiry;
        row["fmg_reviewstatus"] = new OptionSetValue(797283000);
        row["createdon"] = Created;
        return row;
    }

    private static Entity Submission()
    {
        var row = Pending();
        row.Attributes.Remove("createdon");
        return row;
    }

    private static Entity Grant()
    {
        var row = new Entity("fmg_cashaccessgrant", Guid.NewGuid());
        row["fmg_name"] = "Approved F5 " + RequestId.ToString("N");
        row["fmg_principal"] = new EntityReference("systemuser", Recipient);
        row["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", Account);
        row["fmg_operation"] = new OptionSetValue(797282001);
        row["fmg_isactive"] = true;
        row["fmg_allowshared"] = false;
        row["fmg_expiresatutc"] = Expiry;
        return row;
    }

    private static RemoteExecutionContext Child(Entity row, bool decision = false)
    {
        var parent = Parent(decision);
        var child = new RemoteExecutionContext
        {
            Stage = 10, Mode = 0, Depth = 2, IsInTransaction = true,
            ParentContext = parent, MessageName = "Create",
            PrimaryEntityName = row.LogicalName,
            UserId = parent.UserId, InitiatingUserId = parent.InitiatingUserId,
            CorrelationId = parent.CorrelationId
        };
        child.InputParameters["Target"] = row;
        return child;
    }

    private sealed class ReadOnly : IOrganizationService
    {
        public bool GrantReviewer = true;
        public Entity? ExistingGrant;
        public int Reads;
        public int Writes;
        public Entity Retrieve(string name, Guid id, ColumnSet columns)
        {
            Reads++;
            if (name == "fmg_cashgrantrequest") return Pending();
            if (name == "fmg_cashaccessgrant" && ExistingGrant?.Id == id)
                return ExistingGrant;
            if (name == "systemuser")
            {
                var row = new Entity(name,id);
                row["isdisabled"] = false;
                row["accessmode"] = new OptionSetValue(0);
                return row;
            }
            throw new Exception("Unexpected query " + name);
        }
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveUserPrivilegeByPrivilegeNameRequest privilege)
            {
                var response = new RetrieveUserPrivilegeByPrivilegeNameResponse();
                response.Results["RolePrivileges"] = !GrantReviewer &&
                    privilege.PrivilegeName == DataverseGrantReviewPrivileges.DecidePrivilege
                    ? Array.Empty<RolePrivilege>()
                    : new[] { new RolePrivilege {
                        PrivilegeName = privilege.PrivilegeName,
                        PrivilegeId = Guid.NewGuid(), Depth = PrivilegeDepth.Global
                    } };
                return response;
            }
            Writes++;
            throw new Exception("Guard must not mutate Dataverse");
        }
        public EntityCollection RetrieveMultiple(QueryBase query)
        { Reads++; return new EntityCollection(); }
        public Guid Create(Entity entity) { Writes++; throw new Exception("write"); }
        public void Update(Entity entity) { Writes++; throw new Exception("write"); }
        public void Delete(string table, Guid id) { Writes++; throw new Exception("write"); }
        public void Associate(string table, Guid id, Relationship relationship,
            EntityReferenceCollection references) { Writes++; throw new Exception("write"); }
        public void Disassociate(string table, Guid id, Relationship relationship,
            EntityReferenceCollection references) { Writes++; throw new Exception("write"); }
    }

    private static void Admit(RemoteExecutionContext ctx, ReadOnly store) =>
        F5_ValidatedNestedReviewGuard.Require(ctx, SubmitPin, DecidePin, store);

    [Fact]
    public void ExactSubmissionAndDistinctApprovalGrantAreReadOnlyAdmissible()
    {
        var db = new ReadOnly();
        Admit(Child(Submission()), db);
        Admit(Child(Grant(), true), db);
        Assert.Equal(0, db.Writes);
    }

    [Fact]
    public void DirectTagOrFalseServerPinCannotAuthorizeGrant()
    {
        var db = new ReadOnly();
        var direct = Child(Submission());
        direct.ParentContext = null;
        direct.SharedVariables["tag"] = "trusted";
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(direct, db));
        var forged = Child(Grant(), true);
        forged.ParentContext.OwningExtension =
            new EntityReference("sdkmessageprocessingstep", SubmitPin);
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(forged, db));
        Assert.Equal(0, db.Writes);
    }

    [Fact]
    public void ExtraFieldWrongFingerprintOrMissingReviewPrivilegeDeny()
    {
        var db = new ReadOnly();
        var proposal = Submission();
        proposal["fmg_approver"] = new EntityReference("systemuser", Maker);
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(Child(proposal), db));
        proposal = Submission();
        proposal["fmg_requestfingerprint"] = new string('A', 64);
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(Child(proposal), db));
        var grant = Child(Grant(), true);
        db.GrantReviewer = false;
        Assert.Throws<CashPlanningException>(() => Admit(grant, db));
        Assert.Equal(0, db.Writes);
    }

    [Fact]
    public void SelfApprovalAndUnreviewedGrantAndDeleteAlwaysFail()
    {
        var db = new ReadOnly();
        var self = Child(Grant(), true);
        self.ParentContext.UserId = Maker;
        self.ParentContext.InitiatingUserId = Maker;
        self.UserId = Maker;
        self.InitiatingUserId = Maker;
        Assert.Throws<CashPlanningException>(() => Admit(self, db));
        var unreviewed = Child(Grant());
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(unreviewed, db));
        var deletion = Child(Submission());
        deletion.MessageName = "Delete";
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(deletion, db));
        Assert.Equal(0, db.Writes);
    }

    [Fact]
    public void DeniedDecisionCannotCreateGrant()
    {
        var db = new ReadOnly();
        var grant = Child(Grant(), true);
        grant.ParentContext.InputParameters["Approved"] = false;
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(grant, db));
    }

    [Fact]
    public void VersionedDecisionUpdateIsAdmittedButTamperingIsRejected()
    {
        var db = new ReadOnly();
        var target = new Entity("fmg_cashgrantrequest", RequestId) { RowVersion = "12" };
        target["fmg_reviewstatus"] = new OptionSetValue(797283001);
        target["fmg_approver"] = new EntityReference("systemuser", Reviewer);
        target["fmg_decidedatutc"] = DateTime.UtcNow;
        var ctx = Child(target, true);
        ctx.MessageName = "Update";
        Admit(ctx, db);
        Assert.Equal(0, db.Writes);
        target["fmg_approver"] = new EntityReference("systemuser", Maker);
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(ctx, db));
        target["fmg_approver"] = new EntityReference("systemuser", Reviewer);
        target.RowVersion = "old";
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(ctx, db));
    }

    [Fact]
    public void InactiveGrantReactivationNeedsExactCurrentETagAndIdentity()
    {
        var db = new ReadOnly();
        var current = Grant();
        current.RowVersion = "14";
        current["fmg_isactive"] = false;
        db.ExistingGrant = current;
        var target = new Entity("fmg_cashaccessgrant", current.Id) { RowVersion = "14" };
        target["fmg_isactive"] = true;
        target["fmg_allowshared"] = false;
        target["fmg_expiresatutc"] = Expiry;
        var ctx = Child(target, true);
        ctx.MessageName = "Update";
        Admit(ctx, db);
        Assert.Equal(0, db.Writes);
        target.RowVersion = "13";
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(ctx, db));
        target.RowVersion = "14";
        current["fmg_principal"] = new EntityReference("systemuser", Maker);
        Assert.Throws<InvalidPluginExecutionException>(() => Admit(ctx, db));
    }

    [Fact]
    public void ConstructorRejectsUnpinnedOrReusedReviewSteps()
    {
        foreach (var value in new[] { "", "invalid", SubmitPin.ToString("D"),
            SubmitPin.ToString("D") + "|" + SubmitPin.ToString("D") })
            Assert.Throws<InvalidPluginExecutionException>(() =>
                new F5_ValidatedNestedReviewGuard(value, ""));
        Assert.NotNull(new F5_ValidatedNestedReviewGuard(
            SubmitPin.ToString("D") + "|" + DecidePin.ToString("D"), ""));
    }
}
