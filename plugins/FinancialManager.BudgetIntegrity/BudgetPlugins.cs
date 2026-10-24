using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.BudgetIntegrity
{
    public sealed class BG_EnforceBudgetIntegrity : BudgetPluginBase
    {
        public BG_EnforceBudgetIntegrity(string unsecure, string secure)
            : base(typeof(BG_EnforceBudgetIntegrity)) {}
        protected override string ExpectedTable => "fmg_budget";
    }
    public sealed class BA_EnforceAllocationIntegrity : BudgetPluginBase
    {
        public BA_EnforceAllocationIntegrity(string unsecure, string secure)
            : base(typeof(BA_EnforceAllocationIntegrity)) {}
        protected override string ExpectedTable => "fmg_budgetallocation";
    }
    public sealed class AE_BlockDirectEventWrites : BudgetPluginBase
    {
        public AE_BlockDirectEventWrites(string unsecure, string secure)
            : base(typeof(AE_BlockDirectEventWrites)) {}
        protected override string ExpectedTable => "fmg_activityevent";
    }

    public abstract class BudgetPluginBase : PluginBase
    {
        internal BudgetPluginBase(Type t) : base(t) {}
        protected abstract string ExpectedTable { get; }
        protected override void ExecuteDataversePlugin(ILocalPluginContext local)
        {
            if(local==null) throw new InvalidPluginExecutionException("BGV000: Missing plugin context.");
            var context=local.PluginExecutionContext;
            if(context.Stage!=10 || context.Mode!=0)
                throw new InvalidPluginExecutionException("BGV000: Must execute in synchronous PreValidation.");
            if(!String.Equals(context.PrimaryEntityName,ExpectedTable,StringComparison.OrdinalIgnoreCase))
                throw new InvalidPluginExecutionException("BGV000: Wrong plugin table.");
            if(!context.InputParameters.Contains("Target"))
                throw new InvalidPluginExecutionException("BGV000: Missing Target.");
            Entity target=context.InputParameters["Target"] as Entity;
            if(context.MessageName=="Delete")
            {
                var reference=context.InputParameters["Target"] as EntityReference;
                if(reference==null) throw new InvalidPluginExecutionException("BGV000: Delete target is invalid.");
                target=new Entity(reference.LogicalName) {Id=reference.Id};
            }
            if(target==null) throw new InvalidPluginExecutionException("BGV000: Invalid operation target.");
            Entity before=null;
            if(context.MessageName!="Create" && context.PreEntityImages.Contains("Before"))
                before=context.PreEntityImages["Before"];
            // The approval bypass may originate only from the dedicated server
            // Custom API nested in the same Dataverse transaction. A client cannot
            // supply ParentContext or its SharedVariables via an entity update.
            var parent = context.ParentContext;
            bool trustedApproval = false;
            if (ExpectedTable == "fmg_budget" && context.MessageName == "Update" &&
                parent != null && parent.MessageName == "fmg_ApproveBudget" &&
                parent.IsInTransaction && context.IsInTransaction &&
                parent.InitiatingUserId == context.InitiatingUserId &&
                parent.SharedVariables.Contains("fmg_ApprovalBudgetId"))
            {
                var marker = parent.SharedVariables["fmg_ApprovalBudgetId"] as string;
                trustedApproval = String.Equals(
                    marker, target.Id.ToString("D"), StringComparison.OrdinalIgnoreCase);
            }
            new BudgetGuard(new DataverseBudgetFacts(local.PluginUserService))
                .Validate(ExpectedTable,context.MessageName,target,before,
                    context.InitiatingUserId,trustedApproval);
        }
    }

    internal sealed class DataverseBudgetFacts : IBudgetFacts
    {
        private static readonly Guid SystemAdministratorTemplate =
            new Guid("627090FF-40A3-4053-8790-584EDC5BE201");
        private readonly IOrganizationService svc;
        internal DataverseBudgetFacts(IOrganizationService svc) {this.svc=svc;}

        public Entity Get(string table, Guid id, params string[] fields)
            => svc.Retrieve(table,id,new ColumnSet(fields));

        public bool IsAdministrator(Guid actor)
        {
            if(actor==Guid.Empty) return false;
            var query = new QueryExpression("role") {ColumnSet=new ColumnSet("roleid"),TopCount=1};
            query.Criteria.AddCondition("roletemplateid",ConditionOperator.Equal,SystemAdministratorTemplate);
            var direct=query.AddLink("systemuserroles","roleid","roleid");
            direct.LinkCriteria.AddCondition("systemuserid",ConditionOperator.Equal,actor);
            if(svc.RetrieveMultiple(query).Entities.Count>0) return true;

            var viaTeam = new QueryExpression("role") {ColumnSet=new ColumnSet("roleid"),TopCount=1};
            viaTeam.Criteria.AddCondition("roletemplateid",ConditionOperator.Equal,SystemAdministratorTemplate);
            var membership=viaTeam.AddLink("teamroles","roleid","roleid")
                .AddLink("teammembership","teamid","teamid");
            membership.LinkCriteria.AddCondition("systemuserid",ConditionOperator.Equal,actor);
            return svc.RetrieveMultiple(viaTeam).Entities.Count>0;
        }

        public bool AllocationExists(Guid budgetId)
        {
            var q=new QueryExpression("fmg_budgetallocation") {ColumnSet=new ColumnSet(false),TopCount=1};
            q.Criteria.AddCondition("fmg_budget",ConditionOperator.Equal,budgetId);
            return svc.RetrieveMultiple(q).Entities.Count>0;
        }
        public bool AllocationDuplicate(Guid budgetId,int month,Guid categoryId,Guid excludedId)
        {
            var q=new QueryExpression("fmg_budgetallocation") {ColumnSet=new ColumnSet(false),TopCount=1};
            q.Criteria.AddCondition("fmg_budget",ConditionOperator.Equal,budgetId);
            q.Criteria.AddCondition("fmg_month",ConditionOperator.Equal,month);
            q.Criteria.AddCondition("fmg_category",ConditionOperator.Equal,categoryId);
            if(excludedId!=Guid.Empty)
                q.Criteria.AddCondition("fmg_budgetallocationid",ConditionOperator.NotEqual,excludedId);
            return svc.RetrieveMultiple(q).Entities.Count>0;
        }
        public bool BudgetVersionExists(int year,int scope,Guid? branch,int version,Guid excludedId)
        {
            var q=new QueryExpression("fmg_budget") {ColumnSet=new ColumnSet(false),TopCount=1};
            q.Criteria.AddCondition("fmg_year",ConditionOperator.Equal,year);
            q.Criteria.AddCondition("fmg_scope",ConditionOperator.Equal,scope);
            q.Criteria.AddCondition("fmg_version",ConditionOperator.Equal,version);
            if(branch.HasValue) q.Criteria.AddCondition("fmg_branch",ConditionOperator.Equal,branch.Value);
            else q.Criteria.AddCondition("fmg_branch",ConditionOperator.Null);
            if(excludedId!=Guid.Empty)
                q.Criteria.AddCondition("fmg_budgetid",ConditionOperator.NotEqual,excludedId);
            return svc.RetrieveMultiple(q).Entities.Count>0;
        }
    }
}
