using System;
using FinancialManager.BudgetIntegrity;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace FinancialManager.BudgetIntegrity.Tests
{
    public sealed class BudgetApprovalPreflightTests
    {
        private static readonly Guid Author = Guid.NewGuid();
        private static readonly Guid Owner = Guid.NewGuid();
        private static readonly Guid Approver = Guid.NewGuid();
        private static readonly Guid Submitter = Guid.NewGuid();

        private static Entity Submitted()
        {
            var budget = new Entity("fmg_budget") { Id = Guid.NewGuid() };
            budget["fmg_status"] = new OptionSetValue(BudgetOptions.Submitted);
            budget["createdby"] = new EntityReference("systemuser", Author);
            budget["ownerid"] = new EntityReference("systemuser", Owner);
            budget["modifiedby"] = new EntityReference("systemuser", Submitter);
            budget["fmg_year"] = 2027;
            budget["fmg_version"] = 1;
            budget["fmg_effectivefrommonth"] = 1;
            budget["fmg_scope"] = new OptionSetValue(BudgetOptions.Shared);
            return budget;
        }

        private static void Reject(string code, Entity budget, Guid actor, bool role)
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => new BudgetApprovalPreflight().Validate(budget, actor, role));
            Assert.StartsWith(code, error.Message);
        }

        [Fact]
        public void AuthorizedIndependentReviewerPassesIdentityAndMetadataPreflight()
            => new BudgetApprovalPreflight().Validate(Submitted(), Approver, true);

        [Fact]
        public void BudgetCreatorCannotSelfApprove()
            => Reject("BAP005", Submitted(), Author, true);

        [Fact]
        public void BudgetOwnerCannotSelfApprove()
            => Reject("BAP005", Submitted(), Owner, true);

        [Fact]
        public void BudgetSubmitterCannotApproveEvenIfNotCreatorOrOwner()
            => Reject("BAP005", Submitted(), Submitter, true);

        [Fact]
        public void OnBehalfOfSubmitterCannotApprove()
        {
            var budget = Submitted();
            budget["modifiedonbehalfby"] = new EntityReference("systemuser", Approver);
            Reject("BAP005", budget, Approver, true);
        }

        [Fact]
        public void MissingSubmissionIdentityFailsClosed()
        {
            var budget = Submitted();
            budget.Attributes.Remove("modifiedby");
            Reject("BAP004", budget, Approver, true);
        }

        [Fact]
        public void AdministratorWithoutExplicitApproverRoleIsRejected()
            => Reject("BAP003", Submitted(), Approver, false);

        [Fact]
        public void DraftIsNotEligibleForApproval()
        {
            var budget = Submitted();
            budget["fmg_status"] = new OptionSetValue(BudgetOptions.Draft);
            Reject("BAP002", budget, Approver, true);
        }

        [Fact]
        public void MissingCreatorOrUnknownTeamOwnershipFailClosed()
        {
            var budget = Submitted();
            budget.Attributes.Remove("createdby");
            Reject("BAP004", budget, Approver, true);
            budget = Submitted();
            budget["ownerid"] = new EntityReference("team", Guid.NewGuid());
            Reject("BAP004", budget, Approver, true);
        }

        [Fact]
        public void InvalidEffectiveMonthFailsPreflight()
        {
            var budget = Submitted();
            budget["fmg_effectivefrommonth"] = 13;
            Reject("BAP006", budget, Approver, true);
        }

        [Fact]
        public void SharedBudgetCannotHaveBranch()
        {
            var budget = Submitted();
            budget["fmg_branch"] = new EntityReference("fmg_branch", Guid.NewGuid());
            Reject("BAP006", budget, Approver, true);
        }
    }
}
