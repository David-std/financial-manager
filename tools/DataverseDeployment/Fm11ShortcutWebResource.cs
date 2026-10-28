using System;
using System.IO;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class Fm11ShortcutWebResource
{
    private const string Solution = "FinancialManager";
    private const string Name = "fmg_shortcut_draft.html";
    private static string SourcePath => Path.Combine(Directory.GetCurrentDirectory(),
        "integrations", "power-automate", "fm11_shortcut_draft.html");

    public static int Run(ServiceClient client, bool apply)
    {
        var source = File.ReadAllBytes(SourcePath);
        if (source.Length < 4000 || source.Length > 80000)
            throw new InvalidOperationException("Unexpected FM11 web resource size.");
        var contents = Convert.ToBase64String(source);
        var query = new QueryExpression("webresource") {
            ColumnSet = new ColumnSet("webresourceid", "name", "webresourcetype", "content"),
            TopCount = 2
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, Name);
        var matches = client.RetrieveMultiple(query).Entities;
        if (matches.Count > 1)
            throw new InvalidOperationException("Duplicate shortcut browser resources.");
        if (matches.Count == 1)
        {
            var item = matches[0];
            if (item.GetAttributeValue<OptionSetValue>("webresourcetype")?.Value != 1)
                throw new InvalidOperationException("Existing resource is not HTML.");
            if (item.GetAttributeValue<string>("content") == contents)
            {
                Console.WriteLine("FM11_SHORTCUT_BROWSER_DEPLOYED=true");
                Console.WriteLine("FM11_SHORTCUT_BROWSER_CONTENT_MATCH=true");
                return 0;
            }
            if (!apply)
            {
                Console.WriteLine("FM11_SHORTCUT_BROWSER_CONTENT_MATCH=false");
                return 0;
            }
            client.Update(new Entity("webresource", item.Id) { ["content"] = contents });
            Publish(client, item.Id);
        }
        else
        {
            Console.WriteLine("FM11_SHORTCUT_BROWSER_DEPLOYED=false");
            if (!apply) return 0;
            var row = new Entity("webresource") {
                ["name"] = Name,
                ["displayname"] = "FM11 Shortcut delegated sign-in",
                ["description"] = "Entra SPA PKCE S256 and authenticated Draft creation.",
                ["webresourcetype"] = new OptionSetValue(1),
                ["content"] = contents
            };
            var request = new CreateRequest { Target = row };
            request.Parameters["SolutionUniqueName"] = Solution;
            var id = ((CreateResponse)client.Execute(request)).id;
            if (id == Guid.Empty) throw new InvalidOperationException("Web resource create failed.");
            Publish(client, id);
        }
        var saved = client.RetrieveMultiple(query).Entities;
        if (saved.Count != 1 ||
            saved[0].GetAttributeValue<OptionSetValue>("webresourcetype")?.Value != 1 ||
            saved[0].GetAttributeValue<string>("content") != contents)
            throw new InvalidOperationException("Web resource readback mismatch.");
        Console.WriteLine("FM11_SHORTCUT_BROWSER_DEPLOYED=true");
        Console.WriteLine("FM11_SHORTCUT_BROWSER_CONTENT_MATCH=true");
        Console.WriteLine("FM11_SHORTCUT_BROWSER_FINANCIAL_WRITES=0");
        return 0;
    }

    private static void Publish(ServiceClient client, Guid id)
    {
        client.Execute(new PublishXmlRequest {
            ParameterXml = "<importexportxml><webresources><webresource>{" +
                           id.ToString("D") + "}</webresource></webresources></importexportxml>"
        });
    }
}
