using System;
using System.Linq;
using FinancialManager.CashDomain;
using FinancialManager.CashOperations;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

public sealed class CashOperationEndpointTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static ParameterCollection Args(bool transfer)
    {
        var values = new ParameterCollection
        {
            { "RequestKey", "test-only-123" },
            { "SourceAccount", new EntityReference("fmg_cashaccount", A) },
            { "Amount", new Money(23.40m) },
            { "ValueDate", new DateTime(2026, 10, 26) },
            { "EvidenceReference", "test-evidence" }
        };
        if (transfer)
            values.Add("DestinationAccount", new EntityReference("fmg_cashaccount", B));
        return values;
    }

    [Fact]
    public void ReceiptInputBindsPENButCannotImpersonatePrincipal()
    {
        var request = F5_CashOperationEndpoint.ParseRequest(
            F5_CashOperationEndpoint.ReceiptMessage, Args(false));
        Assert.Equal(CashOperationKind.SettleReceipt, request.Kind);
        Assert.Equal("PEN", request.Currency);
        Assert.Equal(A, request.SourceAccountId);
        Assert.Null(request.DestinationAccountId);
        Assert.Equal(23.40m, request.AmountPen);
    }

    [Fact]
    public void TransferParsesIndependentSourceAndDestination()
    {
        var request = F5_CashOperationEndpoint.ParseRequest(
            F5_CashOperationEndpoint.TransferMessage, Args(true));
        Assert.Equal(CashOperationKind.InternalTransfer, request.Kind);
        Assert.Equal(B, request.DestinationAccountId);
    }

    [Theory]
    [InlineData("BypassAuthorization")]
    [InlineData("UserId")]
    [InlineData("BranchId")]
    [InlineData("Currency")]
    [InlineData("tag")]
    public void CannotSupplyClientPrivilegesOrUnrecognizedParameters(string parameter)
    {
        var args = Args(true);
        args[parameter] = "forged";
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            F5_CashOperationEndpoint.ParseRequest(
                F5_CashOperationEndpoint.TransferMessage, args)).Code);
    }

    [Fact]
    public void InvalidMessageAndConflictingDestinationAreRejected()
    {
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            F5_CashOperationEndpoint.ParseRequest("fmg_NotAnApi", Args(true))).Code);
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            F5_CashOperationEndpoint.ParseRequest(F5_CashOperationEndpoint.ReceiptMessage,
                Args(true))).Code);
    }

    [Fact]
    public void WrongSDKArgumentTypesCannotBeCoercedFromClientText()
    {
        var args = Args(true);
        args["Amount"] = "23.40";
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            F5_CashOperationEndpoint.ParseRequest(
                F5_CashOperationEndpoint.TransferMessage, args)).Code);
        args = Args(true);
        args["SourceAccount"] = new EntityReference("systemuser", A);
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            F5_CashOperationEndpoint.ParseRequest(
                F5_CashOperationEndpoint.TransferMessage, args)).Code);
    }

    private sealed class CurrencyService : IOrganizationService
    {
        public int Matches;
        public string Currency = "PEN";
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = Assert.IsType<QueryExpression>(query);
            Assert.Equal("transactioncurrency", q.EntityName);
            Assert.Equal("isocurrencycode", q.Criteria.Conditions.Single().AttributeName);
            Assert.Equal("PEN", q.Criteria.Conditions.Single().Values.Single());
            var result = new EntityCollection();
            for (int i = 0; i < Matches; i++)
            {
                var row = new Entity("transactioncurrency", Guid.NewGuid());
                row["isocurrencycode"] = Currency;
                result.Entities.Add(row);
            }
            return result;
        }
        public Guid Create(Entity entity) => throw new InvalidOperationException("Read only");
        public void Update(Entity entity) => throw new InvalidOperationException("Read only");
        public void Delete(string name, Guid id) => throw new InvalidOperationException("Read only");
        public Entity Retrieve(string name, Guid id, ColumnSet columns) =>
            throw new InvalidOperationException("Unexpected retrieve");
        public void Associate(string name, Guid id, Relationship relationship,
            EntityReferenceCollection entities) => throw new InvalidOperationException("Read only");
        public void Disassociate(string name, Guid id, Relationship relationship,
            EntityReferenceCollection entities) => throw new InvalidOperationException("Read only");
        public OrganizationResponse Execute(OrganizationRequest req) =>
            throw new InvalidOperationException("Read only");
    }

    [Fact]
    public void PenMustExistExactlyOnceInTrustedServerMetadata()
    {
        var empty = new CurrencyService();
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            F5_CashOperationEndpoint.FindExactlyOnePenCurrency(empty)).Code);
        var verified = new CurrencyService { Matches = 1 };
        Assert.NotEqual(Guid.Empty, F5_CashOperationEndpoint.FindExactlyOnePenCurrency(verified));
        verified.Matches = 2;
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            F5_CashOperationEndpoint.FindExactlyOnePenCurrency(verified)).Code);
        verified.Matches = 1;
        verified.Currency = "USD";
        Assert.Equal("F5E001", Assert.Throws<CashPlanningException>(() =>
            F5_CashOperationEndpoint.FindExactlyOnePenCurrency(verified)).Code);
    }
}
