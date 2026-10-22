using System;
using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class BudgetLiveVerifier
{
    private const string Budget="fmg_budget", Allocation="fmg_budgetallocation", Event="fmg_activityevent";
    private const int Draft=797280400, Submitted=797280401, Approved=797280402, Rejected=797280403;
    private const int Branch=797280300;
    public static int Run(ServiceClient svc)
    {
        var bq=new QueryExpression("fmg_branch"){ColumnSet=new ColumnSet("fmg_branchid"),TopCount=1};
        bq.Criteria.AddCondition("fmg_isactive",ConditionOperator.Equal,true);
        var branches=svc.RetrieveMultiple(bq).Entities;
        if(branches.Count<1) throw new InvalidOperationException("Live verification needs one active Branch.");

        var cq=new QueryExpression("fmg_category"){ColumnSet=new ColumnSet("fmg_categoryid"),TopCount=1};
        cq.Criteria.AddCondition("fmg_isactive",ConditionOperator.Equal,true);
        cq.Criteria.AddCondition("fmg_isgroup",ConditionOperator.Equal,false);
        cq.Criteria.AddCondition("fmg_parentcategory",ConditionOperator.NotNull);
        cq.Criteria.AddCondition("fmg_code",ConditionOperator.NotEqual,"999");
        var cats=svc.RetrieveMultiple(cq).Entities;
        if(cats.Count<1) throw new InvalidOperationException("Live verification needs one active leaf Category.");

        var pq=new QueryExpression("transactioncurrency"){ColumnSet=new ColumnSet("transactioncurrencyid"),TopCount=1};
        pq.Criteria.AddCondition("isocurrencycode",ConditionOperator.Equal,"PEN");
        var pen=svc.RetrieveMultiple(pq).Entities;
        if(pen.Count!=1) throw new InvalidOperationException("Live verification needs a PEN currency.");

        Entity NewBudget(Guid id)
        {
            var b=new Entity(Budget){Id=id};
            b["fmg_name"]="[FM-BG-ROLLBACK-TEST] 2026 annual";
            b["fmg_status"]=new OptionSetValue(Draft);
            b["fmg_scope"]=new OptionSetValue(Branch);
            b["fmg_branch"]=new EntityReference("fmg_branch",branches[0].Id);
            b["fmg_year"]=2026;
            b["fmg_version"]=31567;
            b["fmg_effectivefrommonth"]=10;
            return b;
        }
        Entity NewAllocation(Guid id,Guid budgetId)
        {
            var a=new Entity(Allocation){Id=id};
            a["fmg_name"]="[FM-BG-ROLLBACK-TEST] category/month";
            a["fmg_budget"]=new EntityReference(Budget,budgetId);
            a["fmg_month"]=10;
            a["fmg_category"]=new EntityReference("fmg_category",cats[0].Id);
            a["fmg_amount"]=new Money(100.05m);
            a["transactioncurrencyid"]=new EntityReference("transactioncurrency",pen[0].Id);
            return a;
        }
        Entity ChangeStatus(Guid id,int status)
        {
            var e=new Entity(Budget){Id=id};
            e["fmg_status"]=new OptionSetValue(status);
            return e;
        }
        CreateRequest Create(Entity e)=>new CreateRequest{Target=e};
        UpdateRequest Update(Entity e)=>new UpdateRequest{Target=e};
        int checks=0;
        void Check(string label,string faultCode,params OrganizationRequest[] requests)
        {
            var transaction=new ExecuteTransactionRequest{
                Requests=new OrganizationRequestCollection(),ReturnResponses=false
            };
            foreach(var request in requests) transaction.Requests.Add(request);
            // A guaranteed-invalid create request makes a successful scenario atomic and rollback-only.
            transaction.Requests.Add(Create(RejectedSentinel()));
            string error="";int index=-1;
            try
            {
                svc.Execute(transaction);
                throw new InvalidOperationException("UNSAFE: expected rollback sentinel, but transaction committed.");
            }
            catch(FaultException<OrganizationServiceFault> ex)
            {
                error=ex.ToString();
                if(ex.Detail is ExecuteTransactionFault details)
                    index=details.FaultedRequestIndex;
            }
            if(faultCode=="SENTINEL")
            {
                if(index!=transaction.Requests.Count-1 || !error.Contains("BGV001"))
                    throw new InvalidOperationException(label+" did not reach the rollback sentinel. Request index="+index+
                        " Fault: "+error.Substring(0,Math.Min(500,error.Length)));
            }
            else if(index<0 || index>=transaction.Requests.Count-1 || !error.Contains(faultCode))
                throw new InvalidOperationException(label+" expected "+faultCode+" before sentinel; index="+index+
                    " fault="+error.Substring(0,Math.Min(500,error.Length)));
            Console.WriteLine("PASS "+label+" (transaction rolled back)");
            checks++;
        }
        // Avoid attributes added to an already-constructed Entity via invalid object initializer.
        Entity RejectedSentinel()
        {
            var b=NewBudget(Guid.NewGuid());
            b["fmg_status"]=new OptionSetValue(Approved);
            return b;
        }


        var dir=NewBudget(Guid.NewGuid());dir["fmg_status"]=new OptionSetValue(Approved);
        Check("Direct approved budget creation denied","BGV001",Create(dir));

        var ok=Guid.NewGuid();
        Check("Create budget, allocate and submit","SENTINEL",
            Create(NewBudget(ok)),Create(NewAllocation(Guid.NewGuid(),ok)),
            Update(ChangeStatus(ok,Submitted)));

        var empty=Guid.NewGuid();
        Check("Cannot submit empty budget","BGV005",
            Create(NewBudget(empty)),Update(ChangeStatus(empty,Submitted)));

        var appro=Guid.NewGuid();
        Check("Approvals fail closed","BGV009",
            Create(NewBudget(appro)),Create(NewAllocation(Guid.NewGuid(),appro)),
            Update(ChangeStatus(appro,Submitted)),Update(ChangeStatus(appro,Approved)));

        var dupe=Guid.NewGuid();var duplicate=NewAllocation(Guid.NewGuid(),dupe);
        Check("Duplicate month-category allocation denied","BAV005",
            Create(NewBudget(dupe)),Create(NewAllocation(Guid.NewGuid(),dupe)),Create(duplicate));

        var frozen=Guid.NewGuid();
        var altered=NewAllocation(Guid.NewGuid(),frozen);
        Check("Submitted budget allocation mutation denied","BAV006",
            Create(NewBudget(frozen)),Create(NewAllocation(Guid.NewGuid(),frozen)),
            Update(ChangeStatus(frozen,Submitted)),Create(altered));

        var tamper=Guid.NewGuid();
        var badUpdate=new Entity(Budget){Id=tamper};
        badUpdate["fmg_name"]="UNAUTHORIZED AMENDMENT";
        Check("Submitted budget mutation denied","BGV006",
            Create(NewBudget(tamper)),Create(NewAllocation(Guid.NewGuid(),tamper)),
            Update(ChangeStatus(tamper,Submitted)),Update(badUpdate));

        var eventCreate=new Entity(Event){Id=Guid.NewGuid()};
        eventCreate["fmg_name"]="[FM-BG-ROLLBACK-TEST] forged event";
        Check("Direct event creation denied","AEV001",Create(eventCreate));

        var rejected=Guid.NewGuid();
        Check("Admin may reject submitted budget","SENTINEL",
            Create(NewBudget(rejected)),Create(NewAllocation(Guid.NewGuid(),rejected)),
            Update(ChangeStatus(rejected,Submitted)),Update(ChangeStatus(rejected,Rejected)));

        Console.WriteLine("BUDGET_LIVE_CHECKS_PASSED="+checks);
        return checks;
    }
}
