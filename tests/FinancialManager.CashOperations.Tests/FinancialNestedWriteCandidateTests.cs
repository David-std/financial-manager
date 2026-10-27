using System;
using System.Collections.Generic;
using System.Linq;
using FinancialManager.CashDomain;
using FinancialManager.CashOperations;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

public sealed class FinancialNestedWriteCandidateTests
{
    private static readonly Guid Pin = Guid.Parse("10000000-0000-4000-8000-000000000321");
    private static readonly Guid ReceiptPin = Guid.Parse("10000000-0000-4000-8000-000000000329");
    private static readonly Guid User = Guid.Parse("10000000-0000-4000-8000-000000000322");
    private static readonly Guid A = Guid.Parse("10000000-0000-4000-8000-000000000323");
    private static readonly Guid B = Guid.Parse("10000000-0000-4000-8000-000000000324");
    private static readonly Guid Branch = Guid.Parse("10000000-0000-4000-8000-000000000328");
    private static readonly Guid Currency = Guid.Parse("10000000-0000-4000-8000-000000000325");
    private static readonly Guid Header = Guid.Parse("10000000-0000-4000-8000-000000000326");
    private static readonly Guid Correlation = Guid.Parse("10000000-0000-4000-8000-000000000327");
    private static readonly DateTime Day = new DateTime(2026, 10, 27);

    private static CashOperationRequest Request() =>
        new CashOperationRequest(CashOperationKind.InternalTransfer, "nested-financial-test",
            A, B, 20m, Day, "PEN", "synthetic evidence only");
    private static PreparedCashOperation Plan() =>
        CashOperationPolicy.Prepare(Request(),
            new TrustedCashActor(User, new[] { A, B },
                new[] { CashOperationKind.InternalTransfer }));

    private static Entity HeaderRow()
    {
        var p = Plan();
        var header = new Entity("fmg_cashtransfer", Header) { RowVersion = "8" };
        header["fmg_name"] = "F5 InternalTransfer " + p.RequestKey;
        header["fmg_externalrequestid"] = p.RequestKey;
        header["fmg_requestfingerprint"] = p.Fingerprint;
        header["fmg_initiatinguserid"] = User.ToString("D");
        header["fmg_amount"] = new Money(20m);
        header["transactioncurrencyid"] = new EntityReference("transactioncurrency", Currency);
        header["fmg_sourceaccount"] = new EntityReference("fmg_cashaccount", A);
        header["fmg_destinationaccount"] = new EntityReference("fmg_cashaccount", B);
        header["fmg_transferredon"] = Day;
        header["fmg_status"] = new OptionSetValue(797281100);
        return header;
    }

    private static Entity Posting(int index)
    {
        var p = Plan();
        var leg = p.Legs[index];
        var row = new Entity("fmg_cashposting", Guid.NewGuid());
        row["fmg_name"] = "F5-" + p.Fingerprint.Substring(0, 48) + "-" + index;
        row["fmg_externalrequestid"] = "F5-" + p.Fingerprint + "-" + index;
        row["fmg_operationid"] = Header.ToString("D");
        row["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", leg.AccountId);
        row["fmg_signedamount"] = new Money(leg.SignedAmountPen);
        row["fmg_postedon"] = Day;
        row["fmg_kind"] = new OptionSetValue(797281202);
        row["transactioncurrencyid"] = new EntityReference("transactioncurrency", Currency);
        return row;
    }

    private static RemoteExecutionContext Context(Entity proposed)
    {
        var parent = new RemoteExecutionContext
        {
            MessageName = "fmg_PostCashTransfer", Stage = 30, Mode = 0,
            Depth = 1, IsInTransaction = true, UserId = User,
            InitiatingUserId = User, CorrelationId = Correlation,
            OwningExtension = new EntityReference("sdkmessageprocessingstep", Pin)
        };
        var request = Request();
        parent.InputParameters["RequestKey"] = request.RequestKey;
        parent.InputParameters["SourceAccount"] =
            new EntityReference("fmg_cashaccount", A);
        parent.InputParameters["DestinationAccount"] =
            new EntityReference("fmg_cashaccount", B);
        parent.InputParameters["Amount"] = new Money(20m);
        parent.InputParameters["ValueDate"] = Day;
        parent.InputParameters["EvidenceReference"] = request.EvidenceReference;
        var child = new RemoteExecutionContext
        {
            MessageName = "Create", PrimaryEntityName = proposed.LogicalName,
            Stage = 10, Mode = 0, Depth = 2, IsInTransaction = true,
            UserId = User, InitiatingUserId = User, CorrelationId = Correlation,
            ParentContext = parent
        };
        child.InputParameters["Target"] = proposed;
        return child;
    }

