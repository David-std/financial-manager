using System;
using FinancialManager.CashDomain;
using Xunit;

public sealed class CashReversalPolicyTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AccountA = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid AccountB = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid OperationId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid PostingA = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid PostingB = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly DateTime Day = new DateTime(2026, 10, 24);

    private static TrustedCashActor Actor(bool canReverse = true, bool destination = true)
        => new TrustedCashActor(ActorId,
            destination ? new[] { AccountA, AccountB } : new[] { AccountA },
            canReverse ? new[] { CashOperationKind.Reversal } : Array.Empty<CashOperationKind>());

    private static PostedCashLeg Leg(Guid posting, Guid account, decimal signed, DateTime? day = null)
        => new PostedCashLeg(posting, account, signed, day ?? Day);

    private static PostedCashOperation Receipt(decimal amount = 100m,
        bool posted = true, bool reversed = false)
        => new PostedCashOperation(OperationId, CashOperationKind.SettleReceipt,
            new[] { Leg(PostingA, AccountA, amount) }, posted, reversed);

    private static PostedCashOperation Transfer(decimal destinationAmount = 100m,
        Guid? destinationAccount = null, Guid? destinationPosting = null)
        => new PostedCashOperation(OperationId, CashOperationKind.InternalTransfer,
            new[]
            {
                Leg(PostingA, AccountA, -100m),
                Leg(destinationPosting ?? PostingB, destinationAccount ?? AccountB, destinationAmount)
            }, true, false);

    private static CashReversalCommand Request(DateTime? day = null,
        string key = "reverse-synthetic-1", string reason = "synthetic correction")
        => new CashReversalCommand(key, day ?? Day.AddDays(2), reason);

    [Fact]
    public void ReceiptReversalIsAnIndependentOppositePosting()
    {
        var result = CashReversalPolicy.Prepare(Receipt(), Request(), Actor());
        Assert.Equal(OperationId, result.OriginalOperationId);
        Assert.Single(result.Legs);
        Assert.Equal(-100m, result.Legs[0].SignedAmountPen);
        Assert.Equal(PostingA, result.Legs[0].ReversesPostingId);
        Assert.Equal(Day.AddDays(2), result.Legs[0].PostedOn);
        Assert.Equal(64, result.Fingerprint.Length);
    }

    [Fact]
    public void TransferReversalAlwaysContainsBothOppositeLegs()
    {
        var result = CashReversalPolicy.Prepare(Transfer(), Request(), Actor());
        Assert.Equal(2, result.Legs.Count);
        Assert.Equal(100m, result.Legs[0].SignedAmountPen);
        Assert.Equal(-100m, result.Legs[1].SignedAmountPen);
        Assert.Equal(0m, result.Legs[0].SignedAmountPen + result.Legs[1].SignedAmountPen);
        Assert.Equal(PostingA, result.Legs[0].ReversesPostingId);
        Assert.Equal(PostingB, result.Legs[1].ReversesPostingId);
    }

    [Fact]
    public void IncompleteOriginalTransferCannotBeReversed()
    {
        var original = new PostedCashOperation(OperationId, CashOperationKind.InternalTransfer,
            new[] { Leg(PostingA, AccountA, -100m) }, true, false);
        Assert.Equal("F5R001", Assert.Throws<CashPlanningException>(
            () => CashReversalPolicy.Prepare(original, Request(), Actor())).Code);
    }

    [Fact]
    public void UnbalancedOriginalTransferIsRejected()
        => Assert.Equal("F5R001", Assert.Throws<CashPlanningException>(
            () => CashReversalPolicy.Prepare(Transfer(99m), Request(), Actor())).Code);

    [Fact]
    public void SameAccountTransferCannotBeReversedAsValidLedger()
        => Assert.Equal("F5R001", Assert.Throws<CashPlanningException>(
            () => CashReversalPolicy.Prepare(Transfer(destinationAccount: AccountA), Request(), Actor())).Code);

    [Fact]
    public void DuplicateOriginalPostingIdFailsClosed()
        => Assert.Equal("F5R001", Assert.Throws<CashPlanningException>(
            () => CashReversalPolicy.Prepare(Transfer(destinationPosting: PostingA), Request(), Actor())).Code);

    [Fact]
    public void AlreadyReversedOrDraftRecordCannotBeReversed()
    {
        foreach (var original in new[] { Receipt(reversed: true), Receipt(posted: false) })
            Assert.Equal("F5R003", Assert.Throws<CashPlanningException>(
                () => CashReversalPolicy.Prepare(original, Request(), Actor())).Code);
    }

    [Fact]
    public void ExplicitReversalRoleIsRequired()
        => Assert.Equal("F5R002", Assert.Throws<CashPlanningException>(
            () => CashReversalPolicy.Prepare(Receipt(), Request(), Actor(canReverse: false))).Code);

    [Fact]
    public void BothAccountPermissionsAreRequired()
        => Assert.Equal("F5R002", Assert.Throws<CashPlanningException>(
            () => CashReversalPolicy.Prepare(Transfer(), Request(), Actor(destination: false))).Code);

    [Fact]
    public void OriginalPostingIsNotBackdated()
        => Assert.Equal("F5R001", Assert.Throws<CashPlanningException>(
            () => CashReversalPolicy.Prepare(Receipt(), Request(day: Day.AddDays(-1)), Actor())).Code);

    [Fact]
    public void ReversalReasonIsMandatoryAndBounded()
    {
        foreach (var reason in new[] { " ", "", new string('x', 301) })
            Assert.Equal("F5R001", Assert.Throws<CashPlanningException>(
                () => CashReversalPolicy.Prepare(Receipt(), Request(reason: reason), Actor())).Code);
    }

    [Fact]
    public void ReversalIdempotencyKeyCannotHaveSpacesOrExceedLength()
    {
        foreach (var key in new[] { "bad key", "", new string('k', 101) })
            Assert.Equal("F5R001", Assert.Throws<CashPlanningException>(
                () => CashReversalPolicy.Prepare(Receipt(), Request(key: key), Actor())).Code);
    }

    [Fact]
    public void HashIsDeterministicAndSensitiveToTheReason()
    {
        var first = CashReversalPolicy.Prepare(Receipt(), Request(), Actor());
        var second = CashReversalPolicy.Prepare(Receipt(), Request(), Actor());
        var changed = CashReversalPolicy.Prepare(Receipt(), Request(reason: "other synthetic reason"), Actor());
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public void InvalidOriginalPrecisionIsRejected()
        => Assert.Equal("F5R001", Assert.Throws<CashPlanningException>(
            () => CashReversalPolicy.Prepare(Receipt(amount: 10.001m), Request(), Actor())).Code);

    [Fact]
    public void ExtremeInvalidDecimalFailsWithControlledIntegrityError()
        => Assert.Equal("F5R001", Assert.Throws<CashPlanningException>(
            () => CashReversalPolicy.Prepare(Receipt(amount: decimal.MinValue),
                Request(), Actor())).Code);

    [Fact]
    public void DirectReversalThroughTheReceiptTransferPlannerIsRejected()
    {
        var unsafeInput = new CashOperationRequest(CashOperationKind.Reversal, "synthetic-1",
            AccountA, AccountB, 10m, Day, "PEN", "reason");
        var withWrongEntryPoint = new TrustedCashActor(ActorId, new[] { AccountA, AccountB },
            new[] { CashOperationKind.Reversal });
        Assert.Equal("F5P001", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(unsafeInput, withWrongEntryPoint)).Code);
    }

    [Fact]
    public void OriginalPostingDateDisagreementIsRejected()
    {
        var original = new PostedCashOperation(OperationId, CashOperationKind.InternalTransfer,
            new[] { Leg(PostingA, AccountA, -100m), Leg(PostingB, AccountB, 100m, Day.AddDays(1)) }, true, false);
        Assert.Equal("F5R001", Assert.Throws<CashPlanningException>(
            () => CashReversalPolicy.Prepare(original, Request(), Actor())).Code);
    }
}
