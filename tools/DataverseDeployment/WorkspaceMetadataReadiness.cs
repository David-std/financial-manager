using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

/// <summary>Read-only F4 Web API metadata contract; prints no row contents.</summary>
internal static class WorkspaceMetadataReadiness
{
    public static int Inspect(ServiceClient client)
    {
        var names = new[] { "fmg_financialtransaction", "fmg_budget",
            "fmg_budgetallocation", "fmg_activityevent", "fmg_category",
            "fmg_branch", "fmg_cashaccount" };
        foreach (var logical in names)
        {
            var row = (RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
            {
                LogicalName = logical, RetrieveAsIfPublished = false,
                EntityFilters = EntityFilters.Entity | EntityFilters.Attributes | EntityFilters.Relationships
            });
            var m = row.EntityMetadata;
            if (m == null || m.LogicalName != logical ||
                String.IsNullOrWhiteSpace(m.EntitySetName))
                throw new InvalidOperationException("Unverified table metadata " + logical);
            Console.WriteLine("F4_SET " + logical + " " + m.EntitySetName +
                " OWNERSHIP=" + m.OwnershipType + " AUDIT=" + m.IsAuditEnabled?.Value);
            string[] relevant = logical switch
            {
                "fmg_financialtransaction" => new[] {
                    "fmg_financialtransactionid", "fmg_name", "fmg_status",
                    "fmg_amount", "fmg_transactiondate", "fmg_scope", "fmg_flowclass",
                    "fmg_description", "fmg_documentreference",
                    "fmg_category", "fmg_branch", "fmg_cashaccount", "transactioncurrencyid" },
                "fmg_budget" => new[] { "fmg_budgetid", "fmg_name",
                    "fmg_year", "fmg_scope", "fmg_version",
                    "fmg_status", "fmg_effectivefrommonth", "fmg_branch" },
                "fmg_budgetallocation" => new[] { "fmg_budgetallocationid",
                    "fmg_amount", "fmg_month", "fmg_budget", "fmg_category" },
                "fmg_activityevent" => new[] { "fmg_activityeventid",
                    "fmg_name", "fmg_eventtype", "fmg_eventtime",
                    "fmg_recordtype", "fmg_recordid", "fmg_summary", "fmg_actor" },
                "fmg_category" => new[] { "fmg_categoryid", "fmg_name", "fmg_code", "fmg_isactive", "fmg_isgroup", "fmg_parentcategory" },
                "fmg_branch" => new[] { "fmg_branchid", "fmg_name", "fmg_isactive" },
                "fmg_cashaccount" => new[] { "fmg_cashaccountid", "fmg_name", "fmg_isactive", "fmg_branch" },
                _ => new[] { logical + "id", "fmg_name" }
            };
            if (logical == "fmg_financialtransaction")
            {
                foreach (var relation in m.ManyToOneRelationships ?? Array.Empty<OneToManyRelationshipMetadata>())
                {
                    if (new[] { "fmg_category", "fmg_branch", "fmg_cashaccount",
                        "transactioncurrencyid" }.Contains(relation.ReferencingAttribute))
                        Console.WriteLine("F4_NAV " + relation.ReferencingAttribute +
                            " " + relation.ReferencingEntityNavigationPropertyName +
                            " TARGET=" + relation.ReferencedEntity);
                }
            }
            foreach (var column in relevant)
            {
                var a = m.Attributes?.SingleOrDefault(x => x.LogicalName == column);
                if (a == null || a.AttributeType == null)
                    throw new InvalidOperationException("Missing " + logical + "." + column);
                Console.WriteLine("F4_FIELD " + logical + "." + column + " " + a.AttributeType);
            }
        }
        Console.WriteLine("F4_METADATA_READONLY_VERIFIED=true");
        return 0;
    }
}