    private sealed class ReadOnlyDataverse : IOrganizationService
    {
        public Entity StoredHeader = HeaderRow();
        public List<Entity> PersistedPostings = new List<Entity>();
        public bool NoGrants;
        public int GrantedOperation = 797282001;
        public bool Disabled;
        public bool ApplicationUser;
        public bool InactiveAccount;
        public int Writes;

        public Entity Retrieve(string name, Guid id, ColumnSet columns)
        {
            var entity = new Entity(name, id);
            switch (name)
            {
                case "systemuser":
                    entity["isdisabled"] = Disabled;
                    entity["accessmode"] = new OptionSetValue(0);
                    if (ApplicationUser) entity["applicationid"] = Guid.NewGuid();
                    break;
                case "fmg_cashaccount":
                    entity["fmg_isactive"] = !InactiveAccount;
                    entity["fmg_branch"] = new EntityReference("fmg_branch", Branch);
                    break;
                case "fmg_branch":
                    entity["fmg_isactive"] = true;
                    break;
                case "transactioncurrency":
                    entity["isocurrencycode"] = "PEN";
                    break;
                case "fmg_cashtransfer":
                    return StoredHeader;
                default: throw new InvalidOperationException("Unexpected read " + name);
            }
            return entity;
        }
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = Assert.IsType<QueryExpression>(query);
            var result = new EntityCollection();
            if (q.EntityName == "fmg_cashposting")
            {
                Assert.Contains(q.Criteria.Conditions, c =>
                    c.AttributeName == "fmg_operationid" &&
                    Equals(c.Values.Single(), Header.ToString("D")));
                foreach (var posting in PersistedPostings)
                    result.Entities.Add(posting);
                return result;
            }
            Assert.Equal("fmg_cashaccessgrant", q.EntityName);
            if (NoGrants) return result;
            foreach (var id in new[] { A, B })
            {
                var row = new Entity("fmg_cashaccessgrant", Guid.NewGuid());
                row["fmg_principal"] = new EntityReference("systemuser", User);
                row["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", id);
                row["fmg_operation"] = new OptionSetValue(GrantedOperation);
                row["fmg_isactive"] = true;
                row["fmg_allowshared"] = false;
                row["fmg_expiresatutc"] = DateTime.UtcNow.AddDays(3);
                result.Entities.Add(row);
            }
            return result;
        }
        public Guid Create(Entity x) { Writes++; throw new Exception("Guard may not write"); }
        public void Update(Entity x) { Writes++; throw new Exception("Guard may not write"); }
        public void Delete(string n, Guid id) { Writes++; throw new Exception("Guard may not write"); }
        public void Associate(string n, Guid id, Relationship r, EntityReferenceCollection e)
        { Writes++; throw new Exception("Guard may not write"); }
        public void Disassociate(string n, Guid id, Relationship r, EntityReferenceCollection e)
        { Writes++; throw new Exception("Guard may not write"); }
        public OrganizationResponse Execute(OrganizationRequest x)
        { Writes++; throw new Exception("Guard may not write"); }
    }

    private static void Validate(Entity row, ReadOnlyDataverse svc,
        Action<RemoteExecutionContext>? manipulate = null)
    {
        var child = Context(row);
        manipulate?.Invoke(child);
        F5_FinancialWriteCandidatePolicy.RequireCreate(child, Pin, svc);
        Assert.Equal(0, svc.Writes);
    }

    private static Entity ReceiptHeader()
    {
        var req = new CashOperationRequest(CashOperationKind.SettleReceipt,
            "nested-receipt-test", A, null, 20m, Day, "PEN", "receipt test evidence");
        var plan = CashOperationPolicy.Prepare(req,
            new TrustedCashActor(User, new[] { A },
                new[] { CashOperationKind.SettleReceipt }));
        var row = new Entity("fmg_cashreceipt", Guid.NewGuid());
        row["fmg_name"] = "F5 SettleReceipt " + plan.RequestKey;
        row["fmg_externalrequestid"] = plan.RequestKey;
        row["fmg_requestfingerprint"] = plan.Fingerprint;
        row["fmg_initiatinguserid"] = User.ToString("D");
        row["fmg_amount"] = new Money(20m);
        row["transactioncurrencyid"] = new EntityReference("transactioncurrency", Currency);
        row["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", A);
        row["fmg_branch"] = new EntityReference("fmg_branch", Branch);
        row["fmg_scope"] = new OptionSetValue(797281010);
        row["fmg_receivedon"] = Day;
        row["fmg_status"] = new OptionSetValue(797281000);
        return row;
    }

    private static RemoteExecutionContext ReceiptContext(Entity row)
    {
        var child = Context(row);
        var parent = child.ParentContext;
        parent.MessageName = "fmg_SettleCashReceipt";
        parent.OwningExtension = new EntityReference("sdkmessageprocessingstep", ReceiptPin);
        parent.InputParameters.Remove("DestinationAccount");
        parent.InputParameters["RequestKey"] = "nested-receipt-test";
        parent.InputParameters["EvidenceReference"] = "receipt test evidence";
        return child;
    }

    [Fact]
    public void BranchReceiptRequiresExactReceiptGrantAndBranchScope()
    {
        var db = new ReadOnlyDataverse { GrantedOperation = 797282000 };
        F5_FinancialWriteCandidatePolicy.RequireCreate(
            ReceiptContext(ReceiptHeader()), ReceiptPin, db);
        Assert.Equal(0, db.Writes);
    }

    [Fact]
    public void ReceiptCannotAlterBranchOrScopeOrUseTransferGrant()
    {
        var db = new ReadOnlyDataverse { GrantedOperation = 797282000 };
        var row = ReceiptHeader();
        row["fmg_scope"] = new OptionSetValue(797281011);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_FinancialWriteCandidatePolicy.RequireCreate(
                ReceiptContext(row), ReceiptPin, db));
        row = ReceiptHeader();
        row["fmg_branch"] = new EntityReference("fmg_branch", Guid.NewGuid());
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_FinancialWriteCandidatePolicy.RequireCreate(
                ReceiptContext(row), ReceiptPin, db));
        Assert.Throws<CashPlanningException>(() =>
            F5_FinancialWriteCandidatePolicy.RequireCreate(
                ReceiptContext(ReceiptHeader()), ReceiptPin, new ReadOnlyDataverse()));
    }

    [Fact]
    public void ExactTransferHeaderMustMatchParentRequestAndLiveGrants()
    {
        Validate(HeaderRow(), new ReadOnlyDataverse());
    }

    [Theory]
    [InlineData("fmg_amount")]
    [InlineData("fmg_requestfingerprint")]
    [InlineData("fmg_initiatinguserid")]
    [InlineData("fmg_sourceaccount")]
    [InlineData("fmg_destinationaccount")]
    public void TamperedTransferHeadersAreRejected(string key)
    {
        var row = HeaderRow();
        row[key] = key == "fmg_amount" ? (object)new Money(21m) :
            key == "fmg_sourceaccount" || key == "fmg_destinationaccount"
                ? new EntityReference("fmg_cashaccount", Guid.NewGuid())
                : "tampered";
        Assert.Throws<InvalidPluginExecutionException>(() =>
            Validate(row, new ReadOnlyDataverse()));
    }

    [Fact]
    public void UnexpectedFieldsCannotBeUsedToSmuggleOtherFinancialChanges()
    {
        var row = HeaderRow();
        row["fmg_clientapproved"] = true;
        Assert.Throws<InvalidPluginExecutionException>(() =>
            Validate(row, new ReadOnlyDataverse()));
    }

    [Fact]
    public void UnpinnedOrDirectOrDifferentTransactionCannotCreateHeader()
    {
        var header = HeaderRow();
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_FinancialWriteCandidatePolicy.RequireCreate(Context(header),
                Guid.NewGuid(), new ReadOnlyDataverse()));
        Assert.Throws<InvalidPluginExecutionException>(() =>
            Validate(header, new ReadOnlyDataverse(), c => c.ParentContext = null));
        Assert.Throws<InvalidPluginExecutionException>(() =>
            Validate(header, new ReadOnlyDataverse(), c => c.CorrelationId = Guid.NewGuid()));
        Assert.Throws<InvalidPluginExecutionException>(() =>
            Validate(header, new ReadOnlyDataverse(), c => c.IsInTransaction = false));
    }

    [Fact]
    public void ClientTagDoesNotSatisfyTrustedOrigin()
    {
        Assert.Throws<InvalidPluginExecutionException>(() =>
            Validate(HeaderRow(), new ReadOnlyDataverse(), c =>
            {
                c.ParentContext = null;
                c.SharedVariables["tag"] = "internal";
            }));
    }

    [Fact]
    public void NoGrantOrApplicationIdentityBlocksNestedFinancialMutation()
    {
        Assert.Throws<CashPlanningException>(() =>
            Validate(HeaderRow(), new ReadOnlyDataverse { NoGrants = true }));
        Assert.Throws<CashPlanningException>(() =>
            Validate(HeaderRow(), new ReadOnlyDataverse { ApplicationUser = true }));
        Assert.Throws<CashPlanningException>(() =>
            Validate(HeaderRow(), new ReadOnlyDataverse { Disabled = true }));
        Assert.Throws<CashPlanningException>(() =>
            Validate(HeaderRow(), new ReadOnlyDataverse { InactiveAccount = true }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void VerifiedPostingLegMustExactlyMatchParentAndDraftHeader(int index)
    {
        Validate(Posting(index), new ReadOnlyDataverse());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ReversedSignAndWrongAccountOnEitherLegMustBeRejected(int index)
    {
        var row = Posting(index);
        row["fmg_signedamount"] = new Money(-((Money)row["fmg_signedamount"]).Value);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            Validate(row, new ReadOnlyDataverse()));
        row = Posting(index);
        row["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", Guid.NewGuid());
        Assert.Throws<InvalidPluginExecutionException>(() =>
            Validate(row, new ReadOnlyDataverse()));
    }

    [Fact]
    public void PostingCannotAttachToUnrelatedHeaderOrAnotherIdempotencyKey()
    {
        var row = Posting(1);
        row["fmg_operationid"] = Guid.NewGuid().ToString("D");
        Assert.Throws<InvalidPluginExecutionException>(() =>
            Validate(row, new ReadOnlyDataverse()));
        var db = new ReadOnlyDataverse();
        db.StoredHeader["fmg_requestfingerprint"] = new string('F', 64);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            Validate(Posting(0), db));
    }

    [Fact]
    public void NewCandidateGuardNeverAuthorizesAClientDirectCreate()
    {
        var direct = Context(HeaderRow());
        direct.ParentContext = null;
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ValidatedNestedCashGuard.RequireOperation(direct, ReceiptPin, Pin,
                new ReadOnlyDataverse()));
    }

    [Fact]
    public void NewCandidateGuardUsesExactlySameVerifiedCashPlanChecks()
    {
        var db = CompletedLedger();
        F5_ValidatedNestedCashGuard.RequireOperation(Context(HeaderRow()), ReceiptPin, Pin, db);
        F5_ValidatedNestedCashGuard.RequireOperation(Completion(), ReceiptPin, Pin, db);
        Assert.Equal(0, db.Writes);
        var forged = Posting(1);
        forged["fmg_signedamount"] = new Money(999m);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ValidatedNestedCashGuard.RequireOperation(Context(forged), ReceiptPin, Pin, db));
    }

    [Fact]
    public void NewCandidateGuardRejectsDeletePostingUpdateAndUntrustedStage()
    {
        var db = CompletedLedger();
        var posting = Context(Posting(1));
        posting.MessageName = "Update";
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ValidatedNestedCashGuard.RequireOperation(posting, ReceiptPin, Pin, db));
        var target = Context(HeaderRow());
        target.MessageName = "Delete";
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ValidatedNestedCashGuard.RequireOperation(target, ReceiptPin, Pin, db));
        target = Context(HeaderRow());
        target.Stage = 20;
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ValidatedNestedCashGuard.RequireOperation(target, ReceiptPin, Pin, db));
    }

    [Fact]
    public void ReceiptAndTransferMustNotReuseTheSameMainOperationStep()
    {
        var transfer = Context(HeaderRow());
        transfer.ParentContext.OwningExtension =
            new EntityReference("sdkmessageprocessingstep", ReceiptPin);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ValidatedNestedCashGuard.RequireOperation(transfer,
                ReceiptPin, Pin, new ReadOnlyDataverse()));

        var receipt = ReceiptContext(ReceiptHeader());
        receipt.ParentContext.OwningExtension =
            new EntityReference("sdkmessageprocessingstep", Pin);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ValidatedNestedCashGuard.RequireOperation(receipt,
                ReceiptPin, Pin, new ReadOnlyDataverse
                {
                    GrantedOperation = 797282000
                }));
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ValidatedNestedCashGuard.RequireOperation(transfer,
                Pin, Pin, new ReadOnlyDataverse()));
    }

    [Fact]
    public void GuardConstructorRequiresBothDistinctExactStepPins()
    {
        foreach (var config in new[] {
            "", "invalid", Pin.ToString("D"),
            Pin.ToString("D") + "|" + Pin.ToString("D"),
            Pin.ToString("D") + "|invalid"
        })
        {
            Assert.Throws<InvalidPluginExecutionException>(() =>
                new F5_ValidatedNestedCashGuard(config, ""));
        }
        var guard = new F5_ValidatedNestedCashGuard(
            ReceiptPin.ToString("D") + "|" + Pin.ToString("D"), "");
        Assert.NotNull(guard);
    }

    private static RemoteExecutionContext Completion()
    {
        var target = new Entity("fmg_cashtransfer", Header) { RowVersion = "8" };
        target["fmg_status"] = new OptionSetValue(797281101);
        var context = Context(target);
        context.MessageName = "Update";
        return context;
    }

    private static ReadOnlyDataverse CompletedLedger()
    {
        var db = new ReadOnlyDataverse();
        db.PersistedPostings.Add(Posting(0));
        db.PersistedPostings.Add(Posting(1));
        return db;
    }

    [Fact]
    public void HeaderMayCompleteOnlyWithTwoExactBalancedPersistedLegs()
    {
        var db = CompletedLedger();
        F5_FinancialHeaderCompletionPolicy.RequireUpdate(Completion(), Pin, db);
        Assert.Equal(0, db.Writes);
    }

    [Fact]
    public void MissingOrAlteredSecondLegCannotCompleteHeader()
    {
        var db = CompletedLedger();
        db.PersistedPostings.RemoveAt(1);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_FinancialHeaderCompletionPolicy.RequireUpdate(Completion(), Pin, db));
        db = CompletedLedger();
        db.PersistedPostings[1]["fmg_signedamount"] = new Money(21m);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_FinancialHeaderCompletionPolicy.RequireUpdate(Completion(), Pin, db));
    }

    [Fact]
    public void DuplicateOrDifferentAccountPostingCannotCompleteHeader()
    {
        var db = CompletedLedger();
        db.PersistedPostings[1]["fmg_externalrequestid"] =
            db.PersistedPostings[0]["fmg_externalrequestid"];
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_FinancialHeaderCompletionPolicy.RequireUpdate(Completion(), Pin, db));
        db = CompletedLedger();
        db.PersistedPostings[1]["fmg_cashaccount"] =
            new EntityReference("fmg_cashaccount", Guid.NewGuid());
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_FinancialHeaderCompletionPolicy.RequireUpdate(Completion(), Pin, db));
    }

    [Fact]
    public void FinalizationRequiresExactETagAndOnlyStatusField()
    {
        var db = CompletedLedger();
        var context = Completion();
        ((Entity)context.InputParameters["Target"]).RowVersion = "old";
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_FinancialHeaderCompletionPolicy.RequireUpdate(context, Pin, db));
        context = Completion();
        ((Entity)context.InputParameters["Target"])["fmg_amount"] = new Money(10000m);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_FinancialHeaderCompletionPolicy.RequireUpdate(context, Pin, db));
    }

    [Fact]
    public void UnpinnedOrUnprivilegedMainOperationCannotComplete()
    {
        var db = CompletedLedger();
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_FinancialHeaderCompletionPolicy.RequireUpdate(Completion(),
                Guid.NewGuid(), db));
        db.NoGrants = true;
        Assert.Throws<CashPlanningException>(() =>
            F5_FinancialHeaderCompletionPolicy.RequireUpdate(Completion(), Pin, db));
        Assert.Equal(0, db.Writes);
    }

    [Fact]
    public void DifferentGrantOperationCannotAuthorizeTransfer()
    {
        var row = HeaderRow();
        var db = new ReadOnlyDataverse { NoGrants = true };
        Assert.Throws<CashPlanningException>(() => Validate(row, db));
        Assert.Equal(0, db.Writes);
    }
}
