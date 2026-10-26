using FinancialManager.CashIntegrity;
using Microsoft.Xrm.Sdk;
using Xunit;

public sealed class CashWritePolicyTests
{
    [Theory]
    [InlineData("fmg_cashreceipt")][InlineData("fmg_cashtransfer")]
    [InlineData("fmg_cashposting")][InlineData("fmg_accountopening")]
    [InlineData("fmg_bankstatement")][InlineData("fmg_bankreconciliation")]
    public void AllThreeWritesDeniedOnAllSixTables(string table)
    {
        foreach (var message in new[] {"Create", "Update", "Delete"})
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                CashWritePolicy.Validate(table, message, 10, 0));
            Assert.StartsWith("F5G001:", error.Message);
        }
    }
    [Theory]
    [InlineData("fmg_cashposting", "Associate", 10, 0)]
    [InlineData("fmg_cashposting", "Create", 20, 0)]
    [InlineData("fmg_cashposting", "Create", 10, 1)]
    [InlineData("fmg_financialtransaction", "Create", 10, 0)]
    public void WrongRegistrationFailsClosed(string table, string message, int stage, int mode)
    {
        var error = Assert.Throws<InvalidPluginExecutionException>(() =>
            CashWritePolicy.Validate(table, message, stage, mode));
        Assert.StartsWith("F5G000:", error.Message);
    }
    [Fact]
    public void NullServiceProviderFailsClosed()
    {
        var plugin = new F5_BlockDirectCashWrites("", "");
        var error = Assert.Throws<InvalidPluginExecutionException>(() => plugin.Execute(null));
        Assert.StartsWith("F5G000:", error.Message);
    }
}
