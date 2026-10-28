using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

/// <summary>Operational columns and read-only review queue for FM-11 intake.</summary>
internal static class Fm11IntakeWorkspace
{
    private const string Table = "fmg_mailintake";
    private const string Solution = "FinancialManager";
    private const string ViewName = "FM11 - Pending review";
    private static Label Label(string name) => new Label(name, 3082);

    private static readonly (string Schema, string Title, int Length)[] Columns = {
        ("fmg_Concept", "Concept", 100),
        ("fmg_AmountPen", "Amount (PEN)", 16),
        ("fmg_RequestedDate", "Requested date", 10),
        ("fmg_CategoryCode", "Category code", 3),
        ("fmg_BranchCode", "Branch", 2),
        ("fmg_AccountName", "Account", 100),
        ("fmg_FlowClass", "Expense class", 20),
        ("fmg_ValidationState", "Validation", 40),
    };
    private static readonly string[] ViewFields = {
        "fmg_name", "fmg_concept", "fmg_amountpen", "fmg_requesteddate",
        "fmg_branchcode", "fmg_categorycode", "fmg_accountname",
        "fmg_reviewstate", "fmg_validationstate", "createdon"
    };
    private static EntityMetadata Metadata(ServiceClient service) =>
        ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest {
            LogicalName = Table, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata;

    private static string Fetch()
    {
        return "<fetch mapping='logical' distinct='false'><entity name='" + Table + "'>" +
            string.Concat(ViewFields.Select(f=>"<attribute name='" + f + "'/>")) +
            "<order attribute='createdon' descending='true'/>" +
            "<filter type='and'><condition attribute='fmg_reviewstate' operator='eq' value='Unverified'/></filter>" +
            "</entity></fetch>";
    }

    private static string Layout()
    {
        return "<grid name='resultset' object='1' jump='fmg_name' select='1' icon='1' preview='1'>" +
            "<row name='result' id='fmg_mailintakeid'>" +
            string.Concat(ViewFields.Select((field, i) =>
                "<cell name='" + field + "' width='" + (i == 1 ? "220" : "130") + "'/>")) +
            "</row></grid>";
    }

    private static bool MatchesStoredFetch(string? storedXml, Guid viewId)
    {
        if (string.IsNullOrWhiteSpace(storedXml)) return false;
        var actual = XDocument.Parse(storedXml);
        var expected = XDocument.Parse(Fetch());
        var id = actual.Root?.Attribute("savedqueryid");
        if (id == null || !Guid.TryParse(id.Value, out var savedId) ||
            savedId != viewId)
            return false;
        id.Remove();
        return XNode.DeepEquals(actual.Root, expected.Root);
    }

    public static int Run(ServiceClient service, bool apply)
    {
        var meta = Metadata(service);
        if (meta.LogicalName != Table || meta.IsAuditEnabled?.Value != true)
            throw new InvalidOperationException("Unexpected intake entity or audit settings.");
        var changes = 0;
        foreach (var (schema, title, length) in Columns)
        {
            var logical = schema.ToLowerInvariant();
            var col = meta.Attributes.SingleOrDefault(x=>x.LogicalName == logical);
            if (col == null)
            {
                Console.WriteLine("FM11_WORKSPACE_MISSING_COLUMN=" + logical);
                if (!apply) continue;
                service.Execute(new CreateAttributeRequest {
                    EntityName = Table, SolutionUniqueName = Solution,
                    Attribute = new StringAttributeMetadata {
                        SchemaName = schema, DisplayName = Label(title), MaxLength = length,
                        RequiredLevel = new AttributeRequiredLevelManagedProperty(
                            AttributeRequiredLevel.None)
                    }
                });
                changes++;
            }
            else if (col is not StringAttributeMetadata text || text.MaxLength < length)
                throw new InvalidOperationException("Mismatched intake column " + logical);
        }
        if (apply && changes != 0)
        {
            service.Execute(new PublishXmlRequest {
                ParameterXml = "<importexportxml><entities><entity>" + Table +
                               "</entity></entities></importexportxml>"
            });
        }

        meta = Metadata(service);
        if (apply && ViewFields.Any(f => !meta.Attributes.Any(a => a.LogicalName == f)))
            throw new InvalidOperationException("Review view missing required columns.");

        var q = new QueryExpression("savedquery") {
            ColumnSet = new ColumnSet("savedqueryid","name","returnedtypecode","fetchxml","layoutxml",
                                      "querytype"), TopCount = 3
        };
        q.Criteria.AddCondition("name", ConditionOperator.Equal, ViewName);
        q.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, Table);
        var found = service.RetrieveMultiple(q).Entities;
        if (found.Count > 1) throw new InvalidOperationException("Ambiguous FM11 review view.");
        if (found.Count == 1)
        {
            var row = found[0];
            if (!MatchesStoredFetch(row.GetAttributeValue<string>("fetchxml"), row.Id) ||
                (row.GetAttributeValue<string>("layoutxml") ?? "") != Layout() ||
                row.GetAttributeValue<int>("querytype") != 0)
                throw new InvalidOperationException("Existing FM11 review view differs from validated contract.");
            Console.WriteLine("FM11_REVIEW_VIEW_EXISTS=true");
        }
        else
        {
            Console.WriteLine("FM11_REVIEW_VIEW_EXISTS=false");
            if (apply)
            {
                var view = new Entity("savedquery") {
                    ["name"] = ViewName, ["returnedtypecode"] = Table,
                    ["querytype"] = 0,
                    ["fetchxml"] = Fetch(), ["layoutxml"] = Layout(),
                    ["description"] = "Unverified FM11 intake; view only, not approval"
                };
                var request = new CreateRequest { Target = view };
                request.Parameters["SolutionUniqueName"] = Solution;
                service.Execute(request);
                Console.WriteLine("FM11_REVIEW_VIEW_CREATED=true");
            }
        }
        Console.WriteLine("FM11_REVIEW_VIEW_FINANCIAL_WRITES=0");
        return 0;
    }
}
