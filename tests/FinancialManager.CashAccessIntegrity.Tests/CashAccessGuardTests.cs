using FinancialManager.CashAccessIntegrity;
using Microsoft.Xrm.Sdk;
using Xunit;

public sealed class CashAccessGuardTests
{
    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    [InlineData("Delete")]
    public void EvenAdministratorCannotMutateGrantRowsDirectly(string message)
    {
        var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
            CashAccessWritePolicy.Validate("fmg_cashaccessgrant", message, 10, 0));
        Assert.StartsWith("F5A009", ex.Message);
    }

    [Theory]
    [InlineData("fmg_cashreceipt", "Create", 10, 0)]
    [InlineData("fmg_cashaccessgrant", "Create", 20, 0)]
    [InlineData("fmg_cashaccessgrant", "Update", 10, 1)]
    [InlineData("fmg_cashaccessgrant", "Read", 10, 0)]
    public void UnexpectedPluginRegistrationFailsClosed(string entity, string message, int stage, int mode)
    {
        var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
            CashAccessWritePolicy.Validate(entity, message, stage, mode));
        Assert.StartsWith("F5A000", ex.Message);
    }
}
