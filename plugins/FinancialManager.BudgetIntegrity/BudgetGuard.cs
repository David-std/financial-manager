using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.BudgetIntegrity
{
    public static class BudgetOptions
    {
        public const int Draft=797280400, Submitted=797280401, Approved=797280402;
        public const int Rejected=797280403, Superseded=797280404;
        public const int Branch=797280300, Shared=797280301;
    }

    public interface IBudgetFacts
    {
        Entity Get(string table, Guid id, params string[] fields);
        bool IsAdministrator(Guid actor);
        bool AllocationExists(Guid budgetId);
        bool AllocationDuplicate(Guid budgetId, int month, Guid categoryId, Guid excludedId);
        bool BudgetVersionExists(int year, int scope, Guid? branch, int version, Guid excludedId);
    }

    /// <summary>
    /// Fail-closed, pure validation for annual budgets and monthly allocations.
    /// Approval is intentionally disabled until the finance-role and concurrency policy is tested.
    /// No method modifies caller data or writes another Dataverse record.
    /// </summary>
    public sealed class BudgetGuard
    {
        private readonly IBudgetFacts facts;
        public BudgetGuard(IBudgetFacts facts) { this.facts=facts ?? throw new ArgumentNullException(nameof(facts)); }

        public void Validate(string table, string message, Entity target, Entity before, Guid actor,
            bool trustedApproval = false)
        {
            if (table == "fmg_budget") ValidateBudget(message,target,before,actor,trustedApproval);
            else if (table == "fmg_budgetallocation") ValidateAllocation(message,target,before,actor);
            else if (table == "fmg_activityevent") Error("AEV001","Activity Event is currently closed to direct writes.");
            else Error("BGV000","Unexpected table.");
        }

        private void ValidateBudget(string message,Entity target,Entity before,Guid actor,
            bool trustedApproval)
        {
            if (message=="Create")
            {
                if (target==null) Error("BGV000","Missing create record.");
                if (Option(target,"fmg_status") != null && Option(target,"fmg_status") != BudgetOptions.Draft)
                    Error("BGV001","New budgets must start as Draft.");
                NoApprovalSpoof(target);
                CheckBudgetFields(target,false);
                return;
            }
            if (before==null) Error("BGV000","Missing Before pre-image for Budget "+message);
            int? oldStatus=Option(before,"fmg_status");
            if (message=="Delete")
            {
                if (oldStatus!=BudgetOptions.Draft) Error("BGV008","Only Draft budgets may be deleted.");
                return;
            }
            if (message!="Update" || target==null) Error("BGV000","Unexpected Budget operation.");
            NoApprovalSpoof(target, trustedApproval);
            var current=Merge(before,target);
            var next=Option(current,"fmg_status");
            if (oldStatus==BudgetOptions.Draft)
            {
                if (next==BudgetOptions.Draft)
                {
                    CheckBudgetFields(current,false);
                    return;
                }
                if (next!=BudgetOptions.Submitted) Error("BGV002","Draft may only transition to Submitted.");
                if (!facts.IsAdministrator(actor) && !UserOwns(before,actor))
                    Error("BGV004","Only the budget owner or an administrator may submit.");
                CheckBudgetFields(current,true);
                if(!facts.AllocationExists(before.Id))
                    Error("BGV005","Cannot submit an empty budget.");
                return;
            }
            if (oldStatus==BudgetOptions.Submitted)
            {
                if (next==BudgetOptions.Approved)
                {
                    if (!trustedApproval || !target.Contains("fmg_status") ||
                        !target.Contains("fmg_approvedby") ||
                        !target.Contains("fmg_approvedon"))
                        Error("BGV009","Direct budget approval is forbidden.");
                    foreach (var field in target.Attributes.Keys)
                    {
                        if (field!="fmg_budgetid" && field!="fmg_status" &&
                            field!="fmg_approvedby" && field!="fmg_approvedon")
                            Error("BGV011","Approval may only set server-managed status and attribution.");
                    }
                    return;
                }
                if (next!=BudgetOptions.Rejected || !target.Contains("fmg_status") ||
                    !facts.IsAdministrator(actor))
                    Error("BGV006","Submitted budgets are immutable except for administrator rejection.");
                foreach(var field in target.Attributes.Keys)
                {
                    // Dataverse SDK may include the primary-key attribute with an Update Target.
                    if(field!="fmg_status" && field!="fmg_budgetid")
                        Error("BGV006","Rejected transitions cannot change budget fields.");
                }
                return;
            }
            if (oldStatus==BudgetOptions.Approved || oldStatus==BudgetOptions.Rejected ||
                oldStatus==BudgetOptions.Superseded)
                Error("BGV007","Approved, rejected, and superseded budgets are immutable.");
            Error("BGV000","Unknown previous Budget status.");
        }

        private void ValidateAllocation(string message,Entity target,Entity before,Guid actor)
        {
            if(message=="Delete")
            {
                if(before==null) Error("BAV000","Missing Before image for Allocation Delete.");
                CheckParentEditable(before,actor);
                return;
            }
            if (target==null || (message!="Create" && message!="Update"))
                Error("BAV000","Unexpected allocation request.");
            if(message=="Update")
            {
                if(before==null) Error("BAV000","Missing Before image for Allocation Update.");
                CheckParentEditable(before,actor);
            }
            var combined=message=="Create" ? target : Merge(before,target);
            var budget=Ref(combined,"fmg_budget");
            if (budget==null) Error("BAV001","Budget is required.");
            CheckParentEditable(combined,actor);
            var month=Number(combined,"fmg_month");
            if(month<1 || month>12) Error("BAV002","Month must be 1 through 12.");
            Money amount=combined.GetAttributeValue<Money>("fmg_amount");
            if(amount==null || amount.Value<0 || decimal.Round(amount.Value,2)!=amount.Value)
                Error("BAV003","Allocated amount must be nonnegative PEN with two decimal places.");
            var categoryRef=Ref(combined,"fmg_category");
            if(categoryRef==null) Error("BAV004","Category is required.");
            var category=Read("fmg_category",categoryRef,"fmg_isactive","fmg_isgroup","fmg_code","fmg_parentcategory");
            if(category.GetAttributeValue<bool?>("fmg_isactive")!=true ||
               category.GetAttributeValue<bool?>("fmg_isgroup")!=false ||
               Ref(category,"fmg_parentcategory")==null ||
               category.GetAttributeValue<string>("fmg_code")=="999")
                Error("BAV004","Category must be an active leaf other than 999.");
            var currencyRef=Ref(combined,"transactioncurrencyid");
            // Fail closed: do not trust a Dataverse user default currency after PreValidation.
            if(currencyRef==null) Error("BAV003","Explicit PEN transaction currency is required.");
            var currency=Read("transactioncurrency",currencyRef,"isocurrencycode");
            if(!String.Equals(currency.GetAttributeValue<string>("isocurrencycode"),"PEN",
                StringComparison.OrdinalIgnoreCase))
                Error("BAV003","Only PEN budget allocations are supported.");
            if (facts.AllocationDuplicate(budget.Id,month.Value,categoryRef.Id,combined.Id))
                Error("BAV005","This budget already allocates the category in the given month.");
        }

        private void CheckParentEditable(Entity allocation,Guid actor)
        {
            var reference=Ref(allocation,"fmg_budget");
            if(reference==null) Error("BAV001","Budget is required.");
            var budget=Read("fmg_budget",reference,"fmg_status","ownerid");
            if(Option(budget,"fmg_status")!=BudgetOptions.Draft)
                Error("BAV006","Allocations may only change while their budget is Draft.");
            if(!facts.IsAdministrator(actor) && !UserOwns(budget,actor))
                Error("BAV007","Only the parent budget owner or an administrator may change its allocations.");
        }

        private void CheckBudgetFields(Entity value,bool complete)
        {
            var year=Number(value,"fmg_year");
            var version=Number(value,"fmg_version");
            var month=Number(value,"fmg_effectivefrommonth");
            var scope=Option(value,"fmg_scope");
            var branch=Ref(value,"fmg_branch");
            if ((year!=null && (year<1 || year>9999)) ||
                (version!=null && (version<1 || version>100000)) ||
                (month!=null && (month<1 || month>12)))
                Error("BGV003","Year, Version, and Effective From Month are outside allowed ranges.");
            if(scope!=null && scope!=BudgetOptions.Branch && scope!=BudgetOptions.Shared)
                Error("BGV003","Scope must be Branch or Shared.");
            if(scope==BudgetOptions.Shared && branch!=null)
                Error("BGV003","Shared budgets cannot have a Branch.");
            if(scope==BudgetOptions.Branch && branch!=null)
            {
                var b=Read("fmg_branch",branch,"fmg_isactive");
                if(b.GetAttributeValue<bool?>("fmg_isactive")!=true)
                    Error("BGV003","Branch is inactive.");
            }
            if (!complete) return;
            if(year==null || version==null || month==null || scope==null ||
               (scope==BudgetOptions.Branch && branch==null))
                Error("BGV003","Year, Scope, Branch when relevant, Version, and Effective From Month are required for submission.");
            if (facts.BudgetVersionExists(year.Value,scope.Value,branch?.Id,version.Value,value.Id))
                Error("BGV010","A budget version with this Year, Scope and Branch already exists.");
        }

        private static bool UserOwns(Entity row,Guid actor)
        {
            var o=Ref(row,"ownerid");
            return actor!=Guid.Empty && o!=null &&
                o.LogicalName=="systemuser" && o.Id==actor;
        }
        private Entity Read(string table,EntityReference reference,params string[] columns)
        {
            if(reference.Id==Guid.Empty || !String.Equals(reference.LogicalName,table,StringComparison.OrdinalIgnoreCase))
                Error("BGV000","Unexpected related record.");
            Entity row=facts.Get(table,reference.Id,columns);
            if(row==null) Error("BGV000","Related record unavailable.");
            return row;
        }
        private static void NoApprovalSpoof(Entity row, bool trustedApproval = false)
        {
            if(!trustedApproval &&
               (row.Contains("fmg_approvedby") || row.Contains("fmg_approvedon")))
                Error("BGV011","Approved By/On are server-managed and may not be supplied.");
        }
        private static Entity Merge(Entity before,Entity change)
        {
            var result=new Entity(before.LogicalName) {Id=before.Id};
            foreach(var kv in before.Attributes) result[kv.Key]=kv.Value;
            foreach(var kv in change.Attributes) result[kv.Key]=kv.Value;
            return result;
        }
        private static int? Option(Entity row,string field) => row?.GetAttributeValue<OptionSetValue>(field)?.Value;
        private static int? Number(Entity row,string field) => row?.GetAttributeValue<int?>(field);
        private static EntityReference Ref(Entity row,string field) => row?.GetAttributeValue<EntityReference>(field);
        private static void Error(string code,string message)
        {
            throw new InvalidPluginExecutionException(code+": "+message);
        }
    }
}
