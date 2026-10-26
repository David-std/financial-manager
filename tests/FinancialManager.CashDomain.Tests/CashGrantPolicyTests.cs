using System;
using System.Collections.Generic;
using FinancialManager.CashDomain;
using Xunit;

public sealed class CashGrantPolicyTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Branch = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid AccountA = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid AccountB = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Shared = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly DateTime Date = new DateTime(2026, 10, 26);
    private static CashAccountSnapshot A(bool active = true, bool? branchActive = true)
        => new CashAccountSnapshot(AccountA, Branch, active, branchActive);
    private static CashAccountSnapshot B() => new CashAccountSnapshot(AccountB, Branch, true, true);
    private static CashAccountSnapshot S() => new CashAccountSnapshot(Shared, null, true, null);
    private static CashGrantSnapshot Grant(Guid account, CashOperationKind kind,
        bool active = true, bool shared = false, Guid? user = null, Guid? grantId = null)
        => new CashGrantSnapshot(grantId ?? Guid.NewGuid(), user ?? User,
            account, kind, active, shared);
    private static TrustedCashActor Actor(CashAccountSnapshot[] accounts,
        params CashGrantSnapshot[] grants) => CashGrantPolicy.BuildActor(User, accounts, grants);
    private static CashOperationRequest Transfer(Guid from, Guid to)
        => new CashOperationRequest(CashOperationKind.InternalTransfer, "synthetic-transfer",
            from, to, 5.25m, Date, "PEN", "synthetic");
    private static CashOperationRequest Receipt(Guid account)
        => new CashOperationRequest(CashOperationKind.SettleReceipt, "synthetic-receipt",
            account, null, 5.25m, Date, "PEN", "synthetic-document");
    private static string Reject(CashOperationRequest req, TrustedCashActor actor) =>
        Assert.Throws<CashPlanningException>(() => CashOperationPolicy.Prepare(req, actor)).Code;

    [Fact]
    public void TwoAccountTransferRequiresTheSameOperationGrantOnBothAccounts()
    {
        var actor = Actor(new[] { A(), B() },
            Grant(AccountA, CashOperationKind.InternalTransfer),
            Grant(AccountB, CashOperationKind.SettleReceipt));
        Assert.Equal("F5P002", Reject(Transfer(AccountA, AccountB), actor));
        Assert.True(actor.CanPerform(CashOperationKind.InternalTransfer));
        Assert.True(actor.CanAccessAccount(AccountB));
        Assert.False(actor.CanPerformOnAccount(CashOperationKind.InternalTransfer, AccountB));
    }

    [Fact]
    public void ExactIndependentTransferGrantsPermitTwoLegs()
    {
        var actor = Actor(new[] { A(), B() },
            Grant(AccountA, CashOperationKind.InternalTransfer),
            Grant(AccountB, CashOperationKind.InternalTransfer));
        var plan = CashOperationPolicy.Prepare(Transfer(AccountA, AccountB), actor);
        Assert.Equal(2, plan.Legs.Count);
        Assert.Equal(0m, plan.Legs[0].SignedAmountPen + plan.Legs[1].SignedAmountPen);
    }

    [Fact]
    public void AnAccountWithNoBranchNeedsAnExplicitSharedGrant()
    {
        var noShared = Actor(new[] { S() },
            Grant(Shared, CashOperationKind.SettleReceipt));
        Assert.Equal("F5P002", Reject(Receipt(Shared), noShared));
        var shared = Actor(new[] { S() },
            Grant(Shared, CashOperationKind.SettleReceipt, shared: true));
        Assert.Single(CashOperationPolicy.Prepare(Receipt(Shared), shared).Legs);
    }

    [Fact]
    public void SharedFlagCannotExpandABranchAccount()
    {
        var actor = Actor(new[] { A() },
            Grant(AccountA, CashOperationKind.SettleReceipt, shared: true));
        Assert.Equal("F5P002", Reject(Receipt(AccountA), actor));
    }

    [Fact]
    public void DisabledOrInactiveBranchNeverGrantsCash()
    {
        foreach (var account in new[] { A(false), A(true, false), A(true, null) })
            Assert.Equal("F5P002", Reject(Receipt(AccountA),
                Actor(new[] { account }, Grant(AccountA, CashOperationKind.SettleReceipt))));
    }

    [Fact]
    public void InactiveOrForeignUserGrantIsNotPermission()
    {
        Assert.Equal("F5P002", Reject(Receipt(AccountA),
            Actor(new[] { A() }, Grant(AccountA, CashOperationKind.SettleReceipt, active: false))));
        Assert.Equal("F5P002", Reject(Receipt(AccountA),
            Actor(new[] { A() }, Grant(AccountA, CashOperationKind.SettleReceipt, user: Other))));
    }

    [Fact]
    public void ReversalPermissionIsAccountSpecific()
    {
        var actor = Actor(new[] { A(), B() },
            Grant(AccountA, CashOperationKind.Reversal),
            Grant(AccountB, CashOperationKind.InternalTransfer));
        var original = new PostedCashOperation(Guid.NewGuid(), CashOperationKind.InternalTransfer,
            new[]
            {
                new PostedCashLeg(Guid.NewGuid(), AccountA, -5m, Date),
                new PostedCashLeg(Guid.NewGuid(), AccountB, 5m, Date)
            }, true, false);
        Assert.Equal("F5R002", Assert.Throws<CashPlanningException>(() =>
            CashReversalPolicy.Prepare(original,
                new CashReversalCommand("reverse-1", Date, "synthetic correction"), actor)).Code);
    }

    [Fact]
    public void DuplicateOrMalformedGrantsFailClosed()
    {
        var grant = Grant(AccountA, CashOperationKind.SettleReceipt);
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            Actor(new[] { A() }, grant, grant)).Code);
        var other = Grant(AccountA, CashOperationKind.SettleReceipt);
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            Actor(new[] { A() }, grant, other)).Code);
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            CashGrantPolicy.BuildActor(Guid.Empty, new[] { A() }, new[] { grant })).Code);
    }

    [Fact]
    public void GrantCannotEnableAReferencedAccountMissingFromServer()
    {
        Assert.Equal("F5P002", Reject(Receipt(AccountB),
            Actor(new[] { A() }, Grant(AccountB, CashOperationKind.SettleReceipt))));
    }
}
