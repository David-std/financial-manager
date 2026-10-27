using System;
using FinancialManager.CashTrustProbe;
using Microsoft.Xrm.Sdk;
using Xunit;

public sealed class SyntheticServerOriginTests
{
    private static readonly Guid Pin = Guid.Parse("ce65ac1f-5411-4211-8711-672e81fd6bb3");
    private static readonly Guid User = Guid.Parse("b2670805-0664-4405-972a-0315296138b4");
    private static readonly Guid Correlation = Guid.Parse("b1f3762d-c6d5-4229-93ba-dd30b1e5968d");
    private static RemoteExecutionContext Valid()
    {
        var parent = new RemoteExecutionContext
        {
            MessageName = "fmg_CashNestedTrustProbe", Stage = 30, Mode = 0,
            Depth = 1, IsInTransaction = true, UserId = User,
            InitiatingUserId = User, CorrelationId = Correlation,
            OperationId = Guid.Empty,
            OwningExtension = new EntityReference("sdkmessageprocessingstep", Pin)
        };
        var child = new RemoteExecutionContext
        {
            MessageName = "Create", PrimaryEntityName = "fmg_cashtrustprobe",
            Stage = 10, Mode = 0, Depth = 2, IsInTransaction = true,
            UserId = User, InitiatingUserId = User,
            CorrelationId = Correlation, OperationId = Guid.Empty,
            ParentContext = parent
        };
        child.InputParameters["Target"] =
            new Entity("fmg_cashtrustprobe", Guid.NewGuid());
        return child;
    }
    [Fact]
    public void ExactServerGeneratedMainStepAllowsOnlySyntheticCreate()
    {
        F5_SyntheticServerOriginPolicy.Require(Valid(), Pin);
    }
    [Fact]
    public void DirectCreateCannotClaimCustomApiLineage()
    {
        var child = Valid(); child.ParentContext = null;
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_SyntheticServerOriginPolicy.Require(child, Pin));
    }
    [Fact]
    public void DifferentOwningExtensionFailsEvenWhenApiNameMatches()
    {
        var child = Valid(); child.ParentContext.OwningExtension =
            new EntityReference("sdkmessageprocessingstep", Guid.NewGuid());
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_SyntheticServerOriginPolicy.Require(child, Pin));
    }
    [Fact]
    public void MissingPinnedRegistrationAlwaysFailsClosed()
    {
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_SyntheticServerOriginPolicy.Require(Valid(), Guid.Empty));
    }
    [Theory]
    [InlineData("fmg_cashtransfer")]
    [InlineData("fmg_cashgrantrequest")]
    public void FinancialOrPermissionTablesAreNeverIncluded(string table)
    {
        var child = Valid();
        child.PrimaryEntityName = table;
        child.InputParameters["Target"] = new Entity(table, Guid.NewGuid());
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_SyntheticServerOriginPolicy.Require(child, Pin));
    }
    [Fact]
    public void CallerTagIsNotATrustedExecutionProof()
    {
        var child = Valid(); child.ParentContext = null;
        child.SharedVariables["tag"] = Pin.ToString("D");
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_SyntheticServerOriginPolicy.Require(child, Pin));
    }
    [Fact]
    public void UnassignedTargetIdOrDifferentCorrelationCannotPass()
    {
        var child = Valid();
        child.InputParameters["Target"] = new Entity("fmg_cashtrustprobe");
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_SyntheticServerOriginPolicy.Require(child, Pin));
        child = Valid(); child.CorrelationId = Guid.NewGuid();
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_SyntheticServerOriginPolicy.Require(child, Pin));
    }
    [Fact]
    public void OutOfTransactionOrNonInteractiveChildDepthFails()
    {
        var child = Valid(); child.IsInTransaction = false;
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_SyntheticServerOriginPolicy.Require(child, Pin));
        child = Valid(); child.Depth = 4;
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_SyntheticServerOriginPolicy.Require(child, Pin));
    }
}
