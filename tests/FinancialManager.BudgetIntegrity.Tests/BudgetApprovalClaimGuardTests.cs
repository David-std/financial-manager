using System;
using FinancialManager.BudgetIntegrity;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace FinancialManager.BudgetIntegrity.Tests
{
    public sealed class BudgetApprovalClaimGuardTests
    {
        private static Entity Claim(Guid budgetId)
        {
            var claim = new Entity("fmg_budgetapprovalclaim");
            claim["fmg_scopekey"] = "2027:797280301:shared";
            claim["fmg_budget"] = new EntityReference("fmg_budget", budgetId);
            return claim;
        }

        [Fact]
        public void DirectClaimWritesAreRejected()
        {
            var budget = Guid.NewGuid();
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => BudgetApprovalClaimGuard.Validate(
                    "Create", Claim(budget), null, false, Guid.Empty));
            Assert.StartsWith("BAC003", error.Message);
        }

        [Fact]
        public void TrustedTransactionalClaimIsAllowed()
        {
            var budget = Guid.NewGuid();
            BudgetApprovalClaimGuard.Validate(
                "Create", Claim(budget), "fmg_ApproveBudget", true, budget);
        }

        [Fact]
        public void AClaimForAnotherBudgetIsRejected()
        {
            var budget = Guid.NewGuid();
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => BudgetApprovalClaimGuard.Validate(
                    "Create", Claim(Guid.NewGuid()), "fmg_ApproveBudget", true, budget));
            Assert.StartsWith("BAC004", error.Message);
        }

        [Theory]
        [InlineData("Update")]
        [InlineData("Delete")]
        public void ClaimsAreImmutableEvenFromTrustedApi(string message)
        {
            var budget = Guid.NewGuid();
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => BudgetApprovalClaimGuard.Validate(
                    message, Claim(budget), "fmg_ApproveBudget", true, budget));
            Assert.StartsWith("BAC001", error.Message);
        }

        [Fact]
        public void ClaimsRequireTheCorrectEntityType()
        {
            var budget = Guid.NewGuid();
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => BudgetApprovalClaimGuard.Validate(
                    "Create", new Entity("account"), "fmg_ApproveBudget", true, budget));
            Assert.StartsWith("BAC002", error.Message);
        }
    }
}
