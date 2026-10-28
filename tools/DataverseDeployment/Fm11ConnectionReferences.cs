using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

/// <summary>Solution references to existing authenticated DEV connector instances.</summary>
internal static class Fm11ConnectionReferences
{
    internal const string OutlookLogicalName = "fmg_FM11_Outlook";
    internal const string DataverseLogicalName = "fmg_FM11_Dataverse";
    private const string Solution = "FinancialManager";

    private static readonly (string Api, string Name, string Env, string Display)[] Items = {
        ("shared_office365", OutlookLogicalName,
            "FM11_OUTLOOK_CONNECTION_NAME", "FinancialManager Outlook"),
        ("shared_commondataserviceforapps", DataverseLogicalName,
            "FM11_DATAVERSE_CONNECTION_NAME", "FinancialManager Dataverse")
    };

    public static int Run(ServiceClient client, bool create)
    {
        var configs = Items.Select(x => {
            var value = Environment.GetEnvironmentVariable(x.Env)?.Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                !System.Text.RegularExpressions.Regex.IsMatch(value, @"^[a-zA-Z0-9-]{10,128}$"))
                throw new InvalidOperationException("Missing verified connector: " + x.Api);
            return (x.Api, x.Name, x.Display, ConnectionName: value);
        }).ToArray();

        var type = ComponentType(client);
        var added = new List<Guid>();
        try
        {
            foreach (var item in configs)
            {
                var connector = "/providers/Microsoft.PowerApps/apis/" + item.Api;
                var connection = item.ConnectionName;
                var q = new QueryExpression("connectionreference") {
                    ColumnSet = new ColumnSet(
                        "connectionreferencelogicalname", "connectorid", "connectionid"),
                    TopCount = 3
                };
                q.Criteria.AddCondition("connectionreferencelogicalname",
                    ConditionOperator.Equal, item.Name);
                var existing = client.RetrieveMultiple(q).Entities;
                if (existing.Count > 1)
                    throw new InvalidOperationException("Duplicate FM11 connector reference.");
                if (existing.Count == 1)
                {
                    var refRow = existing[0];
                    if (refRow.GetAttributeValue<string>("connectorid") != connector ||
                        !new [] { connection, item.ConnectionName }.Contains(
                            refRow.GetAttributeValue<string>("connectionid"),
                            StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Incorrect FM11 connector binding.");
                    Console.WriteLine("FM11_REF_" + item.Api + "_ALREADY_PRESENT=true");
                    continue;
                }
                if (!create)
                {
                    throw new InvalidOperationException(
                        "Required solution connection reference missing: " + item.Api);
                }

                var entity = new Entity("connectionreference") {
                    ["connectionreferencelogicalname"] = item.Name,
                    ["connectionreferencedisplayname"] = item.Display,
                    ["connectorid"] = connector,
                    ["connectionid"] = connection,
                    ["description"] = "FM11 expense intake; binds an existing DEV connection."
                };
                var id = client.Create(entity);
                added.Add(id);
                var read = client.Retrieve("connectionreference", id,
                    new ColumnSet("connectionreferencelogicalname", "connectorid", "connectionid"));
                if (read.GetAttributeValue<string>("connectionreferencelogicalname") != item.Name ||
                    read.GetAttributeValue<string>("connectorid") != connector ||
                    read.GetAttributeValue<string>("connectionid") != connection)
                    throw new InvalidOperationException("Reference readback mismatch.");
                client.Execute(new AddSolutionComponentRequest {
                    ComponentId = id, ComponentType = type,
                    SolutionUniqueName = Solution, AddRequiredComponents = false
                });
                Console.WriteLine("FM11_REF_" + item.Api + "_BOUND_TO_SOLUTION=true");
            }
            return 0;
        }
        catch
        {
            foreach (var id in added)
            {
                try { client.Delete("connectionreference", id); } catch { }
            }
            throw;
        }
    }

    private static int ComponentType(ServiceClient client)
    {
        var examples = new QueryExpression("connectionreference") {
            ColumnSet = new ColumnSet("connectorid"), TopCount = 25
        };
        examples.Criteria.AddCondition("connectorid", ConditionOperator.Like,
            "%shared_commondataserviceforapps");
        var rows = client.RetrieveMultiple(examples).Entities;
        var kinds = new HashSet<int>();
        foreach (var row in rows)
        {
            var links = new QueryExpression("solutioncomponent") {
                ColumnSet = new ColumnSet("componenttype"), TopCount = 5
            };
            links.Criteria.AddCondition("objectid", ConditionOperator.Equal, row.Id);
            foreach (var link in client.RetrieveMultiple(links).Entities)
            {
                var kind = link.GetAttributeValue<OptionSetValue>("componenttype")?.Value;
                if (kind.HasValue) kinds.Add(kind.Value);
            }
        }
        if (kinds.Count != 1)
            throw new InvalidOperationException("Cannot determine connection-reference component type.");
        return kinds.Single();
    }
}
