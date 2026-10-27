using System;
using System.Collections.Generic;
using System.Linq;
using FinancialManager.CashDomain;
using FinancialManager.CashOperations;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

public sealed class DataverseCashAuthorizationTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid A = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid B = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Branch = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateTime Day = new DateTime(2026, 10, 26);
    private static CashOperationRequest Transfer() =>
        new CashOperationRequest(CashOperationKind.InternalTransfer, "synthetic-only",
            A, B, 20m, Day, "PEN", "synthetic");
    private static CashOperationRequest Receipt() =>
        new CashOperationRequest(CashOperationKind.SettleReceipt, "synthetic-only",
            A, null, 20m, Day, "PEN", "synthetic-document");
    private static Entity NewGrant(Guid account, int operation, bool shared = false,
        Guid? user = null, bool active = true)
    {
        var g = new Entity("fmg_cashaccessgrant", Guid.NewGuid());
        g["fmg_principal"] = new EntityReference("systemuser", user ?? User);
        g["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", account);
        g["fmg_operation"] = new OptionSetValue(operation);
        g["fmg_isactive"] = active;
        g["fmg_allowshared"] = shared;
        g["fmg_expiresatutc"] = DateTime.UtcNow.AddDays(3);
        return g;
    }
    private sealed class ReadOnlyFake : IOrganizationService
    {
        public List<Entity> Grants = new List<Entity>();
        public bool Disabled;
        public bool BadBranch;
        public bool SharedAccount;
        public bool MoreRecords;
        public bool InactiveAccount;
        public bool UserMissingState;
        public bool AccountMissingState;
        public int Writes;
        public QueryExpression? CapturedQuery;
        public Entity Retrieve(string table, Guid id, ColumnSet columns)
        {
            if (table == "systemuser")
            {
                var u = new Entity("systemuser", id);
                if (!UserMissingState) u["isdisabled"] = Disabled;
                return u;
            }
            if (table == "fmg_cashaccount")
            {
                var a = new Entity("fmg_cashaccount", id);
                if (!AccountMissingState) a["fmg_isactive"] = !InactiveAccount;
                if (!SharedAccount)
                    a["fmg_branch"] = new EntityReference("fmg_branch", Branch);
                return a;
            }
            if (table == "fmg_branch")
            {
                var b = new Entity("fmg_branch", id);
                b["fmg_isactive"] = !BadBranch;
                return b;
            }
            throw new InvalidOperationException("Unexpected lookup " + table);
        }
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = Assert.IsType<QueryExpression>(query);
            Assert.Equal("fmg_cashaccessgrant", q.EntityName);
            CapturedQuery = q;
            var result = new EntityCollection();
            foreach (var g in Grants) result.Entities.Add(g);
            result.MoreRecords = MoreRecords;
            return result;
        }
        public Guid Create(Entity entity) { Writes++; throw new InvalidOperationException("No writes"); }
        public void Update(Entity entity) { Writes++; throw new InvalidOperationException("No writes"); }
        public void Delete(string entityName, Guid id) { Writes++; throw new InvalidOperationException("No writes"); }
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { Writes++; throw new InvalidOperationException("No writes"); }
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { Writes++; throw new InvalidOperationException("No writes"); }
        public OrganizationResponse Execute(OrganizationRequest request) { Writes++; throw new InvalidOperationException("No executes"); }
    }

    [Fact]
    public void ExactlyScopedUserGrantsPermitTransferWithoutWriting()
    {
        var svc = new ReadOnlyFake();
        svc.Grants.Add(NewGrant(A, 797282001));
        svc.Grants.Add(NewGrant(B, 797282001));
        var actor = new DataverseCashAuthorization(svc).Authorize(User, Transfer());
        Assert.True(actor.CanPerformOnAccount(CashOperationKind.InternalTransfer, A));
        Assert.True(actor.CanPerformOnAccount(CashOperationKind.InternalTransfer, B));
        Assert.False(actor.CanPerformOnAccount(CashOperationKind.SettleReceipt, A));
        Assert.Equal(0, svc.Writes);
        Assert.Equal(User, svc.CapturedQuery!.Criteria.Conditions.Single().Values.Single());
    }

    [Fact]
    public void OtherOperationOnDestinationNeverWidensTransferScope()
    {
        var svc = new ReadOnlyFake();
        svc.Grants.Add(NewGrant(A, 797282001));
        svc.Grants.Add(NewGrant(B, 797282000));
        var actor = new DataverseCashAuthorization(svc).Authorize(User, Transfer());
        Assert.Throws<CashPlanningException>(() => CashOperationPolicy.Prepare(Transfer(), actor));
    }

    [Fact]
    public void NoGrantRecordsDenyEvenAnAuthenticatedAdministrator()
    {
        var actor = new DataverseCashAuthorization(new ReadOnlyFake()).Authorize(User, Receipt());
        Assert.False(actor.CanPerformOnAccount(CashOperationKind.SettleReceipt, A));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DisabledUserOrIncompleteGrantQueryFails(bool disabled, bool moreRecords)
    {
        var svc = new ReadOnlyFake { Disabled = disabled, MoreRecords = moreRecords };
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            new DataverseCashAuthorization(svc).Authorize(User, Receipt())).Code);
    }

    [Fact]
    public void MissingUserStateFailsClosed()
    {
        var svc = new ReadOnlyFake { UserMissingState = true };
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            new DataverseCashAuthorization(svc).Authorize(User, Receipt())).Code);
    }

    [Fact]
    public void InactiveBranchAndAccountDeny()
    {
        foreach (var svc in new[] {
            new ReadOnlyFake { BadBranch = true },
            new ReadOnlyFake { InactiveAccount = true }
        })
        {
            svc.Grants.Add(NewGrant(A, 797282000));
            var actor = new DataverseCashAuthorization(svc).Authorize(User, Receipt());
            Assert.False(actor.CanPerformOnAccount(CashOperationKind.SettleReceipt, A));
        }
    }

    [Fact]
    public void MissingCashAccountActiveFlagFailsClosed()
    {
        var svc = new ReadOnlyFake { AccountMissingState = true };
        svc.Grants.Add(NewGrant(A, 797282000));
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            new DataverseCashAuthorization(svc).Authorize(User, Receipt())).Code);
    }

    [Fact]
    public void SharedAccountNeedsExplicitSharedFlag()
    {
        var svc = new ReadOnlyFake { SharedAccount = true };
        svc.Grants.Add(NewGrant(A, 797282000));
        var denied = new DataverseCashAuthorization(svc).Authorize(User, Receipt());
        Assert.False(denied.CanPerformOnAccount(CashOperationKind.SettleReceipt, A));
        svc.Grants[0]["fmg_allowshared"] = true;
        var approved = new DataverseCashAuthorization(svc).Authorize(User, Receipt());
        Assert.True(approved.CanPerformOnAccount(CashOperationKind.SettleReceipt, A));
    }

    [Fact]
    public void UnknownGrantOperationOrForgedPrincipalFailsClosed()
    {
        var svc = new ReadOnlyFake();
        svc.Grants.Add(NewGrant(A, 999));
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            new DataverseCashAuthorization(svc).Authorize(User, Receipt())).Code);
        svc.Grants.Clear();
        svc.Grants.Add(NewGrant(A, 797282000, user: Other));
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            new DataverseCashAuthorization(svc).Authorize(User, Receipt())).Code);
    }

    [Fact]
    public void ExpiredGrantNeverAuthorizesCashOperation()
    {
        var svc = new ReadOnlyFake();
        svc.Grants.Add(NewGrant(A, 797282000));
        svc.Grants[0]["fmg_expiresatutc"] = DateTime.UtcNow.AddMinutes(-1);
        var actor = new DataverseCashAuthorization(svc).Authorize(User, Receipt());
        Assert.False(actor.CanPerformOnAccount(CashOperationKind.SettleReceipt, A));
        Assert.Equal(0, svc.Writes);
    }

    [Fact]
    public void FarFutureGrantExpiryCannotCreateIndefinitePermission()
    {
        var svc = new ReadOnlyFake();
        svc.Grants.Add(NewGrant(A, 797282000));
        svc.Grants[0]["fmg_expiresatutc"] = DateTime.UtcNow.AddYears(3);
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            new DataverseCashAuthorization(svc).Authorize(User, Receipt())).Code);
        Assert.Equal(0, svc.Writes);
    }

    [Fact]
    public void MissingGrantExpiryFailsClosed()
    {
        var svc = new ReadOnlyFake();
        svc.Grants.Add(NewGrant(A, 797282000));
        svc.Grants[0].Attributes.Remove("fmg_expiresatutc");
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            new DataverseCashAuthorization(svc).Authorize(User, Receipt())).Code);
        Assert.Equal(0, svc.Writes);
    }

    [Fact]
    public void MixedDuplicateEvidenceIsDeniedWithoutMutation()
    {
        var svc = new ReadOnlyFake();
        svc.Grants.Add(NewGrant(A, 797282000));
        svc.Grants.Add(NewGrant(A, 797282000));
        Assert.Equal("F5A001", Assert.Throws<CashPlanningException>(() =>
            new DataverseCashAuthorization(svc).Authorize(User, Receipt())).Code);
        Assert.Equal(0, svc.Writes);
    }
}
