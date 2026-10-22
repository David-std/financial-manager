using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using System;
using System.Collections.Generic;
using System.Linq;

internal static class SchemaVerifier
{
    private static readonly Dictionary<string,Dictionary<string,AttributeTypeCode>> Expected = new()
    {
        ["fmg_budget"] = new() {
            ["fmg_name"]=AttributeTypeCode.String, ["fmg_year"]=AttributeTypeCode.Integer,
            ["fmg_scope"]=AttributeTypeCode.Picklist, ["fmg_branch"]=AttributeTypeCode.Lookup,
            ["fmg_version"]=AttributeTypeCode.Integer, ["fmg_effectivefrommonth"]=AttributeTypeCode.Integer,
            ["fmg_status"]=AttributeTypeCode.Picklist, ["fmg_approvedby"]=AttributeTypeCode.Lookup,
            ["fmg_approvedon"]=AttributeTypeCode.DateTime
        },
        ["fmg_budgetallocation"] = new() {
            ["fmg_name"]=AttributeTypeCode.String,["fmg_budget"]=AttributeTypeCode.Lookup,
            ["fmg_month"]=AttributeTypeCode.Integer,["fmg_category"]=AttributeTypeCode.Lookup,
            ["fmg_amount"]=AttributeTypeCode.Money
        },
        ["fmg_activityevent"] = new() {
            ["fmg_name"]=AttributeTypeCode.String,["fmg_eventtype"]=AttributeTypeCode.Picklist,
            ["fmg_recordtype"]=AttributeTypeCode.String,["fmg_recordid"]=AttributeTypeCode.String,
            ["fmg_summary"]=AttributeTypeCode.String,["fmg_actor"]=AttributeTypeCode.Lookup,
            ["fmg_eventtime"]=AttributeTypeCode.DateTime
        }
    };

    private static readonly Dictionary<string,Dictionary<string,string>> LookupTargets = new()
    {
        ["fmg_budget"] = new() {["fmg_branch"]="fmg_branch",["fmg_approvedby"]="systemuser"},
        ["fmg_budgetallocation"] = new() {["fmg_budget"]="fmg_budget",["fmg_category"]="fmg_category"},
        ["fmg_activityevent"] = new() {["fmg_actor"]="systemuser"}
    };
    private static readonly Dictionary<string,(int,string)[]> Options = new()
    {
        ["fmg_budget/fmg_scope"] = new[]{(797280300,"Branch"),(797280301,"Shared")},
        ["fmg_budget/fmg_status"] = new[]{(797280400,"Draft"),(797280401,"Submitted"),
            (797280402,"Approved"),(797280403,"Rejected"),(797280404,"Superseded")},
        ["fmg_activityevent/fmg_eventtype"] = new[]{(797280500,"TransactionCreated"),
            (797280501,"TransactionPosted"),(797280502,"TransactionVoided"),
            (797280503,"BudgetSubmitted"),(797280504,"BudgetApproved"),
            (797280505,"CategoryDeactivated")}
    };

    public static int Run(ServiceClient svc)
    {
        int checkedColumns=0,checkedLookups=0,checkedOptions=0;
        foreach (var table in Expected)
        {
            var entity=((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest{
                LogicalName=table.Key,EntityFilters=EntityFilters.All,RetrieveAsIfPublished=false
            })).EntityMetadata;
            if(entity.OwnershipType != OwnershipTypes.UserOwned)
                throw new InvalidOperationException(table.Key + " must use User or Team ownership.");
            if(entity.IsAuditEnabled?.Value != true)
                throw new InvalidOperationException(table.Key + " must have Dataverse table-level auditing enabled.");
            foreach(var expected in table.Value)
            {
                var a=entity.Attributes.SingleOrDefault(x=>x.LogicalName==expected.Key);
                if(a == null || a.AttributeType != expected.Value)
                    throw new InvalidOperationException(table.Key+"."+expected.Key+" missing/wrong type");
                checkedColumns++;
                if(LookupTargets.TryGetValue(table.Key,out var targets) && targets.TryGetValue(expected.Key,out var parent))
                {
                    var rel=entity.ManyToOneRelationships?.FirstOrDefault(x=>x.ReferencingAttribute==expected.Key &&
                        x.ReferencedEntity==parent);
                    if(rel==null) throw new InvalidOperationException("Missing lookup relation "+table.Key+"."+expected.Key+" -> "+parent);
                    checkedLookups++;
                }
                if(Options.TryGetValue(table.Key+"/"+expected.Key,out var options))
                {
                    var pick=a as PicklistAttributeMetadata;
                    var actual=pick?.OptionSet?.Options;
                    foreach(var option in options)
                    {
                        if(actual == null || !actual.Any(x=>x.Value==option.Item1 &&
                              x.Label.LocalizedLabels.Any(y=>y.Label==option.Item2)))
                            throw new InvalidOperationException("Wrong option "+table.Key+"."+expected.Key+
                                                                ": "+option.Item2);
                        checkedOptions++;
                    }
                }
            }
            Console.WriteLine("Verified "+table.Key+": "+table.Value.Count+" columns");
        }
        Console.WriteLine("SCHEMA_VERIFIED columns="+checkedColumns+" lookups="+checkedLookups+" choice_options="+checkedOptions);
        return 0;
    }
}
