using FinancialManager.CashGrantRequestIntegrity;
using Microsoft.Xrm.Sdk;
using Xunit;

public class CashGrantRequestGuardTests
{
    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    [InlineData("Delete")]
    public void EvenPrivilegedUsersCannotWriteGrantRequestsDirectly(string message)
    {
        var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
            GrantRequestWritePolicy.Validate("fmg_cashgrantrequest", message, 10, 0));
        Assert.StartsWith("F5Q009", ex.Message);
    }

    [Theory]
    [InlineData("fmg_cashgrantrequest", "Create", 20, 0)]
    [InlineData("fmg_cashgrantrequest", "Create", 10, 1)]
    [InlineData("fmg_cashaccessgrant", "Create", 10, 0)]
    [InlineData("fmg_cashgrantrequest", "Retrieve", 10, 0)]
    public void UnexpectedRegistrationCannotOpenGrantRequestTable(
        string table, string message, int stage, int mode)
    {
        var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
            GrantRequestWritePolicy.Validate(table, message, stage, mode));
        Assert.StartsWith("F5Q000", ex.Message);
    }
}