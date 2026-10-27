using System;
using FinancialManager.CashDomain;
using FinancialManager.CashOperations;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

public sealed class DataverseCashGrantRequestReaderTests
{
    private static readonly Guid Id = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Proposer = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid Beneficiary = Guid.Parse("10000000-0000-4000-8000-000000000003");
    private static readonly Guid Approver = Guid.Parse("10000000-0000-4000-8000-000000000004");
    private static readonly Guid Account = Guid.Parse("10000000-0000-4000-8000-000000000005");
    private static readonly DateTime Created = new DateTime(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc);
    private static Entity PendingRow()
    {
        var request = new CashGrantChangeRequest(Id, Beneficiary, Account,
            CashOperationKind.InternalTransfer, CashGrantChangeKind.Grant,
            false, "Auditable synthetic business reason", Created.AddDays(5));
        var planned = CashGrantApprovalPolicy.Prepare(request, Proposer, Created);
        var e = new Entity("fmg_cashgrantrequest", Id) { RowVersion = "8" };
        e["fmg_externalrequestid"] = Id.ToString("D");
        e["fmg_requestfingerprint"] = planned.Fingerprint;
        e["fmg_proposer"] = new EntityReference("systemuser", Proposer);
        e["fmg_beneficiary"] = new EntityReference("systemuser", Beneficiary);
        e["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", Account);
        e["fmg_operation"] = new OptionSetValue(797282001);
        e["fmg_changekind"] = new OptionSetValue(797283100);
        e["fmg_allowshared"] = false;
        e["fmg_reason"] = "Auditable synthetic business reason";
        e["fmg_expiresatutc"] = Created.AddDays(5);
        e["fmg_reviewstatus"] = new OptionSetValue(797283000);
        e["createdon"] = Created;
        return e;
    }

    private sealed class ReadOnlyService : IOrganizationService
    {
        public Entity Row = PendingRow();
        public int Writes;
        public Entity Retrieve(string name, Guid id, ColumnSet columns)
        {
            Assert.Equal("fmg_cashgrantrequest", name);
            Assert.Equal(Id, id);
            Assert.Contains("fmg_requestfingerprint", columns.Columns);
            Assert.Contains("createdon", columns.Columns);
            return Row;
        }
        public Guid Create(Entity entity) { Writes++; throw new InvalidOperationException("Write forbidden"); }
        public void Update(Entity entity) { Writes++; throw new InvalidOperationException("Write forbidden"); }
        public void Delete(string name, Guid id) { Writes++; throw new InvalidOperationException("Write forbidden"); }
        public void Associate(string name, Guid id, Relationship r, EntityReferenceCollection e)
        { Writes++; throw new InvalidOperationException("Write forbidden"); }
        public void Disassociate(string name, Guid id, Relationship r, EntityReferenceCollection e)
        { Writes++; throw new InvalidOperationException("Write forbidden"); }
        public OrganizationResponse Execute(OrganizationRequest request)
        { Writes++; throw new InvalidOperationException("Write forbidden"); }
        public EntityCollection RetrieveMultiple(QueryBase query) =>
            throw new InvalidOperationException("Unexpected query");
    }

    private static string Rejected(ReadOnlyService svc) =>
        Assert.Throws<CashPlanningException>(() =>
            new DataverseCashGrantRequestReader(svc).ReadPending(Id)).Code;

    [Fact]
    public void ExactPendingRowHasImmutableFingerprintAndETag()
    {
        var svc = new ReadOnlyService();
        var result = new DataverseCashGrantRequestReader(svc).ReadPending(Id);
        Assert.Equal("8", result.RowVersion);
        Assert.Equal(Proposer, result.Proposal.ProposerId);
        Assert.Equal(Beneficiary, result.Proposal.BeneficiaryId);
        Assert.Equal(CashGrantChangeKind.Grant, result.Proposal.Change);
        Assert.Equal(64, result.Proposal.Fingerprint.Length);
        Assert.Equal(Approver, CashGrantApprovalPolicy.Approve(
            result.Proposal, Approver, Created.AddHours(1)).ApproverId);
        Assert.Equal(0, svc.Writes);
    }

    [Fact]
    public void TamperedFingerprintCannotBeApproved()
    {
        var svc = new ReadOnlyService();
        svc.Row["fmg_requestfingerprint"] = new string('A', 64);
        Assert.Equal("F5Q001", Rejected(svc));
    }

    [Fact]
    public void ChangedAccountOrBeneficiaryCannotBeApproved()
    {
        var svc = new ReadOnlyService();
        svc.Row["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", Guid.NewGuid());
        Assert.Equal("F5Q001", Rejected(svc));
        svc = new ReadOnlyService();
        svc.Row["fmg_beneficiary"] = new EntityReference("systemuser", Guid.NewGuid());
        Assert.Equal("F5Q001", Rejected(svc));
    }

    [Theory]
    [InlineData(797283001)]
    [InlineData(797283002)]
    [InlineData(797283003)]
    public void OnlyPendingCanBeLoadedForIndependentApproval(int status)
    {
        var svc = new ReadOnlyService();
        svc.Row["fmg_reviewstatus"] = new OptionSetValue(status);
        Assert.Equal("F5Q001", Rejected(svc));
    }

    [Fact]
    public void CompletedAtOrApproverCannotAppearOnPendingRequest()
    {
        var svc = new ReadOnlyService();
        svc.Row["fmg_approver"] = new EntityReference("systemuser", Approver);
        Assert.Equal("F5Q001", Rejected(svc));
        svc = new ReadOnlyService();
        svc.Row["fmg_decidedatutc"] = Created.AddMinutes(2);
        Assert.Equal("F5Q001", Rejected(svc));
    }

    [Fact]
    public void MissingRowVersionAndWrongExternalKeyAreDenied()
    {
        var svc = new ReadOnlyService();
        svc.Row.RowVersion = null;
        Assert.Equal("F5Q001", Rejected(svc));
        svc = new ReadOnlyService();
        svc.Row["fmg_externalrequestid"] = "forged";
        Assert.Equal("F5Q001", Rejected(svc));
    }

    [Fact]
    public void UnknownOperationOrChangeChoiceFailsClosed()
    {
        var svc = new ReadOnlyService();
        svc.Row["fmg_operation"] = new OptionSetValue(111);
        Assert.Equal("F5Q001", Rejected(svc));
        svc = new ReadOnlyService();
        svc.Row["fmg_changekind"] = new OptionSetValue(999);
        Assert.Equal("F5Q001", Rejected(svc));
    }

    [Fact]
    public void RequestWithNonUtcLifetimeOrUnrecognizedReasonDoesNotPassHash()
    {
        var svc = new ReadOnlyService();
        svc.Row["fmg_reason"] = "Different reason than approved";
        Assert.Equal("F5Q001", Rejected(svc));
    }

    [Fact]
    public void ARecipientOrProposerCannotApproveTheIndependentlyReadRequest()
    {
        var proposal = new DataverseCashGrantRequestReader(new ReadOnlyService())
            .ReadPending(Id).Proposal;
        Assert.Equal("F5A011", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Approve(proposal, Proposer, Created.AddHours(1))).Code);
        Assert.Equal("F5A011", Assert.Throws<CashPlanningException>(() =>
            CashGrantApprovalPolicy.Approve(proposal, Beneficiary, Created.AddHours(1))).Code);
    }
}
