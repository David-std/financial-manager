using System;
using System.Collections.Generic;
using System.Linq;
using FinancialManager.CashDomain;
using Xunit;

public sealed class CashExecutionCoordinatorTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid A = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid B = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTime Date = new DateTime(2026, 10, 26);

    private static CashRuntimeEvidence Runtime(bool inTransaction = true, int stage = 30,
        int mode = 0, Guid? initiating = null, Guid? executing = null)
        => new CashRuntimeEvidence(stage, mode, inTransaction, initiating ?? User, executing ?? User);

    private static CashOperationRequest Transfer(string key = "synthetic-transfer",
        decimal amount = 17.25m)
        => new CashOperationRequest(CashOperationKind.InternalTransfer, key, A, B,
            amount, Date, "PEN", "synthetic-movement");

    private static CashOperationRequest Receipt(string key = "synthetic-receipt")
        => new CashOperationRequest(CashOperationKind.SettleReceipt, key, A, null,
            12.50m, Date, "PEN", "synthetic-document");

    private static TestAuth Grant(bool permitted = true, bool destination = true)
        => new TestAuth { Permitted = permitted, Destination = destination };

    private sealed class TestAuth : IServerCashAuthorization
    {
        public bool Permitted { get; set; } = true;
        public bool Destination { get; set; } = true;
        public bool SpoofActor { get; set; }
        public int Invocations { get; private set; }
        public TrustedCashActor Authorize(Guid subject, CashOperationRequest request)
        {
            Invocations++;
            return new TrustedCashActor(SpoofActor ? Guid.NewGuid() : subject,
                Destination ? new[] { A, B } : new[] { A },
                Permitted ? new[] { CashOperationKind.InternalTransfer, CashOperationKind.SettleReceipt }
                          : Array.Empty<CashOperationKind>());
        }
    }

    private sealed class TestLedger : ICashLedgerTransaction
    {
        public CommittedCashOperation? Existing { get; set; }
        public int WriteNumber { get; private set; }
        public int FailOnWrite { get; set; }
        public bool TamperWithPosting { get; set; }
        public bool DropSecondLeg { get; set; }
        public bool DuplicatePostId { get; set; }
        public bool DuplicateRequestKey { get; set; }
        public bool Completed { get; private set; }
        public PreparedCashOperation? Header { get; private set; }
        private readonly List<CashPostingSnapshot> postings = new List<CashPostingSnapshot>();
        public IReadOnlyList<CashPostingSnapshot> Postings => postings;
        public void SeedCommitted(PreparedCashOperation plan)
        {
            foreach (var leg in plan.Legs)
                postings.Add(new CashPostingSnapshot(Guid.NewGuid(), HeaderId,
                    leg.AccountId, leg.SignedAmountPen, leg.ValueDate));
        }
        public void CorruptPriorLedger() => postings.RemoveAt(postings.Count - 1);
        public Guid HeaderId { get; } = Guid.Parse("44444444-4444-4444-8444-444444444444");
        private readonly Guid repeatedPosting = Guid.Parse("55555555-5555-4555-8555-555555555555");

        public CommittedCashOperation? FindCommittedByKey(CashOperationKind kind, string requestKey)
            => Existing;
        public Guid CreateHeader(PreparedCashOperation plan)
        {
            WriteNumber++;
            if (DuplicateRequestKey)
                throw new InvalidOperationException("Simulated active Dataverse alternate key conflict");
            Header = plan;
            if (WriteNumber == FailOnWrite)
                throw new InvalidOperationException("Synthetic header failure");
            return HeaderId;
        }
        public Guid CreatePosting(Guid operationId, PreparedCashOperation plan, CashLeg leg, int index)
        {
            WriteNumber++;
            if (WriteNumber == FailOnWrite)
                throw new InvalidOperationException("Synthetic second-leg failure");
            var id = DuplicatePostId ? repeatedPosting : Guid.NewGuid();
            if (!(DropSecondLeg && index == 1))
                postings.Add(new CashPostingSnapshot(id, operationId, leg.AccountId,
                    TamperWithPosting ? leg.SignedAmountPen + 1m : leg.SignedAmountPen,
                    leg.ValueDate));
            return id;
        }
        public void CompleteHeader(Guid operationId, string fingerprint, IReadOnlyList<Guid> postingIds)
        {
            WriteNumber++;
            if (WriteNumber == FailOnWrite)
                throw new InvalidOperationException("Synthetic finalization failure");
            Completed = true;
        }
        public IReadOnlyList<CashPostingSnapshot> ReadCompleteLedger(Guid operationId)
            => postings.ToArray();

        // Models rollback by aborting a transaction; tests deliberately do NOT
        // represent this as live Dataverse proof of ACID guarantees.
        public void AbortSimulatedTransaction()
        {
            Header = null;
            postings.Clear();
            Completed = false;
        }
    }

    [Fact]
    public void TransferWritesHeaderTwoOppositePostingsThenCompletes()
    {
        var ledger = new TestLedger();
        var result = new CashExecutionCoordinator(Grant(), ledger).Execute(Runtime(), Transfer());
        Assert.False(result.WasReplay);
        Assert.Equal(ledger.HeaderId, result.OperationId);
        Assert.Equal(4, ledger.WriteNumber);
        Assert.True(ledger.Completed);
        Assert.Equal(2, ledger.Postings.Count);
        Assert.Equal(0m, ledger.Postings.Sum(p => p.SignedAmountPen));
        Assert.All(ledger.Postings, p => Assert.Equal(ledger.HeaderId, p.OperationId));
    }

    [Fact]
    public void ReceiptProducesOnlyOnePositivePosting()
    {
        var ledger = new TestLedger();
        new CashExecutionCoordinator(Grant(), ledger).Execute(Runtime(), Receipt());
        Assert.Single(ledger.Postings);
        Assert.Equal(12.50m, ledger.Postings[0].SignedAmountPen);
    }

    [Theory]
    [InlineData(false, 30, 0)]
    [InlineData(true, 10, 0)]
    [InlineData(true, 30, 1)]
    public void NonTransactionalOrWrongPipelineCannotWrite(bool inTx, int stage, int mode)
    {
        var ledger = new TestLedger();
        var auth = Grant();
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(auth, ledger).Execute(
                Runtime(inTx, stage, mode), Transfer())).Code);
        Assert.Equal(0, auth.Invocations);
        Assert.Equal(0, ledger.WriteNumber);
    }

    [Fact]
    public void MissingIdentityCannotAccessTheLedger()
    {
        var ledger = new TestLedger();
        var ctx = new CashRuntimeEvidence(30, 0, true, Guid.Empty, User);
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(Grant(), ledger).Execute(ctx, Transfer())).Code);
        Assert.Equal(0, ledger.WriteNumber);
    }

    [Fact]
    public void DelegatedOrImpersonatedUserFailsClosed()
    {
        var ledger = new TestLedger();
        Assert.Equal("F5E002", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(Grant(), ledger).Execute(
                Runtime(executing: Guid.NewGuid()), Transfer())).Code);
        Assert.Equal(0, ledger.WriteNumber);
    }

    [Fact]
    public void AuthProviderCannotReturnDifferentSubject()
    {
        var ledger = new TestLedger();
        var auth = Grant();
        auth.SpoofActor = true;
        Assert.Equal("F5E002", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(auth, ledger).Execute(Runtime(), Transfer())).Code);
        Assert.Equal(0, ledger.WriteNumber);
    }

    [Fact]
    public void MissingOperationPermissionFailsBeforeWrites()
    {
        var ledger = new TestLedger();
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(Grant(permitted: false), ledger)
                .Execute(Runtime(), Transfer())).Code);
        Assert.Equal(0, ledger.WriteNumber);
    }

    [Fact]
    public void BothAccountsMustBeAuthorizedBeforeWrites()
    {
        var ledger = new TestLedger();
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(Grant(destination: false), ledger)
                .Execute(Runtime(), Transfer())).Code);
        Assert.Equal(0, ledger.WriteNumber);
    }

    [Fact]
    public void ReplayReauthorizesAndNeverWrites()
    {
        var ledger = new TestLedger();
        var auth = Grant();
        var plan = CashOperationPolicy.Prepare(Transfer(), auth.Authorize(User, Transfer()));
        ledger.Existing = new CommittedCashOperation(ledger.HeaderId, plan.RequestKey,
            plan.Kind, plan.SubjectId, plan.Fingerprint);
        ledger.SeedCommitted(plan);
        var result = new CashExecutionCoordinator(auth, ledger).Execute(Runtime(), Transfer());
        Assert.True(result.WasReplay);
        Assert.Equal(ledger.HeaderId, result.OperationId);
        Assert.Equal(0, ledger.WriteNumber);
        Assert.Equal(2, auth.Invocations);
    }

    [Fact]
    public void RetryWithAnIncompleteCommittedLedgerFailsClosed()
    {
        var ledger = new TestLedger();
        var positive = Grant();
        var plan = CashOperationPolicy.Prepare(Transfer(), positive.Authorize(User, Transfer()));
        ledger.Existing = new CommittedCashOperation(ledger.HeaderId, plan.RequestKey,
            plan.Kind, plan.SubjectId, plan.Fingerprint);
        ledger.SeedCommitted(plan);
        ledger.CorruptPriorLedger();
        Assert.Equal("F5E004", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(Grant(), ledger).Execute(Runtime(), Transfer())).Code);
        Assert.Equal(0, ledger.WriteNumber);
    }

    [Fact]
    public void ReplayAfterRevocationIsRejected()
    {
        var ledger = new TestLedger();
        var positive = Grant();
        var plan = CashOperationPolicy.Prepare(Transfer(), positive.Authorize(User, Transfer()));
        ledger.Existing = new CommittedCashOperation(ledger.HeaderId, plan.RequestKey,
            plan.Kind, plan.SubjectId, plan.Fingerprint);
        Assert.Equal("F5P002", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(Grant(permitted: false), ledger)
                .Execute(Runtime(), Transfer())).Code);
        Assert.Equal(0, ledger.WriteNumber);
    }

    [Fact]
    public void ReusedRequestKeyWithDifferentPayloadIsConflict()
    {
        var ledger = new TestLedger();
        var positive = Grant();
        var plan = CashOperationPolicy.Prepare(Transfer(), positive.Authorize(User, Transfer()));
        ledger.Existing = new CommittedCashOperation(ledger.HeaderId, plan.RequestKey,
            plan.Kind, plan.SubjectId, plan.Fingerprint);
        Assert.Equal("F5P003", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(Grant(), ledger)
                .Execute(Runtime(), Transfer(amount: 99m))).Code);
        Assert.Equal(0, ledger.WriteNumber);
    }

    [Fact]
    public void DuplicateKeyRaceDoesNotGeneratePosting()
    {
        var ledger = new TestLedger { DuplicateRequestKey = true };
        Assert.Throws<InvalidOperationException>(() =>
            new CashExecutionCoordinator(Grant(), ledger).Execute(Runtime(), Transfer()));
        Assert.Empty(ledger.Postings);
        Assert.False(ledger.Completed);
    }

    [Fact]
    public void InjectedSecondLegFailurePropagatesForTransactionRollback()
    {
        var ledger = new TestLedger { FailOnWrite = 3 };
        Assert.Throws<InvalidOperationException>(() =>
            new CashExecutionCoordinator(Grant(), ledger).Execute(Runtime(), Transfer()));
        Assert.Single(ledger.Postings);
        Assert.False(ledger.Completed);
        ledger.AbortSimulatedTransaction();
        Assert.Null(ledger.Header);
        Assert.Empty(ledger.Postings);
        Assert.False(ledger.Completed);
    }

    [Fact]
    public void MissingSecondLegDetectedBeforeSuccessResponse()
    {
        var ledger = new TestLedger { DropSecondLeg = true };
        Assert.Equal("F5E004", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(Grant(), ledger).Execute(Runtime(), Transfer())).Code);
    }

    [Fact]
    public void TamperedPostingDetectedBeforeSuccessResponse()
    {
        var ledger = new TestLedger { TamperWithPosting = true };
        Assert.Equal("F5E004", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(Grant(), ledger).Execute(Runtime(), Receipt())).Code);
    }

    [Fact]
    public void DuplicatePostingIdentityDetected()
    {
        var ledger = new TestLedger { DuplicatePostId = true };
        Assert.Equal("F5E003", Assert.Throws<CashPlanningException>(() =>
            new CashExecutionCoordinator(Grant(), ledger).Execute(Runtime(), Transfer())).Code);
    }

    [Fact]
    public void FinalizationFailureMustNotBeSwallowed()
    {
        var ledger = new TestLedger { FailOnWrite = 4 };
        Assert.Throws<InvalidOperationException>(() =>
            new CashExecutionCoordinator(Grant(), ledger).Execute(Runtime(), Transfer()));
        Assert.False(ledger.Completed);
    }
}
