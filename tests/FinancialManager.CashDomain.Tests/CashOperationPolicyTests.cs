using System;
using FinancialManager.CashDomain;
using Xunit;

public sealed class CashOperationPolicyTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SourceId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid DestinationId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTime Day = new DateTime(2026, 10, 26);

    private static TrustedCashActor Actor(bool allowReceipt = true, bool allowTransfer = true,
        bool allowDestination = true, Guid? subjectId = null)
    {
        var rights = allowReceipt && allowTransfer
            ? new[] { CashOperationKind.SettleReceipt, CashOperationKind.InternalTransfer }
            : allowReceipt ? new[] { CashOperationKind.SettleReceipt }
            : allowTransfer ? new[] { CashOperationKind.InternalTransfer }
            : Array.Empty<CashOperationKind>();
        var accounts = allowDestination ? new[] { SourceId, DestinationId } : new[] { SourceId };
        return new TrustedCashActor(subjectId ?? ActorId, accounts, rights);
    }

    private static CashOperationRequest Receipt(decimal amount = 20.50m,
        string key = "receipt-001", string evidence = "synthetic-document")
        => new CashOperationRequest(CashOperationKind.SettleReceipt, key, SourceId, null,
                                    amount, Day, "PEN", evidence);

    private static CashOperationRequest Transfer(decimal amount = 20.50m,
        Guid? destination = null, string key = "transfer-001")
        => new CashOperationRequest(CashOperationKind.InternalTransfer, key, SourceId,
                                    destination ?? DestinationId, amount, Day, "PEN", "internal");

    [Fact]
    public void ReceiptProducesOnePositiveLegAndNoRevenueClaim()
    {
        var plan = CashOperationPolicy.Prepare(Receipt(), Actor());
        Assert.Equal(CashOperationKind.SettleReceipt, plan.Kind);
        Assert.Single(plan.Legs);
        Assert.Equal(20.50m, plan.Legs[0].SignedAmountPen);
        Assert.Equal(SourceId, plan.Legs[0].AccountId);
        Assert.Equal(Day, plan.Legs[0].ValueDate);
        Assert.Equal(64, plan.Fingerprint.Length);
    }

    [Fact]
    public void TransferConservesCashWithTwoOppositeLegs()
    {
        var plan = CashOperationPolicy.Prepare(Transfer(), Actor());
        Assert.Equal(2, plan.Legs.Count);
        Assert.Equal(SourceId, plan.Legs[0].AccountId);
        Assert.Equal(DestinationId, plan.Legs[1].AccountId);
        Assert.Equal(-20.50m, plan.Legs[0].SignedAmountPen);
        Assert.Equal(20.50m, plan.Legs[1].SignedAmountPen);
        Assert.Equal(0m, plan.Legs[0].SignedAmountPen + plan.Legs[1].SignedAmountPen);
        Assert.Equal(plan.Legs[0].ValueDate, plan.Legs[1].ValueDate);
    }

    [Fact]
    public void APlatformAdministratorWithoutBusinessPermissionCannotPost()
    {
        var adminWithoutFinanceRole = Actor(false, false);
        var error = Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Receipt(), adminWithoutFinanceRole));
        Assert.Equal("F5P002", error.Code);
    }

    [Fact]
    public void AccountRightsAreCheckedForBothTransferLegs()
    {
        var error = Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Transfer(), Actor(allowDestination: false)));
        Assert.Equal("F5P002", error.Code);

        var unauthorizedSource = new TrustedCashActor(ActorId,
            new[] { DestinationId }, new[] { CashOperationKind.InternalTransfer });
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Transfer(), unauthorizedSource)).Code);
    }

    [Fact]
    public void ActionPrivilegeIsIndependentFromAccountPermission()
    {
        var error = Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Transfer(), Actor(allowTransfer: false)));
        Assert.Equal("F5P002", error.Code);
    }

    [Fact]
    public void SameAccountTransferIsRejected()
    {
        var request = Transfer(destination: SourceId);
        Assert.Equal("F5P001", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(request, Actor())).Code);
    }

    [Fact]
    public void ATransferWithoutDestinationIsRejected()
    {
        var request = new CashOperationRequest(CashOperationKind.InternalTransfer,
            "transfer-002", SourceId, null, 10m, Day, "PEN", "internal");
        Assert.Equal("F5P001", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(request, Actor())).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(1000000000.01)]
    public void NonPositiveOverpreciseAndOutOfRangeMoneyIsRejected(double amount)
    {
        var request = Receipt((decimal)amount);
        Assert.Equal("F5P001", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(request, Actor())).Code);
    }

    [Fact]
    public void UnsupportedCurrencyCannotBeSilentlyConverted()
    {
        var request = new CashOperationRequest(CashOperationKind.SettleReceipt,
            "receipt-001", SourceId, null, 10m, Day, "USD", "evidence");
        Assert.Equal("F5P001", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(request, Actor())).Code);
    }

    [Fact]
    public void TimestampCannotMasqueradeAsAValueDate()
    {
        var request = new CashOperationRequest(CashOperationKind.SettleReceipt,
            "receipt-001", SourceId, null, 10m, Day.AddHours(9), "PEN", "evidence");
        Assert.Equal("F5P001", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(request, Actor())).Code);
    }

    [Fact]
    public void RequestKeyMustBeBoundedAsciiAndNotContainWhitespace()
    {
        foreach (var key in new[] { "", "a b", " x", new string('x', 101) })
        {
            var error = Assert.Throws<CashPlanningException>(
                () => CashOperationPolicy.Prepare(Receipt(key: key), Actor()));
            Assert.Equal("F5P001", error.Code);
        }
    }

    [Fact]
    public void ReceiptRequiresAnIndependentEvidenceReference()
    {
        Assert.Equal("F5P001", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Receipt(evidence: " "), Actor())).Code);
    }

    [Fact]
    public void AReceiptCannotCarryTransferDestination()
    {
        var request = new CashOperationRequest(CashOperationKind.SettleReceipt,
            "receipt-001", SourceId, DestinationId, 10m, Day, "PEN", "document");
        Assert.Equal("F5P001", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(request, Actor())).Code);
    }

    [Fact]
    public void IdenticalAuthorizedRetryReusesCommittedIdentity()
    {
        var planned = CashOperationPolicy.Prepare(Transfer(), Actor());
        var committedId = Guid.NewGuid();
        var existing = new CommittedCashOperation(committedId, planned.RequestKey,
            planned.Kind, planned.SubjectId, planned.Fingerprint);

        Assert.Equal(committedId, CashOperationPolicy.ResolveRetry(
            CashOperationPolicy.Prepare(Transfer(), Actor()), existing));
        Assert.Null(CashOperationPolicy.ResolveRetry(planned, null));
    }

    [Fact]
    public void SameKeyWithDifferentAmountMustConflict()
    {
        var first = CashOperationPolicy.Prepare(Transfer(), Actor());
        var existing = new CommittedCashOperation(Guid.NewGuid(), first.RequestKey,
            first.Kind, first.SubjectId, first.Fingerprint);
        var changed = CashOperationPolicy.Prepare(Transfer(amount: 21m), Actor());
        Assert.Equal("F5P003", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.ResolveRetry(changed, existing)).Code);
    }

    [Fact]
    public void ADifferentPrincipalCannotReplaySomeoneElsesRequestKey()
    {
        var first = CashOperationPolicy.Prepare(Receipt(), Actor());
        var existing = new CommittedCashOperation(Guid.NewGuid(), first.RequestKey,
            first.Kind, first.SubjectId, first.Fingerprint);
        var other = CashOperationPolicy.Prepare(Receipt(), Actor(subjectId: Guid.NewGuid()));
        Assert.Equal("F5P003", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.ResolveRetry(other, existing)).Code);
    }

    [Fact]
    public void RetryIsReauthorizedEvenIfFingerprintWouldMatch()
    {
        CashOperationPolicy.Prepare(Transfer(), Actor());
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(
            () => CashOperationPolicy.Prepare(Transfer(), Actor(allowDestination: false))).Code);
    }
}
