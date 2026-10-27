using System;
using FinancialManager.CashDomain;
using FinancialManager.CashOperations;
using Microsoft.Xrm.Sdk;
using Xunit;

public sealed class GrantReviewEndpointTests
{
    private static readonly Guid R = Guid.Parse("10000000-0000-4000-8000-000000000071");
    private static readonly Guid U = Guid.Parse("10000000-0000-4000-8000-000000000072");
    private static readonly Guid A = Guid.Parse("10000000-0000-4000-8000-000000000073");

    private static ParameterCollection Submit()
        => new ParameterCollection {
            { "RequestId", R },
            { "Beneficiary", new EntityReference("systemuser", U) },
            { "CashAccount", new EntityReference("fmg_cashaccount", A) },
            { "Operation", new OptionSetValue(797282001) },
            { "ChangeKind", new OptionSetValue(797283100) },
            { "AllowShared", false },
            { "Reason", "Approved synthetic cash access justification" },
            { "ExpiresAtUtc", DateTime.UtcNow.AddDays(7) }
        };

    [Fact]
    public void ExactlyTypedProposalBindsTheImmutableIdentifiers()
    {
        var request = F5_GrantReviewEndpoint.ParseSubmission(Submit());
        Assert.Equal(R, request.RequestId);
        Assert.Equal(U, request.BeneficiaryUserId);
        Assert.Equal(A, request.CashAccountId);
        Assert.Equal(CashOperationKind.InternalTransfer, request.Operation);
        Assert.Equal(CashGrantChangeKind.Grant, request.Change);
        Assert.False(request.ExplicitSharedAccount);
    }

    [Theory]
    [InlineData("Proposer")]
    [InlineData("Approver")]
    [InlineData("UserId")]
    [InlineData("BypassApproval")]
    [InlineData("tag")]
    [InlineData("TenantId")]
    public void CallerCannotSupplyPrincipalOrAuthorizationOverrides(string injected)
    {
        var args = Submit();
        args[injected] = "forged";
        Assert.Equal("F5Q030", Assert.Throws<CashPlanningException>(() =>
            F5_GrantReviewEndpoint.ParseSubmission(args)).Code);
    }

    [Fact]
    public void WrongDataverseTypeAndNonUtcExpiryCannotBeAccepted()
    {
        var args = Submit();
        args["Operation"] = 797282001;
        Assert.Equal("F5Q030", Assert.Throws<CashPlanningException>(() =>
            F5_GrantReviewEndpoint.ParseSubmission(args)).Code);
        args = Submit();
        args["ExpiresAtUtc"] = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        Assert.Equal("F5Q030", Assert.Throws<CashPlanningException>(() =>
            F5_GrantReviewEndpoint.ParseSubmission(args)).Code);
        args = Submit();
        args["Beneficiary"] = new EntityReference("team", U);
        Assert.Equal("F5Q030", Assert.Throws<CashPlanningException>(() =>
            F5_GrantReviewEndpoint.ParseSubmission(args)).Code);
    }

    [Fact]
    public void UnknownFinancialOperationAndReviewChangeFailClosed()
    {
        var args = Submit();
        args["Operation"] = new OptionSetValue(9876543);
        Assert.Equal("F5Q030", Assert.Throws<CashPlanningException>(() =>
            F5_GrantReviewEndpoint.ParseSubmission(args)).Code);
        args = Submit();
        args["ChangeKind"] = new OptionSetValue(9876543);
        Assert.Equal("F5Q030", Assert.Throws<CashPlanningException>(() =>
            F5_GrantReviewEndpoint.ParseSubmission(args)).Code);
    }

    [Fact]
    public void DecisionAcceptsOnlyIdAndBoolean()
    {
        var args = new ParameterCollection { { "RequestId", R }, { "Approved", false } };
        var result = F5_GrantReviewEndpoint.ParseDecision(args);
        Assert.Equal(R, result.Item1);
        Assert.False(result.Item2);
        args["ActingApprover"] = U;
        Assert.Equal("F5Q030", Assert.Throws<CashPlanningException>(() =>
            F5_GrantReviewEndpoint.ParseDecision(args)).Code);
    }

    [Fact]
    public void DecisionCannotUseEmptyRequestIdOrTextApproval()
    {
        var args = new ParameterCollection {
            { "RequestId", Guid.Empty }, { "Approved", true }
        };
        Assert.Equal("F5Q030", Assert.Throws<CashPlanningException>(() =>
            F5_GrantReviewEndpoint.ParseDecision(args)).Code);
        args = new ParameterCollection { { "RequestId", R }, { "Approved", "true" } };
        Assert.Equal("F5Q030", Assert.Throws<CashPlanningException>(() =>
            F5_GrantReviewEndpoint.ParseDecision(args)).Code);
    }
}
