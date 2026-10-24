using System;
using System.Collections.Generic;
using FinancialManager.BudgetIntegrity;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace FinancialManager.BudgetIntegrity.Tests
{
    public sealed class BudgetApprovalOperationTests
    {
        private static readonly Guid Creator = Guid.NewGuid();
        private static readonly Guid Approver = Guid.NewGuid();

        private sealed class FakeService : IOrganizationService
        {
            public readonly Dictionary<Guid, Entity> Budgets = new Dictionary<Guid, Entity>();
            public readonly HashSet<string> Claims = new HashSet<string>();
            public bool HasRole = true;
            public bool HasLines = true;
            public int Updates;

            public Guid Create(Entity entity)
            {
                if (entity.LogicalName != "fmg_budgetapprovalclaim")
                    throw new Exception("Unexpected table.");
                var key = entity.GetAttributeValue<string>("fmg_scopekey");
                if (!Claims.Add(key))
                    throw new InvalidPluginExecutionException("Unique approval key conflict.");
                return Guid.NewGuid();
            }

            public Entity Retrieve(string entityName, Guid id, ColumnSet columns)
            {
                if (entityName != "fmg_budget" || !Budgets.TryGetValue(id, out var budget))
                    throw new Exception("Unrecognized budget.");
                return budget;
            }

            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                var table = ((QueryExpression)query).EntityName;
                bool found = table == "role" ? HasRole :
                    table == "fmg_budgetallocation" && HasLines;
                var result = new EntityCollection();
                if (found) result.Entities.Add(new Entity(table) { Id = Guid.NewGuid() });
                return result;
            }

            public OrganizationResponse Execute(OrganizationRequest request)
            {
                var update = Assert.IsType<UpdateRequest>(request);
                Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
                Assert.False(String.IsNullOrWhiteSpace(update.Target.RowVersion));
                Assert.Equal(BudgetOptions.Approved,
                    update.Target.GetAttributeValue<OptionSetValue>("fmg_status").Value);
                Assert.Equal(Approver,
                    update.Target.GetAttributeValue<EntityReference>("fmg_approvedby").Id);
                Updates++;
                return new UpdateResponse();
            }

            public void Update(Entity entity) => throw new NotSupportedException();
            public void Delete(string entityName, Guid id) => throw new NotSupportedException();
            public void Associate(string entityName, Guid entityId,
                Relationship relationship, EntityReferenceCollection related)
                => throw new NotSupportedException();
            public void Disassociate(string entityName, Guid entityId,
                Relationship relationship, EntityReferenceCollection related)
                => throw new NotSupportedException();

            public Guid AddBudget(int year, int version)
            {
                var id = Guid.NewGuid();
                var budget = new Entity("fmg_budget") { Id = id };
                budget.RowVersion = "17";
                budget["fmg_status"] = new OptionSetValue(BudgetOptions.Submitted);
                budget["fmg_year"] = year;
                budget["fmg_version"] = version;
                budget["fmg_effectivefrommonth"] = 1;
                budget["fmg_scope"] = new OptionSetValue(BudgetOptions.Shared);
                budget["createdby"] = new EntityReference("systemuser", Creator);
                budget["ownerid"] = new EntityReference("systemuser", Creator);
                Budgets.Add(id, budget);
                return id;
            }
        }

        [Fact]
        public void ApprovalRequiresAnActiveTransaction()
        {
            var service = new FakeService();
            var operation = new BudgetApprovalOperation(service);
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => operation.Execute(Guid.NewGuid(), Approver, false, DateTime.UtcNow));
            Assert.StartsWith("BAP010", error.Message);
            Assert.Empty(service.Claims);
        }

        [Fact]
        public void TwoVersionsCannotClaimTheSameAnnualScope()
        {
            var service = new FakeService();
            var first = service.AddBudget(2027, 1);
            var second = service.AddBudget(2027, 2);
            var operation = new BudgetApprovalOperation(service);
            operation.Execute(first, Approver, true, DateTime.UtcNow);
            Assert.Single(service.Claims);
            Assert.Equal(1, service.Updates);
            Assert.Throws<InvalidPluginExecutionException>(
                () => operation.Execute(second, Approver, true, DateTime.UtcNow));
            Assert.Equal(1, service.Updates);
        }

        [Fact]
        public void AUserWithoutTheSpecificApproverRoleCannotClaim()
        {
            var service = new FakeService { HasRole = false };
            var id = service.AddBudget(2027, 1);
            Assert.Throws<InvalidPluginExecutionException>(
                () => new BudgetApprovalOperation(service)
                    .Execute(id, Approver, true, DateTime.UtcNow));
            Assert.Empty(service.Claims);
        }

        [Fact]
        public void NoAllocationsMeansNoApprovalClaim()
        {
            var service = new FakeService { HasLines = false };
            var id = service.AddBudget(2027, 1);
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => new BudgetApprovalOperation(service)
                    .Execute(id, Approver, true, DateTime.UtcNow));
            Assert.StartsWith("BAP012", error.Message);
            Assert.Empty(service.Claims);
        }

        [Fact]
        public void MissingRowVersionCannotClaim()
        {
            var service = new FakeService();
            var id = service.AddBudget(2027, 1);
            service.Budgets[id].RowVersion = null;
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => new BudgetApprovalOperation(service)
                    .Execute(id, Approver, true, DateTime.UtcNow));
            Assert.StartsWith("BAP011", error.Message);
        }

        [Fact]
        public void AnnualScopeKeyIsStableAndBranchSpecific()
        {
            var branch = Guid.NewGuid();
            Assert.NotEqual(
                BudgetApprovalOperation.ApprovalScopeKey(2027, BudgetOptions.Shared, null),
                BudgetApprovalOperation.ApprovalScopeKey(2027, BudgetOptions.Branch, branch));
            Assert.StartsWith("2027:",
                BudgetApprovalOperation.ApprovalScopeKey(2027, BudgetOptions.Branch, branch));
            Assert.Throws<InvalidPluginExecutionException>(
                () => BudgetApprovalOperation.ApprovalScopeKey(
                    2027, BudgetOptions.Shared, branch));
        }
    }
}
