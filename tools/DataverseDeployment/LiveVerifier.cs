using System;
using System.Collections.Generic;
using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class LiveVerifier
{
    private const string Table = "fmg_financialtransaction";
    private const int Draft = 797280000, Posted = 797280001, Voided = 797280002;
    private const int Branch = 797280000, Operating = 797280000;

    public static int Run(ServiceClient client)
    {
        // All cases run in ExecuteTransaction, with a deliberately impossible sentinel operation
        // at the end. Both passed and rejected cases therefore leave NO test transactions.
        var qAccount = new QueryExpression("fmg_cashaccount") { ColumnSet = new ColumnSet("fmg_cashaccountid", "fmg_branch", "fmg_isactive", "fmg_type"), TopCount = 25 };
        qAccount.Criteria.AddCondition("fmg_isactive", ConditionOperator.Equal, true);
        qAccount.Criteria.AddCondition("fmg_branch", ConditionOperator.NotNull);
        var accounts = client.RetrieveMultiple(qAccount).Entities;
        if (accounts.Count == 0) throw new InvalidOperationException("No active branch account fixture.");

        var account = accounts[0];
        var branchRef = account.GetAttributeValue<EntityReference>("fmg_branch");
        var qCat = new QueryExpression("fmg_category") { ColumnSet = new ColumnSet("fmg_categoryid"), TopCount = 5 };
        qCat.Criteria.AddCondition("fmg_isactive", ConditionOperator.Equal, true);
        qCat.Criteria.AddCondition("fmg_isgroup", ConditionOperator.Equal, false);
        qCat.Criteria.AddCondition("fmg_code", ConditionOperator.NotEqual, "999");
        qCat.Criteria.AddCondition("fmg_parentcategory", ConditionOperator.NotNull);
        var cats = client.RetrieveMultiple(qCat).Entities;
        if(cats.Count==0) throw new InvalidOperationException("No active leaf category fixture.");

        var qCur = new QueryExpression("transactioncurrency") { ColumnSet = new ColumnSet("transactioncurrencyid"), TopCount = 2 };
        qCur.Criteria.AddCondition("isocurrencycode", ConditionOperator.Equal, "PEN");
        var curr = client.RetrieveMultiple(qCur).Entities;
        if(curr.Count!=1) throw new InvalidOperationException("Exactly one PEN currency is required.");

        Entity NewDraft(Guid id)
        {
            var payment = new Entity(Table) { Id = id };
            payment["fmg_status"] = new OptionSetValue(Draft);
            payment["fmg_description"] = "[FM-AUTO-TEST] rollback-required";
            payment["fmg_transactiondate"] = new DateTime(2026,10,22);
            payment["fmg_amount"] = new Money(12.34m);
            payment["fmg_flowclass"] = new OptionSetValue(Operating);
            payment["fmg_scope"] = new OptionSetValue(Branch);
            payment["fmg_branch"] = new EntityReference("fmg_branch", branchRef.Id);
            payment["fmg_category"] = new EntityReference("fmg_category", cats[0].Id);
            payment["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", account.Id);
            payment["transactioncurrencyid"] = new EntityReference("transactioncurrency", curr[0].Id);
            return payment;
        }

        Entity Update(Guid id, int status, string? reason=null)
        {
            var row = new Entity(Table) { Id = id };
            row["fmg_status"] = new OptionSetValue(status);
            if (reason != null) row["fmg_voidreason"] = reason;
            return row;
        }

        var checks = 0;
        void Check(string label, string expectedFault, params OrganizationRequest[] requested)
        {
            var tx = new ExecuteTransactionRequest { Requests = new OrganizationRequestCollection(), ReturnResponses = false };
            foreach(var r in requested) tx.Requests.Add(r);
            // Always-failing final request ensures rollback even if a validation is missing.
            var rollbackSentinel = new Entity(Table);
            rollbackSentinel["fmg_status"] = new OptionSetValue(Voided);
            tx.Requests.Add(new CreateRequest { Target = rollbackSentinel });
            string fault = ""; int faultIndex = -1;
            try { client.Execute(tx); throw new InvalidOperationException("UNSAFE: supposed rollback transaction unexpectedly committed."); }
            catch(FaultException<OrganizationServiceFault> e)
            {
                fault = e.ToString();
                if(e.Detail is ExecuteTransactionFault transactionFault)
                    faultIndex = transactionFault.FaultedRequestIndex;
            }
            if (expectedFault == "SENTINEL")
            {
                if (faultIndex != tx.Requests.Count-1 || !fault.Contains("FTV005"))
                    throw new InvalidOperationException(label + " failed before the rollback sentinel (index="+faultIndex+"): " + fault.Substring(0,Math.Min(fault.Length,500)));
            }
            else if (faultIndex < 0 || faultIndex >= tx.Requests.Count-1 || !fault.Contains(expectedFault))
                throw new InvalidOperationException(label + ": expected " + expectedFault + " before sentinel; index="+faultIndex+" actual: " + fault.Substring(0,Math.Min(fault.Length,550)));
            Console.WriteLine("PASS: "+label+" (transaction rolled back)");
            checks++;
        }

        var direct = NewDraft(Guid.NewGuid());
        direct["fmg_status"] = new OptionSetValue(Posted);
        Check("Direct Posted create rejected", "FTV005", new CreateRequest { Target=direct });

        var idValid = Guid.NewGuid();
        Check("Draft -> Posted -> Voided allowed",
            "SENTINEL", new CreateRequest { Target=NewDraft(idValid) },
            new UpdateRequest { Target=Update(idValid, Posted) },
            new UpdateRequest { Target=Update(idValid, Voided, "Automated test void") });

        var idMut = Guid.NewGuid();
        var alteration = new Entity(Table) { Id = idMut };
        alteration["fmg_amount"] = new Money(99m);
        Check("Posted mutation rejected", "FTV005",
            new CreateRequest{Target=NewDraft(idMut)},
            new UpdateRequest{Target=Update(idMut,Posted)},
            new UpdateRequest{Target=alteration});

        var idDel = Guid.NewGuid();
        Check("Posted deletion rejected","FTV008",
            new CreateRequest{Target=NewDraft(idDel)},
            new UpdateRequest{Target=Update(idDel,Posted)},
            new DeleteRequest{Target=new EntityReference(Table,idDel)});

        var groupsQuery = new QueryExpression("fmg_category") {ColumnSet=new ColumnSet("fmg_categoryid"),TopCount=1};
        groupsQuery.Criteria.AddCondition("fmg_isgroup",ConditionOperator.Equal,true);
        var groups = client.RetrieveMultiple(groupsQuery).Entities;
        if(groups.Count > 0)
        {
            var gid=Guid.NewGuid();var invalid=NewDraft(gid);
            invalid["fmg_category"] = new EntityReference("fmg_category",groups[0].Id);
            Check("Group category rejected","FTV002",new CreateRequest{Target=invalid},
                new UpdateRequest{Target=Update(gid,Posted)});
        }

        var inactiveAccountId=Guid.NewGuid();
        var aid=Guid.NewGuid();
        var inactive = new Entity("fmg_cashaccount") {Id=inactiveAccountId};
        inactive["fmg_name"]="[FM-AUTO-TEST] temporary inactive";
        inactive["fmg_isactive"]=false;
        inactive["fmg_type"]=account.GetAttributeValue<OptionSetValue>("fmg_type");
        inactive["fmg_branch"]=new EntityReference("fmg_branch",branchRef.Id);
        var paymentForInactive=NewDraft(aid);
        paymentForInactive["fmg_cashaccount"]=new EntityReference("fmg_cashaccount",inactiveAccountId);
        Check("Inactive account rejected","FTV004",
            new CreateRequest{Target=inactive},
            new CreateRequest{Target=paymentForInactive},
            new UpdateRequest{Target=Update(aid,Posted)});

        Console.WriteLine("LIVE_CHECKS_PASSED="+checks);
        return checks;
    }
}
