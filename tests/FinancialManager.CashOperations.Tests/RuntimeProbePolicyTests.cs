using System;
using FinancialManager.CashOperations;
using Microsoft.Xrm.Sdk;
using Xunit;

public sealed class RuntimeProbePolicyTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReportsTheActualTransactionState(bool inTransaction)
    {
        var state = F5_RuntimeProbePolicy.Inspect("fmg_CashTransactionReadiness", 30, 0,
            inTransaction, User, User);
        Assert.Equal(inTransaction, state.InTransaction);
        Assert.Equal(30, state.Stage);
        Assert.True(state.SameIdentity);
    }

    [Theory]
    [InlineData("fmg_PostCashTransfer", 30, 0)]
    [InlineData("fmg_CashTransactionReadiness", 20, 0)]
    [InlineData("fmg_CashTransactionReadiness", 30, 1)]
    public void RejectsUnexpectedCalls(string message, int stage, int mode)
    {
        Assert.StartsWith("F5D001", Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_RuntimeProbePolicy.Inspect(message, stage, mode, false, User, User)).Message);
    }

    [Fact]
    public void RequiresNonEmptyInitiator()
    {
        Assert.StartsWith("F5D002", Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_RuntimeProbePolicy.Inspect("fmg_CashTransactionReadiness", 30, 0,
                false, User, Guid.Empty)).Message);
    }

    [Fact]
    public void DoesNotAssumeUserIdentityMatchesInitiator()
    {
        var report = F5_RuntimeProbePolicy.Inspect("fmg_CashTransactionReadiness", 30, 0,
            true, User, Guid.NewGuid());
        Assert.False(report.SameIdentity);
    }
}
