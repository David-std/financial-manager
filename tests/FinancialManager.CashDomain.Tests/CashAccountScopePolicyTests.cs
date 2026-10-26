using System;
using System.Collections.Generic;
using FinancialManager.CashDomain;
using Xunit;

public sealed class CashAccountScopePolicyTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AccountA = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid AccountB = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid BranchA = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid BranchB = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateTime Day = new DateTime(2026, 10, 26);

    private static TrustedCashActor Build(
        CashAccountSnapshot[] accounts,
        Guid[]? branches = null,
        Guid[]? shared = null,
        CashOperationKind[]? operations = null,
        Guid? subjectId = null)
        => CashAccountScopePolicy.BuildActor(subjectId ?? Actor, accounts,
            branches ?? new[] { BranchA }, shared ?? Array.Empty<Guid>(),
            operations ?? new[] { CashOperationKind.SettleReceipt, CashOperationKind.InternalTransfer });

    private static CashOperationRequest Receipt(Guid account)
        => new CashOperationRequest(CashOperationKind.SettleReceipt, "synthetic-1",
            account, null, 10m, Day, "PEN", "synthetic-invoice");

    private static CashOperationRequest Transfer()
        => new CashOperationRequest(CashOperationKind.InternalTransfer, "synthetic-2",
            AccountA, AccountB, 10m, Day, "PEN", "internal");

    private static CashAccountSnapshot Account(Guid accountId, Guid? branchId,
        bool active = true, bool? branchActive = true)
        => new CashAccountSnapshot(accountId, branchId, active, branchActive);

    [Fact]
    public void ActiveAccountWithinVerifiedBranchIsAllowed()
    {
        var actor = Build(new[] { Account(AccountA, BranchA) });
        Assert.Single(CashOperationPolicy.Prepare(Receipt(AccountA), actor).Legs);
    }

    [Fact]
    public void AForeignBranchIsNeverGrantedByOrganizationOwnership()
    {
        var actor = Build(new[] { Account(AccountA, BranchB) });
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Receipt(AccountA), actor)).Code);
    }

    [Fact]
    public void InactiveAccountIsDenied()
    {
        var actor = Build(new[] { Account(AccountA, BranchA, active: false) });
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Receipt(AccountA), actor)).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void InactiveOrUnverifiedBranchIsDenied(bool? branchActive)
    {
        var actor = Build(new[] { Account(AccountA, BranchA, branchActive: branchActive) });
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Receipt(AccountA), actor)).Code);
    }

    [Fact]
    public void MissingBranchIsNotImplicitSharedAccess()
    {
        var actor = Build(new[] { Account(AccountA, null) });
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Receipt(AccountA), actor)).Code);
    }

    [Fact]
    public void ApprovedSharedAccountCanBeUsedWithoutBranch()
    {
        var actor = Build(new[] { Account(AccountA, null) }, shared: new[] { AccountA });
        Assert.Single(CashOperationPolicy.Prepare(Receipt(AccountA), actor).Legs);
    }

    [Fact]
    public void SharedGrantCannotBypassForeignBranch()
    {
        var actor = Build(new[] { Account(AccountA, BranchB) }, shared: new[] { AccountA });
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Receipt(AccountA), actor)).Code);
    }

    [Fact]
    public void TransferMustHaveRightsOnBothAccounts()
    {
        var actor = Build(new[] { Account(AccountA, BranchA), Account(AccountB, BranchB) });
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Transfer(), actor)).Code);
    }

    [Fact]
    public void TransferCanCrossTwoExplicitlyAuthorizedBranches()
    {
        var actor = Build(new[] { Account(AccountA, BranchA), Account(AccountB, BranchB) },
            branches: new[] { BranchA, BranchB });
        var legs = CashOperationPolicy.Prepare(Transfer(), actor).Legs;
        Assert.Equal(0m, legs[0].SignedAmountPen + legs[1].SignedAmountPen);
    }

    [Fact]
    public void AccountRightsDoNotImplyPrivilegeToSettle()
    {
        var actor = Build(new[] { Account(AccountA, BranchA) },
            operations: Array.Empty<CashOperationKind>());
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Receipt(AccountA), actor)).Code);
    }

    [Fact]
    public void DuplicateAccountEvidenceFailsClosed()
    {
        var account = Account(AccountA, BranchA);
        Assert.Equal("F5P004", Assert.Throws<CashPlanningException>(
            () => Build(new[] { account, account })).Code);
    }

    [Fact]
    public void MissingAuthenticatedPrincipalFailsClosed()
    {
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => Build(new[] { Account(AccountA, BranchA) }, subjectId: Guid.Empty)).Code);
    }

    [Fact]
    public void MissingAccountMetadataFailsClosed()
    {
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashAccountScopePolicy.BuildActor(Actor, null!,
                new[] { BranchA }, null!, null!)).Code);
    }

    [Fact]
    public void SharedGrantNeverActivatesAnInactiveAccount()
    {
        var actor = Build(new[] { Account(AccountA, null, active: false) }, shared: new[] { AccountA });
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Receipt(AccountA), actor)).Code);
    }
}
