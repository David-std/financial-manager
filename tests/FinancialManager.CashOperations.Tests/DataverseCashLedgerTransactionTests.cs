using System;
using System.Collections.Generic;
using System.Linq;
using FinancialManager.CashDomain;
using FinancialManager.CashOperations;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

public sealed class DataverseCashLedgerTransactionTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AccountA = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid AccountB = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Branch = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateTime Day = new DateTime(2026, 10, 26);

    private static PreparedCashOperation Prepare(CashOperationKind kind)
    {
        var actor = new TrustedCashActor(User, new[] { AccountA, AccountB },
            new[] { CashOperationKind.SettleReceipt, CashOperationKind.InternalTransfer });
        var request = new CashOperationRequest(kind, "synthetic-key", AccountA,
            kind == CashOperationKind.InternalTransfer ? AccountB : (Guid?)null,
            17.25m, Day, "PEN", "synthetic");
        return CashOperationPolicy.Prepare(request, actor);
    }

    private sealed class FakeDataverse : IOrganizationService
    {
        public readonly Dictionary<Guid, Entity> Store = new Dictionary<Guid, Entity>();
        public readonly List<string> Calls = new List<string>();
        public bool Branchless;
        public bool Inactive;
        public bool MissingRowVersion;
        public int ConcurrencyUpdates;
        public Entity Retrieve(string table, Guid id, ColumnSet cols)
        {
            if (table == "fmg_cashaccount")
            {
                var account = new Entity(table, id);
                account["fmg_isactive"] = !Inactive;
                if (!Branchless)
                    account["fmg_branch"] = new EntityReference("fmg_branch", Branch);
                return account;
            }
            if (table == "fmg_branch")
            {
                var branch = new Entity(table, id);
                branch["fmg_isactive"] = !Inactive;
                return branch;
            }
            if (!Store.TryGetValue(id, out var row) || row.LogicalName != table)
                throw new InvalidOperationException("No synthetic row: " + table);
            row.RowVersion = MissingRowVersion ? null : "41";
            return row;
        }
        public EntityCollection RetrieveMultiple(QueryBase input)
        {
            var q = Assert.IsType<QueryExpression>(input);
            var records = Store.Values.Where(e => e.LogicalName == q.EntityName).ToArray();
            var criteria = q.Criteria.Conditions;
            foreach (var condition in criteria)
                records = records.Where(e =>
                    e.Contains(condition.AttributeName) &&
                    Equals(e[condition.AttributeName], condition.Values.FirstOrDefault())).ToArray();
            var result = new EntityCollection();
            foreach (var record in records) result.Entities.Add(record);
            return result;
        }
        public Guid Create(Entity entity)
        {
            Calls.Add("Create:" + entity.LogicalName);
            var id = Guid.NewGuid();
            entity.Id = id;
            Store[id] = entity;
            return id;
        }
        public void Update(Entity entity) { throw new InvalidOperationException("Unversioned update forbidden"); }
        public OrganizationResponse Execute(OrganizationRequest req)
        {
            var update = Assert.IsType<UpdateRequest>(req);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
            Assert.NotNull(update.Target.RowVersion);
            ConcurrencyUpdates++;
            Store[update.Target.Id]["fmg_status"] = update.Target["fmg_status"];
            Calls.Add("VersionedUpdate");
            return new UpdateResponse();
        }
        public void Delete(string table, Guid id) { throw new InvalidOperationException("No delete"); }
        public void Associate(string table, Guid id, Relationship relationship,
            EntityReferenceCollection refs) { throw new InvalidOperationException("No associate"); }
        public void Disassociate(string table, Guid id, Relationship relationship,
            EntityReferenceCollection refs) { throw new InvalidOperationException("No disassociate"); }
    }

    [Fact]
    public void ReceiptHeaderOneLegAndVersionedStatusUseServerSdk()
    {
        var fake = new FakeDataverse();
        var adapter = new DataverseCashLedgerTransaction(fake, User, Currency);
        var plan = Prepare(CashOperationKind.SettleReceipt);
        var headerId = adapter.CreateHeader(plan);
        var header = fake.Store[headerId];
        Assert.Equal("fmg_cashreceipt", header.LogicalName);
        Assert.Equal("synthetic-key", header.GetAttributeValue<string>("fmg_externalrequestid"));
        Assert.Equal(plan.Fingerprint, header.GetAttributeValue<string>("fmg_requestfingerprint"));
        Assert.Equal(User.ToString("D"), header.GetAttributeValue<string>("fmg_initiatinguserid"));
        Assert.Equal(17.25m, header.GetAttributeValue<Money>("fmg_amount").Value);
        Assert.Equal(797281010, header.GetAttributeValue<OptionSetValue>("fmg_scope").Value);
        Assert.Equal(Currency, header.GetAttributeValue<EntityReference>("transactioncurrencyid").Id);
        var posting = adapter.CreatePosting(headerId, plan, plan.Legs[0], 0);
        Assert.Equal(797281200, fake.Store[posting].GetAttributeValue<OptionSetValue>("fmg_kind").Value);
        adapter.CompleteHeader(headerId, plan.Fingerprint, new[] { posting });
        Assert.Equal(1, fake.ConcurrencyUpdates);
        Assert.Equal(797281001,
            fake.Store[headerId].GetAttributeValue<OptionSetValue>("fmg_status").Value);
        var posted = adapter.ReadCompleteLedger(headerId);
        CashExecutionIntegrity.Verify(plan, headerId, new[] { posting }, posted);
        Assert.Equal(3, fake.Calls.Count);
    }

    [Fact]
    public void BalancedTransferUsesTwoSeparateAccountsAndVersionedStatus()
    {
        var fake = new FakeDataverse();
        var adapter = new DataverseCashLedgerTransaction(fake, User, Currency);
        var plan = Prepare(CashOperationKind.InternalTransfer);
        var headerId = adapter.CreateHeader(plan);
        var legs = plan.Legs.Select((leg, i) =>
            adapter.CreatePosting(headerId, plan, leg, i)).ToArray();
        Assert.Equal(2, legs.Distinct().Count());
        Assert.Equal("fmg_cashtransfer", fake.Store[headerId].LogicalName);
        Assert.Equal(AccountA, fake.Store[headerId].GetAttributeValue<EntityReference>("fmg_sourceaccount").Id);
        Assert.Equal(AccountB, fake.Store[headerId].GetAttributeValue<EntityReference>("fmg_destinationaccount").Id);
        adapter.CompleteHeader(headerId, plan.Fingerprint, legs);
        Assert.Equal(797281101, fake.Store[headerId].GetAttributeValue<OptionSetValue>("fmg_status").Value);
        var actual = adapter.ReadCompleteLedger(headerId);
        CashExecutionIntegrity.Verify(plan, headerId, legs, actual);
        Assert.Equal(0m, actual.Sum(x => x.SignedAmountPen));
        Assert.All(actual, x => Assert.Equal(797281202,
            fake.Store[x.PostingId].GetAttributeValue<OptionSetValue>("fmg_kind").Value));
    }

    [Fact]
    public void CompletedRequestHasRecoverableImmutableFingerprintAndUser()
    {
        var fake = new FakeDataverse();
        var adapter = new DataverseCashLedgerTransaction(fake, User, Currency);
        var plan = Prepare(CashOperationKind.SettleReceipt);
        var id = adapter.CreateHeader(plan);
        var leg = adapter.CreatePosting(id, plan, plan.Legs[0], 0);
        adapter.CompleteHeader(id, plan.Fingerprint, new[] { leg });
        var record = adapter.FindCommittedByKey(plan.Kind, plan.RequestKey);
        Assert.NotNull(record);
        Assert.Equal(id, record!.OperationId);
        Assert.Equal(plan.Fingerprint, record.Fingerprint);
        Assert.Equal(User, record.SubjectId);
        Assert.Equal(id, CashOperationPolicy.ResolveRetry(plan, record));
    }

    [Fact]
    public void DraftHeadersCannotMasqueradeAsCompletedRetry()
    {
        var fake = new FakeDataverse();
        var adapter = new DataverseCashLedgerTransaction(fake, User, Currency);
        var plan = Prepare(CashOperationKind.SettleReceipt);
        adapter.CreateHeader(plan);
        Assert.Equal("F5E004", Assert.Throws<CashPlanningException>(() =>
            adapter.FindCommittedByKey(plan.Kind, plan.RequestKey)).Code);
    }

    [Fact]
    public void MissingRowVersionCannotCompleteAnyPosting()
    {
        var fake = new FakeDataverse { MissingRowVersion = true };
        var adapter = new DataverseCashLedgerTransaction(fake, User, Currency);
        var plan = Prepare(CashOperationKind.SettleReceipt);
        var id = adapter.CreateHeader(plan);
        var posting = adapter.CreatePosting(id, plan, plan.Legs[0], 0);
        Assert.Equal("F5E003", Assert.Throws<CashPlanningException>(() =>
            adapter.CompleteHeader(id, plan.Fingerprint, new[] { posting })).Code);
        Assert.Equal(0, fake.ConcurrencyUpdates);
    }

    [Fact]
    public void ReceiptAccountStateMustStillBeActiveDuringHeaderCreation()
    {
        var fake = new FakeDataverse { Inactive = true };
        var adapter = new DataverseCashLedgerTransaction(fake, User, Currency);
        Assert.Equal("F5E002", Assert.Throws<CashPlanningException>(() =>
            adapter.CreateHeader(Prepare(CashOperationKind.SettleReceipt))).Code);
        Assert.Empty(fake.Store);
    }

    [Fact]
    public void ReceiptWithoutBranchIsExplicitSharedScope()
    {
        var fake = new FakeDataverse { Branchless = true };
        var adapter = new DataverseCashLedgerTransaction(fake, User, Currency);
        var id = adapter.CreateHeader(Prepare(CashOperationKind.SettleReceipt));
        Assert.Equal(797281011,
            fake.Store[id].GetAttributeValue<OptionSetValue>("fmg_scope").Value);
        Assert.False(fake.Store[id].Contains("fmg_branch"));
    }

    [Fact]
    public void UnexpectedPostingNeverSucceedsWithDifferentAmount()
    {
        var fake = new FakeDataverse();
        var adapter = new DataverseCashLedgerTransaction(fake, User, Currency);
        var plan = Prepare(CashOperationKind.InternalTransfer);
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            adapter.CreatePosting(Guid.NewGuid(), plan,
                new CashLeg(AccountB, 999m, Day), 0)).Code);
        Assert.Empty(fake.Store);
    }
}
