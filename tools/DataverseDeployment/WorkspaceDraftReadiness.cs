using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

/// <summary>Read-only DEV diagnosis for an exact user-supplied Draft name.
/// Emits field-presence metadata, never IDs, amounts, descriptions or tokens.</summary>
internal static class WorkspaceDraftReadiness
{
    public static int Inspect(ServiceClient service)
    {
        var name = Environment.GetEnvironmentVariable("FM_DRAFT_INSPECT_NAME");
        if (String.IsNullOrWhiteSpace(name) || name.Length > 120)
            throw new InvalidOperationException("Provide a safe exact FM_DRAFT_INSPECT_NAME.");
        var required = new[] {
            "fmg_amount", "fmg_transactiondate", "fmg_flowclass", "fmg_scope",
            "fmg_category", "fmg_cashaccount", "transactioncurrencyid"
        };
        var q = new QueryExpression("fmg_financialtransaction")
        {
            ColumnSet = new ColumnSet(required.Concat(new[] { "fmg_status", "fmg_branch" }).ToArray()),
            TopCount = 10
        };
        q.Criteria.AddCondition("fmg_name", ConditionOperator.Equal, name);
        var found = service.RetrieveMultiple(q);
        if (found.MoreRecords) throw new InvalidOperationException("More than 10 matching rows; ambiguous.");
        Console.WriteLine("F4_NAMED_DRAFT_MATCHES=" + found.Entities.Count);
        foreach (var row in found.Entities)
        {
            var status = row.GetAttributeValue<OptionSetValue>("fmg_status")?.Value;
            if (status != 797280000)
            {
                Console.WriteLine("F4_ROW_NOT_AN_EDITABLE_DRAFT");
                continue;
            }
            var missing = required.Where(f => !row.Contains(f) || row[f] == null).ToArray();
            if (row.GetAttributeValue<OptionSetValue>("fmg_scope")?.Value == 797280000 &&
                !row.Contains("fmg_branch")) missing = missing.Concat(new[] { "fmg_branch" }).ToArray();
            Console.WriteLine("F4_DRAFT_SERVER_FIELD_PRESENCE=" +
                (missing.Length == 0 ? "COMPLETE" : "INCOMPLETE"));
            Console.WriteLine("F4_DRAFT_SERVER_MISSING_FIELDS=" +
                (missing.Length == 0 ? "(none)" : String.Join(",", missing)));
        }
        Console.WriteLine("F4_DRAFT_INSPECTION_READ_ONLY_NO_WRITES");
        return 0;
    }
}
