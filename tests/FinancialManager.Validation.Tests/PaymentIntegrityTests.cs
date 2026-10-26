using System;
using System.Collections.Generic;
using FinancialManager.Validation;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace FinancialManager.Validation.Tests
{
    public class PaymentIntegrityTests
    {
        private static readonly Guid Cat = Guid.NewGuid(), Parent = Guid.NewGuid(), Branch = Guid.NewGuid(),
            Other = Guid.NewGuid(), Account = Guid.NewGuid(), Currency = Guid.NewGuid(), Actor = Guid.NewGuid();

        private sealed class Facts : IFinancialFacts
        {
            public bool Admin = true;
            public readonly Dictionary<string, Entity> Rows = new Dictionary<string, Entity>();
            public Entity Get(string name, Guid id, params string[] cols) => Rows.ContainsKey(name + id) ? Rows[name + id] : null;
            public bool IsSystemAdministrator(Guid actor) => Admin && actor == Actor;
            public void Add(Entity row) => Rows[row.LogicalName + row.Id] = row;
        }
        private static Entity R(string table, Guid id) => new Entity(table) { Id = id };
        private static EntityReference Ref(string table, Guid id) => new EntityReference(table, id);
        private static Entity Draft()
        {
            var x = R("fmg_financialtransaction", Guid.NewGuid());
            x["fmg_status"] = new OptionSetValue(FmgOptions.Draft);
            x["fmg_amount"] = new Money(120.25m);
            x["fmg_transactiondate"] = new DateTime(2026, 10, 22);
            x["fmg_scope"] = new OptionSetValue(FmgOptions.Branch);
            x["fmg_flowclass"] = new OptionSetValue(FmgOptions.OperatingPayment);
            x["fmg_category"] = Ref("fmg_category", Cat);
            x["fmg_branch"] = Ref("fmg_branch", Branch);
            x["fmg_cashaccount"] = Ref("fmg_cashaccount", Account);
            x["transactioncurrencyid"] = Ref("transactioncurrency", Currency);
            return x;
        }
        private static Facts Good()
        {
            var f = new Facts();
            var c = R("fmg_category", Cat); c["fmg_code"] = "101"; c["fmg_isactive"] = true;
            c["fmg_isgroup"] = false; c["fmg_parentcategory"] = Ref("fmg_category", Parent); f.Add(c);
            var b = R("fmg_branch", Branch); b["fmg_isactive"] = true; f.Add(b);
            var a = R("fmg_cashaccount", Account); a["fmg_isactive"] = true; a["fmg_branch"] = Ref("fmg_branch", Branch); f.Add(a);
            var cur = R("transactioncurrency", Currency); cur["isocurrencycode"] = "PEN"; f.Add(cur);
            return f;
        }
        private static Entity Update(int status)
        {
            var x = new Entity("fmg_financialtransaction"); x["fmg_status"] = new OptionSetValue(status); return x;
        }
        private static void Pass(Facts facts, string msg, Entity target, Entity old = null)
            => new PaymentIntegrity(facts).Validate(msg, target, old, Actor);
        private static void Reject(string code, Facts facts, string msg, Entity target, Entity old = null)
            => Assert.StartsWith(code, Assert.Throws<InvalidPluginExecutionException>(() => Pass(facts,msg,target,old)).Message);

        [Fact] public void CreateDraft() => Pass(Good(),"Create",Draft());
        [Theory]
        [InlineData(FmgOptions.Posted)] [InlineData(FmgOptions.Voided)]
        public void CannotCreateNonDraft(int status) { var x=Draft();x["fmg_status"]=new OptionSetValue(status);Reject("FTV005",Good(),"Create",x); }
        [Fact] public void EditDraft() { var x=new Entity("fmg_financialtransaction");x["fmg_description"]="draft";Pass(Good(),"Update",x,Draft()); }
        [Fact] public void PostValidDraft() => Pass(Good(),"Update",Update(FmgOptions.Posted),Draft());
        [Fact] public void PostNonAdminDenied() { var f=Good();f.Admin=false;Reject("FTV007",f,"Update",Update(FmgOptions.Posted),Draft()); }
        [Fact] public void NonAdminCanEditDraft() { var f=Good();f.Admin=false;Pass(f,"Update",new Entity("fmg_financialtransaction"),Draft()); }
        [Fact] public void NoDraftToVoid() => Reject("FTV005",Good(),"Update",Update(FmgOptions.Voided),Draft());
        [Fact] public void NoMissingCategory() { var x=Draft();x["fmg_category"]=null;Reject("FTV002",Good(),"Update",Update(FmgOptions.Posted),x); }
        [Theory]
        [InlineData(false,false,"101")] [InlineData(true,true,"101")] [InlineData(true,false,"999")]
        public void CategoryMustBeActiveLeaf(bool active,bool group,string code)
        {
            var f=Good(); var c=f.Get("fmg_category",Cat);c["fmg_isactive"]=active;c["fmg_isgroup"]=group;c["fmg_code"]=code;
            Reject("FTV002",f,"Update",Update(FmgOptions.Posted),Draft());
        }
        [Fact] public void CategoryNeedsParent() { var f=Good();f.Get("fmg_category",Cat)["fmg_parentcategory"]=null;Reject("FTV002",f,"Update",Update(FmgOptions.Posted),Draft()); }
        [Fact] public void InactiveAccount() { var f=Good();f.Get("fmg_cashaccount",Account)["fmg_isactive"]=false;Reject("FTV004",f,"Update",Update(FmgOptions.Posted),Draft()); }
        [Fact] public void WrongAccountBranch() { var f=Good();f.Get("fmg_cashaccount",Account)["fmg_branch"]=Ref("fmg_branch",Other);Reject("FTV004",f,"Update",Update(FmgOptions.Posted),Draft()); }
        [Fact] public void InactiveBranch() { var f=Good();f.Get("fmg_branch",Branch)["fmg_isactive"]=false;Reject("FTV003",f,"Update",Update(FmgOptions.Posted),Draft()); }
        [Fact] public void SharedNoBranch()
        {
            var x=Draft();x["fmg_scope"]=new OptionSetValue(FmgOptions.Shared);
            Reject("FTV003",Good(),"Update",Update(FmgOptions.Posted),x);
        }
        [Fact] public void SharedMustUseSharedAccount()
        {
            var x=Draft();x["fmg_scope"]=new OptionSetValue(FmgOptions.Shared);x["fmg_branch"]=null;
            Reject("FTV004",Good(),"Update",Update(FmgOptions.Posted),x);
        }
        [Fact] public void SharedValid()
        {
            var f=Good();f.Get("fmg_cashaccount",Account)["fmg_branch"]=null;
            var x=Draft();x["fmg_scope"]=new OptionSetValue(FmgOptions.Shared);x["fmg_branch"]=null;
            Pass(f,"Update",Update(FmgOptions.Posted),x);
        }
        // A Draft can be reclassified in either direction, but may only Post
        // with a branch/account combination consistent with its latest scope.
        [Fact] public void BranchToSharedToBranchCanPostWithMatchingAccount()
        {
            var f = Good();
            var sharedAccount = Guid.NewGuid();
            var shared = R("fmg_cashaccount",sharedAccount);
            shared["fmg_isactive"] = true;
            f.Add(shared);
            var original = Draft();
            var toShared = new Entity("fmg_financialtransaction");
            toShared["fmg_scope"] = new OptionSetValue(FmgOptions.Shared);
            toShared["fmg_branch"] = null;
            toShared["fmg_cashaccount"] = Ref("fmg_cashaccount",sharedAccount);
            Pass(f,"Update",toShared,original);
            var nowShared = Draft();
            nowShared["fmg_scope"] = new OptionSetValue(FmgOptions.Shared);
            nowShared["fmg_branch"] = null;
            nowShared["fmg_cashaccount"] = Ref("fmg_cashaccount",sharedAccount);
            var toBranch = new Entity("fmg_financialtransaction");
            toBranch["fmg_scope"] = new OptionSetValue(FmgOptions.Branch);
            toBranch["fmg_branch"] = Ref("fmg_branch",Branch);
            toBranch["fmg_cashaccount"] = Ref("fmg_cashaccount",Account);
            Pass(f,"Update",toBranch,nowShared);
            Pass(f,"Update",Update(FmgOptions.Posted),Draft());
        }

        [Fact] public void SharedToPostedWorksOnlyWhenBranchAndAccountAreCleared()
        {
            var f = Good();
            var sharedAccount = Guid.NewGuid();
            var shared = R("fmg_cashaccount",sharedAccount);
            shared["fmg_isactive"] = true;
            f.Add(shared);
            var record = Draft();
            record["fmg_scope"] = new OptionSetValue(FmgOptions.Shared);
            record["fmg_branch"] = null;
            record["fmg_cashaccount"] = Ref("fmg_cashaccount",sharedAccount);
            Pass(f,"Update",Update(FmgOptions.Posted),record);
            record["fmg_branch"] = Ref("fmg_branch",Branch);
            Reject("FTV003",f,"Update",Update(FmgOptions.Posted),record);
            record["fmg_branch"] = null;
            record["fmg_cashaccount"] = Ref("fmg_cashaccount",Account);
            Reject("FTV004",f,"Update",Update(FmgOptions.Posted),record);
        }

        [Fact] public void SharedToBranchRequiresActiveBranchAndCompatibleAccount()
        {
            var f = Good();
            var otherBranch = R("fmg_branch",Other);
            otherBranch["fmg_isactive"] = true;
            f.Add(otherBranch);
            var sharedAccount = Guid.NewGuid();
            var shared = R("fmg_cashaccount",sharedAccount);
            shared["fmg_isactive"] = true;
            f.Add(shared);
            var previous = Draft();
            previous["fmg_scope"] = new OptionSetValue(FmgOptions.Shared);
            previous["fmg_branch"] = null;
            previous["fmg_cashaccount"] = Ref("fmg_cashaccount",sharedAccount);
            var toBranch = new Entity("fmg_financialtransaction");
            toBranch["fmg_scope"] = new OptionSetValue(FmgOptions.Branch);
            toBranch["fmg_cashaccount"] = Ref("fmg_cashaccount",Account);
            Pass(f,"Update",toBranch,previous); // Draft may remain partial.
            var invalidPost = Draft();
            invalidPost["fmg_branch"] = null;
            Reject("FTV003",f,"Update",Update(FmgOptions.Posted),invalidPost);
            invalidPost["fmg_branch"] = Ref("fmg_branch",Other);
            Reject("FTV004",f,"Update",Update(FmgOptions.Posted),invalidPost);
        }

        [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(12.345)]
        public void PositiveTwoDecimalAmount(double number)
        {
            var x=Draft();x["fmg_amount"]=new Money((decimal)number);
            Reject("FTV001",Good(),"Update",Update(FmgOptions.Posted),x);
        }
        [Fact] public void CurrencyMustBePEN()
        {
            var f=Good();f.Get("transactioncurrency",Currency)["isocurrencycode"]="USD";
            Reject("FTV001",f,"Update",Update(FmgOptions.Posted),Draft());
        }
        [Fact] public void DateRequired() { var x=Draft();x["fmg_transactiondate"]=null;Reject("FTV001",Good(),"Update",Update(FmgOptions.Posted),x); }
        [Fact] public void PostedCannotBeEdited()
        {
            var before=Draft();before["fmg_status"]=new OptionSetValue(FmgOptions.Posted);
            var x=new Entity("fmg_financialtransaction");x["fmg_description"]="changed";
            Reject("FTV005",Good(),"Update",x,before);
        }
        [Fact] public void VoidCannotChangeAmount()
        {
            var old=Draft();old["fmg_status"]=new OptionSetValue(FmgOptions.Posted);
            var x=Update(FmgOptions.Voided);x["fmg_voidreason"]="Correction";x["fmg_amount"]=new Money(1);
            Reject("FTV006",Good(),"Update",x,old);
        }
        [Fact] public void VoidNeedsReason()
        {
            var old=Draft();old["fmg_status"]=new OptionSetValue(FmgOptions.Posted);
            var x=Update(FmgOptions.Voided);x["fmg_voidreason"]="  ";
            Reject("FTV007",Good(),"Update",x,old);
        }
        [Fact] public void VoidRequiresAdmin()
        {
            var f=Good();f.Admin=false;var old=Draft();old["fmg_status"]=new OptionSetValue(FmgOptions.Posted);
            var x=Update(FmgOptions.Voided);x["fmg_voidreason"]="Correction";Reject("FTV007",f,"Update",x,old);
        }
        [Fact] public void VoidValid()
        {
            var old=Draft();old["fmg_status"]=new OptionSetValue(FmgOptions.Posted);
            var x=Update(FmgOptions.Voided);x["fmg_voidreason"]="Correction";Pass(Good(),"Update",x,old);
        }
        [Fact] public void VoidedIsImmutable()
        {
            var old=Draft();old["fmg_status"]=new OptionSetValue(FmgOptions.Voided);
            Reject("FTV005",Good(),"Update",Update(FmgOptions.Voided),old);
        }
        [Fact] public void DeleteDraftOnly()
        {
            var x=Draft();Pass(Good(),"Delete",new Entity("fmg_financialtransaction"),x);
            x["fmg_status"]=new OptionSetValue(FmgOptions.Posted);
            Reject("FTV008",Good(),"Delete",new Entity("fmg_financialtransaction"),x);
        }
        [Fact] public void MissingImageFailsClosed() => Reject("FTV000",Good(),"Update",Update(FmgOptions.Posted));
    }
}
