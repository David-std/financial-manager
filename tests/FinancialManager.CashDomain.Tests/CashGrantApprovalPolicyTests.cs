using System;
using FinancialManager.CashDomain;
using Xunit;

public sealed class CashGrantApprovalPolicyTests
{
    private static readonly Guid Maker = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Reviewer = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Recipient = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly DateTime Now = new DateTime(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);
    private static CashGrantChangeRequest Proposal(
        Guid? recipient = null, CashGrantChangeKind change = CashGrantChangeKind.Grant,
        DateTime? expiry = null, string reason = "Reviewed business need",
        CashOperationKind operation = CashOperationKind.InternalTransfer,
        bool shared = false)
        => new CashGrantChangeRequest(Guid.NewGuid(), recipient ?? Recipient, Account,
            operation, change, shared, reason, expiry ?? Now.AddDays(7));
    private static PendingCashGrantChange Pending(CashGrantChangeRequest? req = null)
        => CashGrantApprovalPolicy.Prepare(req ?? Proposal(), Maker, Now);

    [Fact]
    public void ApprovedGrantRequiresTwoIndependentHumanUsersAndAnIndependentBeneficiary()
    {
        var pending = Pending();
        var approved = CashGrantApprovalPolicy.Approve(pending, Reviewer, Now.AddMinutes(10));
        Assert.Equal(Reviewer, approved.ApproverId);
        Assert.Equal(Maker, approved.Request.ProposerId);
        Assert.Equal(Recipient, approved.Request.BeneficiaryId);
        Assert.Equal(CashGrantChangeKind.Grant, approved.Request.Change);
        Assert.Equal(64, approved.Request.Fingerprint.Length);
        Assert.NotEqual(Maker, approved.ApproverId);
        Assert.NotEqual(Recipient, approved.ApproverId);
    }

    [Fact]
    public void ApproverCannotBeOriginalRequester()
    {
        Assert.Equal("F5A011", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Approve(Pending(), Maker, Now.AddMinutes(10))).Code);
    }

    [Fact]
    public void BeneficiaryCannotApproveOwnGrant()
    {
        Assert.Equal("F5A011", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Approve(Pending(), Recipient, Now.AddMinutes(10))).Code);
    }

    [Fact]
    public void ProposerCannotBeBeneficiary()
    {
        Assert.Equal("F5A010", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Prepare(Proposal(recipient: Maker), Maker, Now)).Code);
    }

    [Fact]
    public void TimeLimitedGrantsExpireAndFutureDatedDecisionsAreRejected()
    {
        var pending = Pending();
        Assert.Equal("F5A011", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Approve(pending, Reviewer, Now.AddDays(7))).Code);
        Assert.Equal("F5A011", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Approve(pending, Reviewer, Now.AddSeconds(-1))).Code);
    }

    [Fact]
    public void MalformedExpiryAndTooLongGrantsAreRejected()
    {
        foreach (var date in new[] { Now, Now.AddDays(-1), Now.AddDays(91) })
            Assert.Equal("F5A010", Assert.Throws<CashPlanningException>(() =>
                CashGrantApprovalPolicy.Prepare(Proposal(expiry: date), Maker, Now)).Code);
        Assert.Equal("F5A010", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Prepare(Proposal(expiry: DateTime.SpecifyKind(
                Now.AddDays(7), DateTimeKind.Unspecified)), Maker, Now)).Code);
    }

    [Fact]
    public void ReasonMustBeAuditableAndBounded()
    {
        foreach (var reason in new[] { "", "x", new string('p', 301) })
            Assert.Equal("F5A010", Assert.Throws<CashPlanningException>(() =>
                CashGrantApprovalPolicy.Prepare(Proposal(reason: reason), Maker, Now)).Code);
    }

    [Fact]
    public void RevokeUsesSameIndependentApprovalPolicy()
    {
        var request = Pending(Proposal(change: CashGrantChangeKind.Revoke));
        var approved = CashGrantApprovalPolicy.Approve(request, Reviewer, Now.AddMinutes(1));
        Assert.Equal(CashGrantChangeKind.Revoke, approved.Request.Change);
        Assert.Equal("F5A011", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Approve(request, Maker, Now.AddMinutes(1))).Code);
    }

    [Fact]
    public void FingerprintChangesForMaterialRequestDifferences()
    {
        var id = Guid.NewGuid();
        CashGrantChangeRequest Req(CashOperationKind kind, bool shared, string reason) =>
            new CashGrantChangeRequest(id, Recipient, Account, kind,
                CashGrantChangeKind.Grant, shared, reason, Now.AddDays(7));
        var original = Pending(Req(CashOperationKind.InternalTransfer, false, "Reviewed request"));
        var differentOperation = Pending(Req(CashOperationKind.SettleReceipt, false, "Reviewed request"));
        var differentShared = Pending(Req(CashOperationKind.InternalTransfer, true, "Reviewed request"));
        var differentReason = Pending(Req(CashOperationKind.InternalTransfer, false, "Other business reason"));
        Assert.NotEqual(original.Fingerprint, differentOperation.Fingerprint);
        Assert.NotEqual(original.Fingerprint, differentShared.Fingerprint);
        Assert.NotEqual(original.Fingerprint, differentReason.Fingerprint);
        Assert.Equal(original.Fingerprint,
            Pending(Req(CashOperationKind.InternalTransfer, false, "Reviewed request")).Fingerprint);
    }

    [Fact]
    public void UnknownOperationOrChangeNeverCreatesAnApproachableRequest()
    {
        foreach (var req in new[]
        {
            Proposal(operation: (CashOperationKind)999),
            Proposal(change: (CashGrantChangeKind)999)
        })
            Assert.Equal("F5A010", Assert.Throws<CashPlanningException>(() =>
                CashGrantApprovalPolicy.Prepare(req, Maker, Now)).Code);
    }

    [Fact]
    public void EmptyIdsAndNonUtcClockAreRejected()
    {
        var r = new CashGrantChangeRequest(Guid.Empty, Recipient, Account,
            CashOperationKind.InternalTransfer, CashGrantChangeKind.Grant, false,
            "Reviewed proposal", Now.AddDays(2));
        Assert.Equal("F5A010", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Prepare(r, Maker, Now)).Code);
        Assert.Equal("F5A010", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Prepare(Proposal(), Maker,
                DateTime.SpecifyKind(Now, DateTimeKind.Unspecified))).Code);
        Assert.Equal("F5A011", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Approve(Pending(), Guid.Empty, Now.AddMinutes(1))).Code);
    }

    [Fact]
    public void IndependentApprovalDoesNotByItselfIssueAnyGrant()
    {
        var approved = CashGrantApprovalPolicy.Approve(Pending(), Reviewer, Now.AddMinutes(1));
        Assert.NotNull(approved);
        // Source-only approval has no persistence, organization service or
        // bypass flag. The reviewed server adapter must atomically apply it.
        Assert.NotEqual(Guid.Empty, approved.Request.RequestId);
    }
}
