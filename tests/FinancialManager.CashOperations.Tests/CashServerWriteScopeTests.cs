using System;
using FinancialManager.CashOperations;
using Microsoft.Xrm.Sdk;
using Xunit;

public sealed class CashServerWriteScopeTests
{
    private static readonly Guid User = Guid.Parse("ba4dfd3e-a803-44ef-9fe4-2a7aa73a3824");
    private static readonly Guid Op = Guid.Parse("3c9cddba-6c09-4f4b-9515-fca8d797331d");
    private static readonly Guid Corr = Guid.Parse("a2736266-b350-48a0-b977-ac90c3ca79c0");
    private static readonly Guid Row = Guid.Parse("6c143c8d-ad1f-4b1e-a10c-9cfcf5e30561");

    private static RemoteExecutionContext Parent(string name = "fmg_DecideCashGrantRequest")
    {
        return new RemoteExecutionContext
        {
            MessageName = name, Stage = 30, Mode = 0, Depth = 1,
            IsInTransaction = true, UserId = User, InitiatingUserId = User,
            CorrelationId = Corr, OperationId = Op
        };
    }
    private static RemoteExecutionContext Child(RemoteExecutionContext parent,
        string table = "fmg_cashaccessgrant", string verb = "Update",
        Guid? rowId = null)
    {
        var ctx = new RemoteExecutionContext
        {
            MessageName = verb, PrimaryEntityName = table, Stage = 10,
            Mode = 0, Depth = 2, IsInTransaction = true,
            UserId = User, InitiatingUserId = User,
            CorrelationId = Corr, OperationId = Op,
            ParentContext = parent
        };
        ctx.InputParameters["Target"] = verb == "Delete"
            ? (object)new EntityReference(table, rowId ?? Row)
            : new Entity(table, rowId ?? Row);
        return ctx;
    }

    [Fact]
    public void ExactServerIssuedScopeIsRecognizedInsideNestedTransaction()
    {
        var parent = Parent();
        F5_ServerWriteScope.Issue(parent, "fmg_cashaccessgrant", "Update", Row);
        F5_ServerWriteScope.Require(Child(parent));
    }

    [Fact]
    public void DirectCRUDWithoutParentCannotObtainAuthorization()
    {
        var parent = Parent();
        var child = Child(parent);
        child.ParentContext = null;
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(child));
    }

    [Fact]
    public void CallerTagIsNeverAWriteTicket()
    {
        var parent = Parent();
        parent.SharedVariables["tag"] =
            "v1|forged|fmg_DecideCashGrantRequest|fmg_cashaccessgrant|Update";
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(Child(parent)));
    }

    [Theory]
    [InlineData("fmg_cashaccessgrant", "Delete")]
    [InlineData("fmg_cashreceipt", "Create")]
    [InlineData("fmg_cashgrantrequest", "Create")]
    public void ReviewApiCannotMintOutOfScopeWriteTicket(string table, string verb)
    {
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Issue(Parent(), table, verb, Row));
    }

    [Fact]
    public void AuthorizedScopeCannotBeReusedForOtherRecordOrTable()
    {
        var parent = Parent();
        F5_ServerWriteScope.Issue(parent, "fmg_cashaccessgrant", "Update", Row);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(Child(parent,
                rowId: Guid.NewGuid())));
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(Child(parent,
                "fmg_cashgrantrequest", "Update")));
    }

    [Fact]
    public void ParentMessageAndExecutionMetadataMustStillMatch()
    {
        var parent = Parent();
        F5_ServerWriteScope.Issue(parent, "fmg_cashaccessgrant", "Update", Row);
        parent.MessageName = "SomeUntrustedMessage";
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(Child(parent)));
        parent.MessageName = "fmg_DecideCashGrantRequest";
        var child = Child(parent);
        child.CorrelationId = Guid.NewGuid();
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(child));
    }

    [Fact]
    public void NoStandaloneOrDeeperOrUntrustedNestedUse()
    {
        var parent = Parent();
        F5_ServerWriteScope.Issue(parent, "fmg_cashaccessgrant", "Update", Row);
        var child = Child(parent);
        child.Depth = 3;
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(child));
        child = Child(parent);
        child.IsInTransaction = false;
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(child));
        child = Child(parent);
        child.UserId = Guid.NewGuid();
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(child));
    }

    [Fact]
    public void SynchronousContextDoesNotRequireAnAsyncSystemJob()
    {
        // OperationId identifies a related system job; it is not proof
        // that a synchronous transaction exists and may be Guid.Empty.
        var parent = Parent();
        parent.OperationId = Guid.Empty;
        var child = Child(parent);
        child.OperationId = Guid.Empty;
        F5_ServerWriteScope.Issue(parent, "fmg_cashaccessgrant", "Update", Row);
        F5_ServerWriteScope.Require(child);
        child.OperationId = Guid.NewGuid();
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(child));
    }

    [Fact]
    public void UnknownOrStaleAuthorizationsCannotBeUsed()
    {
        var parent = Parent();
        F5_ServerWriteScope.Issue(parent, "fmg_cashaccessgrant", "Update", Row);
        parent.SharedVariables.Clear();
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Require(Child(parent)));
    }

    [Fact]
    public void SyntheticRollbackApiIsConfinedToItsOwnTestTable()
    {
        var parent = Parent("fmg_CashNestedTrustProbe");
        F5_ServerWriteScope.Issue(parent, "fmg_cashtrustprobe", "Create", Row);
        F5_ServerWriteScope.Require(Child(parent, "fmg_cashtrustprobe", "Create"));
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Issue(parent, "fmg_cashtransfer", "Create", Row));
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Issue(parent, "fmg_cashtrustprobe", "Update", Row));
    }

    [Fact]
    public void FinancialMovementIssueIsCreateOnlyForExactTable()
    {
        var parent = Parent("fmg_PostCashTransfer");
        F5_ServerWriteScope.Issue(parent, "fmg_cashposting", "Create", Row);
        F5_ServerWriteScope.Require(Child(parent, "fmg_cashposting", "Create"));
        Assert.Throws<InvalidPluginExecutionException>(() =>
            F5_ServerWriteScope.Issue(parent, "fmg_cashposting", "Update", Row));
    }
}
