using System;
using System.Linq;
using FinancialManager.CashDomain;
using Xunit;

public sealed class CashBookReconciliationPolicyTests
{
    private static readonly Guid Account = Guid.Parse("bb000001-0000-4000-8000-000000000001");
    private static readonly Guid Another = Guid.Parse("bb000002-0000-4000-8000-000000000002");
    private static readonly Guid OpeningId = Guid.Parse("bb000003-0000-4000-8000-000000000003");
    private static readonly Guid StatementId = Guid.Parse("bb000004-0000-4000-8000-000000000004");
    private static readonly Guid P1 = Guid.Parse("bb000005-0000-4000-8000-000000000005");
    private static readonly Guid P2 = Guid.Parse("bb000006-0000-4000-8000-000000000006");
    private static readonly DateTime OpeningDate = new DateTime(2026,10,1);
    private static readonly DateTime Cutoff = new DateTime(2026,10,27);
    private static CashOpeningSnapshot Opening(decimal balance=100m,Guid? account=null,
        string approval="dual-control-reference")
        => new CashOpeningSnapshot(OpeningId,account??Account,OpeningDate,balance,"PEN",approval);
    private static CashBankStatementSnapshot Statement(decimal balance=110m,Guid? account=null,
        string evidence="verified-bank-document")
        => new CashBankStatementSnapshot(StatementId,account??Account,Cutoff,balance,"PEN",evidence);
    private static CashMovementSnapshot Posting(Guid id,decimal amount,
        Guid? account=null,bool posted=true,DateTime? date=null,string currency="PEN")
        => new CashMovementSnapshot(id,account??Account,date??Cutoff,amount,posted,
            currency,"posted-operational-document");
    private static CashMovementSnapshot[] Movements() =>
        new[] {Posting(P1,-20m), Posting(P2,30m)};
    private static CashPlanningException Denied(Func<CashBookReconciliation> evaluate)
        => Assert.Throws<CashPlanningException>(evaluate);

    [Fact]
    public void SignedPostingsAndApprovedOpeningProduceExactReconciledBookBalance()
    {
        var result=CashBookReconciliationPolicy.Prepare(Account,Opening(),Movements(),Statement());
        Assert.Equal(110m,result.BookBalancePen);
        Assert.Equal(110m,result.BankBalancePen);
        Assert.Equal(0m,result.DifferencePen);
        Assert.True(result.IsReconciled);
        Assert.Equal(2,result.PostingCount);
        Assert.Equal(64,result.Fingerprint.Length);
    }

    [Fact]
    public void ReorderingLedgerRowsDoesNotChangeReconciliationFingerprint()
    {
        var first=CashBookReconciliationPolicy.Prepare(Account,Opening(),Movements(),Statement());
        var reverse=CashBookReconciliationPolicy.Prepare(Account,Opening(),
            Movements().Reverse().ToArray(),Statement());
        Assert.Equal(first.Fingerprint,reverse.Fingerprint);
    }

    [Fact]
    public void BankDifferenceIsReportedAndNotSilentlyErased()
    {
        var result=CashBookReconciliationPolicy.Prepare(Account,Opening(),Movements(),Statement(105m));
        Assert.Equal(-5m,result.DifferencePen);
        Assert.False(result.IsReconciled);
    }

    [Fact]
    public void CrossAccountPostingAndBankEvidenceAreRejected()
    {
        Assert.Equal("F5B002",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(),new[]{Posting(P1,5m,Another)},Statement())).Code);
        Assert.Equal("F5B001",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(),Movements(),Statement(account:Another))).Code);
        Assert.Equal("F5B001",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(account:Another),Movements(),Statement())).Code);
    }

    [Fact]
    public void DuplicatePostingCannotInflateBalance()
    {
        Assert.Equal("F5B002",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(),new[]{Posting(P1,30m),Posting(P1,30m)},Statement())).Code);
    }

    [Theory]
    [InlineData(false,"PEN")]
    [InlineData(true,"USD")]
    public void DraftOrWrongCurrencyCannotAffectBook(bool posted,string currency)
    {
        Assert.Equal("F5B002",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(),new[]{Posting(P1,1m,posted:posted,currency:currency)},
            Statement())).Code);
    }

    [Fact]
    public void ReconciliationRejectsLinesOutsideOpeningCutoffRange()
    {
        Assert.Equal("F5B002",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(),new[]{Posting(P1,1m,date:OpeningDate.AddDays(-1))},Statement())).Code);
        Assert.Equal("F5B002",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(),new[]{Posting(P1,1m,date:Cutoff.AddDays(1))},
            Statement())).Code);
    }

    [Fact]
    public void PostedMovementsOnOpeningDayAreIncludedFromStartOfDay()
    {
        var result=CashBookReconciliationPolicy.Prepare(
            Account,Opening(100m),new[]{Posting(P1,-10m,date:OpeningDate)},Statement(90m));
        Assert.Equal(90m,result.BookBalancePen);
        Assert.True(result.IsReconciled);
    }

    [Fact]
    public void MissingOpeningApprovalAndStatementEvidenceAreRejected()
    {
        Assert.Equal("F5B001",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(approval:" "),Movements(),Statement())).Code);
        Assert.Equal("F5B001",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(),Movements(),Statement(evidence:" "))).Code);
    }

    [Fact]
    public void UnapprovedOpeningCannotBeInferredFromTransfers()
    {
        Assert.Equal("F5B001",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,null!,Movements(),Statement())).Code);
    }

    [Fact]
    public void ExcessPrecisionCannotEnterBankOrPostingBalance()
    {
        Assert.Equal("F5B002",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(),new[]{Posting(P1,0.001m)},Statement())).Code);
        Assert.Equal("F5B001",Denied(()=>CashBookReconciliationPolicy.Prepare(
            Account,Opening(),Movements(),Statement(110.001m))).Code);
    }

    [Fact]
    public void ValidEmptyLedgerIsAllowedOnlyWithExplicitOpening()
    {
        var result=CashBookReconciliationPolicy.Prepare(
            Account,Opening(50m),Array.Empty<CashMovementSnapshot>(),Statement(50m));
        Assert.Equal(50m,result.BookBalancePen);
        Assert.True(result.IsReconciled);
    }

    [Fact]
    public void ChangingBankEvidenceChangesImmutableFingerprint()
    {
        var a=CashBookReconciliationPolicy.Prepare(Account,Opening(),Movements(),Statement());
        var b=CashBookReconciliationPolicy.Prepare(Account,Opening(),Movements(),
            Statement(evidence:"different-bank-source"));
        Assert.NotEqual(a.Fingerprint,b.Fingerprint);
    }
}
