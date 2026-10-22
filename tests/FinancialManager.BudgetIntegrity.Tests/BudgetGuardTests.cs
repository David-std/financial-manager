using System;
using System.Collections.Generic;
using FinancialManager.BudgetIntegrity;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace FinancialManager.BudgetIntegrity.Tests
{
    public class BudgetGuardTests
    {
        static readonly Guid Actor=Guid.NewGuid(), OtherUser=Guid.NewGuid(), BudgetId=Guid.NewGuid(),
            BranchId=Guid.NewGuid(), CategoryId=Guid.NewGuid(), CurrencyId=Guid.NewGuid();
        private sealed class Facts : IBudgetFacts
        {
            public bool Admin=false, HasLines=true, HasDuplicate=false, HasVersion=false;
            public Dictionary<string, Entity> Rows=new Dictionary<string,Entity>();
            public Entity Get(string table,Guid id,params string[] fields)
                => Rows.TryGetValue(table+id,out var e)?e:null;
            public bool IsAdministrator(Guid actor)=>Admin && actor==Actor;
            public bool AllocationExists(Guid budgetId)=>HasLines;
            public bool AllocationDuplicate(Guid b,int m,Guid c,Guid x)=>HasDuplicate;
            public bool BudgetVersionExists(int y,int s,Guid? b,int v,Guid x)=>HasVersion;
            public void Add(Entity e) => Rows[e.LogicalName+e.Id]=e;
        }
        static Entity E(string table,Guid id) => new Entity(table){Id=id};
        static EntityReference R(string table,Guid id) => new EntityReference(table,id);
        static Entity Budget()
        {
            var b=E("fmg_budget",BudgetId);
            b["fmg_status"]=new OptionSetValue(BudgetOptions.Draft);
            b["fmg_name"]="QA annual";
            b["fmg_year"]=2026;
            b["fmg_version"]=1;
            b["fmg_scope"]=new OptionSetValue(BudgetOptions.Branch);
            b["fmg_branch"]=R("fmg_branch",BranchId);
            b["fmg_effectivefrommonth"]=1;
            b["ownerid"]=R("systemuser",Actor);
            return b;
        }
        static Entity Allocation()
        {
            var a=E("fmg_budgetallocation",Guid.NewGuid());
            a["fmg_name"]="QA allocation";
            a["fmg_budget"]=R("fmg_budget",BudgetId);
            a["fmg_month"]=10;
            a["fmg_category"]=R("fmg_category",CategoryId);
            a["fmg_amount"]=new Money(100m);
            a["transactioncurrencyid"]=R("transactioncurrency",CurrencyId);
            return a;
        }
        static Facts Valid()
        {
            var f=new Facts();
            f.Add(Budget());
            var branch=E("fmg_branch",BranchId);branch["fmg_isactive"]=true;f.Add(branch);
            var cat=E("fmg_category",CategoryId);cat["fmg_isactive"]=true;cat["fmg_isgroup"]=false;
            cat["fmg_code"]="101";cat["fmg_parentcategory"]=R("fmg_category",Guid.NewGuid());f.Add(cat);
            var cur=E("transactioncurrency",CurrencyId);cur["isocurrencycode"]="PEN";f.Add(cur);
            return f;
        }
        static void Pass(Facts f,string t,string m,Entity item,Entity old=null)
            => new BudgetGuard(f).Validate(t,m,item,old,Actor);
        static void Reject(string code,Facts f,string t,string m,Entity item,Entity old=null)
            => Assert.StartsWith(code,Assert.Throws<InvalidPluginExecutionException>(()=>Pass(f,t,m,item,old)).Message);
        static Entity Transition(int status)
        {
            var t=E("fmg_budget",BudgetId);t["fmg_status"]=new OptionSetValue(status);return t;
        }
        [Fact] public void DraftCreated() => Pass(Valid(),"fmg_budget","Create",Budget());
        [Theory] [InlineData(BudgetOptions.Submitted)] [InlineData(BudgetOptions.Approved)]
        public void NoDirectSubmitOrApprove(int s)
        {
            var b=Budget();b["fmg_status"]=new OptionSetValue(s);
            Reject("BGV001",Valid(),"fmg_budget","Create",b);
        }
        [Fact] public void ApprovalFieldsCannotBeSpoofed()
        {
            var b=Budget();b["fmg_approvedby"]=R("systemuser",Actor);
            Reject("BGV011",Valid(),"fmg_budget","Create",b);
        }
        [Fact] public void EditableDraft() => Pass(Valid(),"fmg_budget","Update",new Entity("fmg_budget"),Budget());
        [Fact] public void SubmitCompleteBudget() => Pass(Valid(),"fmg_budget","Update",Transition(BudgetOptions.Submitted),Budget());
        [Fact] public void NoSubmitWithoutLines()
        {
            var f=Valid();f.HasLines=false;
            Reject("BGV005",f,"fmg_budget","Update",Transition(BudgetOptions.Submitted),Budget());
        }
        [Fact] public void NoSubmitByUnrelatedUser()
        {
            var b=Budget();b["ownerid"]=R("systemuser",OtherUser);
            Reject("BGV004",Valid(),"fmg_budget","Update",Transition(BudgetOptions.Submitted),b);
        }
        [Fact] public void NoSubmitDuplicateVersion()
        {
            var f=Valid();f.HasVersion=true;
            Reject("BGV010",f,"fmg_budget","Update",Transition(BudgetOptions.Submitted),Budget());
        }
        [Fact] public void AdminMaySubmit()
        {
            var f=Valid();f.Admin=true;var b=Budget();b["ownerid"]=R("systemuser",OtherUser);
            Pass(f,"fmg_budget","Update",Transition(BudgetOptions.Submitted),b);
        }
        [Theory] [InlineData(BudgetOptions.Approved)] [InlineData(BudgetOptions.Superseded)]
        public void CannotJumpFromDraft(int status)
            => Reject("BGV002",Valid(),"fmg_budget","Update",Transition(status),Budget());
        [Fact] public void ApprovalsFailClosedEvenForAdmin()
        {
            var f=Valid();f.Admin=true;var b=Budget();b["fmg_status"]=new OptionSetValue(BudgetOptions.Submitted);
            Reject("BGV009",f,"fmg_budget","Update",Transition(BudgetOptions.Approved),b);
        }
        [Fact] public void SubmittedCannotBeEdited()
        {
            var b=Budget();b["fmg_status"]=new OptionSetValue(BudgetOptions.Submitted);
            var change=E("fmg_budget",BudgetId);change["fmg_name"]="changed";
            Reject("BGV006",Valid(),"fmg_budget","Update",change,b);
        }
        [Fact] public void AdminCanReject()
        {
            var f=Valid();f.Admin=true;var b=Budget();b["fmg_status"]=new OptionSetValue(BudgetOptions.Submitted);
            Pass(f,"fmg_budget","Update",Transition(BudgetOptions.Rejected),b);
        }
        [Fact] public void AdminCanRejectWithDataversePrimaryIdField()
        {
            var f=Valid();f.Admin=true;var b=Budget();b["fmg_status"]=new OptionSetValue(BudgetOptions.Submitted);
            var change=Transition(BudgetOptions.Rejected);change["fmg_budgetid"]=BudgetId;
            Pass(f,"fmg_budget","Update",change,b);
        }
        [Fact] public void AdminCannotAlterFieldsWhileRejecting()
        {
            var f=Valid();f.Admin=true;var b=Budget();b["fmg_status"]=new OptionSetValue(BudgetOptions.Submitted);
            var change=Transition(BudgetOptions.Rejected);change["fmg_year"]=2027;
            Reject("BGV006",f,"fmg_budget","Update",change,b);
        }
        [Fact] public void OrdinaryUserCannotReject()
        {
            var b=Budget();b["fmg_status"]=new OptionSetValue(BudgetOptions.Submitted);
            Reject("BGV006",Valid(),"fmg_budget","Update",Transition(BudgetOptions.Rejected),b);
        }
        [Theory] [InlineData(BudgetOptions.Approved)] [InlineData(BudgetOptions.Rejected)]
        [InlineData(BudgetOptions.Superseded)]
        public void FinalBudgetsImmutable(int status)
        {
            var b=Budget();b["fmg_status"]=new OptionSetValue(status);
            Reject("BGV007",Valid(),"fmg_budget","Update",Transition(BudgetOptions.Draft),b);
        }
        [Fact] public void DraftCanDelete() => Pass(Valid(),"fmg_budget","Delete",E("fmg_budget",BudgetId),Budget());
        [Fact] public void SubmittedCannotDelete()
        {
            var b=Budget();b["fmg_status"]=new OptionSetValue(BudgetOptions.Submitted);
            Reject("BGV008",Valid(),"fmg_budget","Delete",E("fmg_budget",BudgetId),b);
        }
        [Fact] public void CreateValidAllocation() => Pass(Valid(),"fmg_budgetallocation","Create",Allocation());
        [Fact] public void NegativeAllocationDenied()
        {
            var a=Allocation();a["fmg_amount"]=new Money(-0.01m);
            Reject("BAV003",Valid(),"fmg_budgetallocation","Create",a);
        }
        [Fact] public void InvalidMonthDenied()
        {
            var a=Allocation();a["fmg_month"]=13;
            Reject("BAV002",Valid(),"fmg_budgetallocation","Create",a);
        }
        [Fact] public void GroupCategoryDenied()
        {
            var f=Valid();f.Get("fmg_category",CategoryId)["fmg_isgroup"]=true;
            Reject("BAV004",f,"fmg_budgetallocation","Create",Allocation());
        }
        [Fact] public void InactiveCategoryDenied()
        {
            var f=Valid();f.Get("fmg_category",CategoryId)["fmg_isactive"]=false;
            Reject("BAV004",f,"fmg_budgetallocation","Create",Allocation());
        }
        [Fact] public void DuplicateAllocationDenied()
        {
            var f=Valid();f.HasDuplicate=true;
            Reject("BAV005",f,"fmg_budgetallocation","Create",Allocation());
        }
        [Fact] public void ForeignCurrencyDenied()
        {
            var f=Valid();f.Get("transactioncurrency",CurrencyId)["isocurrencycode"]="USD";
            Reject("BAV003",f,"fmg_budgetallocation","Create",Allocation());
        }
        [Fact] public void SubmittedBudgetAllocationImmutable()
        {
            var f=Valid();f.Get("fmg_budget",BudgetId)["fmg_status"]=new OptionSetValue(BudgetOptions.Submitted);
            Reject("BAV006",f,"fmg_budgetallocation","Create",Allocation());
        }
        [Fact] public void AllocationUnrelatedUserDenied()
        {
            var f=Valid();f.Get("fmg_budget",BudgetId)["ownerid"]=R("systemuser",OtherUser);
            Reject("BAV007",f,"fmg_budgetallocation","Create",Allocation());
        }
        [Fact] public void AdminCanEditOthersDraftAllocation()
        {
            var f=Valid();f.Admin=true;f.Get("fmg_budget",BudgetId)["ownerid"]=R("systemuser",OtherUser);
            var a=Allocation();Pass(f,"fmg_budgetallocation","Update",a,a);
        }
        [Fact] public void CannotMoveAllocationOutOfSubmittedBudget()
        {
            var f=Valid();var before=Allocation();
            f.Get("fmg_budget",BudgetId)["fmg_status"]=new OptionSetValue(BudgetOptions.Submitted);
            Reject("BAV006",f,"fmg_budgetallocation","Delete",E("fmg_budgetallocation",before.Id),before);
        }
        [Theory] [InlineData("Create")] [InlineData("Update")] [InlineData("Delete")]
        public void ActivityEventClosed(string operation)
            => Reject("AEV001",Valid(),"fmg_activityevent",operation,E("fmg_activityevent",Guid.NewGuid()));
        [Fact] public void MissingBeforeImageBlocks()
            => Reject("BGV000",Valid(),"fmg_budget","Delete",E("fmg_budget",BudgetId));
        [Fact] public void SharedCannotSelectBranch()
        {
            var b=Budget();b["fmg_scope"]=new OptionSetValue(BudgetOptions.Shared);
            Reject("BGV003",Valid(),"fmg_budget","Create",b);
        }
    }
}
