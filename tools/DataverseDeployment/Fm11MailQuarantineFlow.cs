using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

/// <summary>FM11 inert Outlook trigger; never activates or creates financial rows.</summary>
internal static class Fm11MailQuarantineFlow
{
    private const string Name = "FM11 - Mail Intake Quarantine (disabled)";
    private const string Solution = "FinancialManager";
    private const string Api = "shared_office365";
    private const string Subject = "FM11-DRAFT-V1";

    public static int Run(ServiceClient client, bool create)
    {
        var q = new QueryExpression("workflow")
        {
            TopCount = 5,
            ColumnSet = new ColumnSet("workflowid", "name", "category", "statecode", "clientdata")
        };
        q.Criteria.AddCondition("name", ConditionOperator.Equal, Name);
        q.Criteria.AddCondition("category", ConditionOperator.Equal, 5);
        var matches = client.RetrieveMultiple(q).Entities;
        if (matches.Count > 1)
            throw new InvalidOperationException("Multiple FM11 quarantine workflows.");
        if (matches.Count == 1)
        {
            VerifyInert(matches[0]);
            Console.WriteLine("FM11_MAIL_QUARANTINE_EXISTS=" + matches[0].Id);
            Console.WriteLine("FM11_MAIL_QUARANTINE_OFF=true");
            return 0;
        }
        if (!create)
        {
            Console.WriteLine("FM11_MAIL_QUARANTINE_EXISTS=false");
            return 0;
        }

        var connName = Environment.GetEnvironmentVariable("FM11_OUTLOOK_CONNECTION_NAME")?.Trim();
        // This is a connection resource identifier, not an OAuth token.
        if (string.IsNullOrWhiteSpace(connName) ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                connName, @"^[a-zA-Z0-9-]{10,128}$"))
            throw new InvalidOperationException(
                "Provide the verified Office365 connection name in FM11_OUTLOOK_CONNECTION_NAME.");
        var location = Path.Combine(Directory.GetCurrentDirectory(), "integrations",
            "power-automate", "fm11_mail_quarantine.workflow.json");
        var body = JsonDocument.Parse(File.ReadAllText(location)).RootElement.Clone();
        ValidateDefinition(body);

        var connectionId = $"/providers/Microsoft.PowerApps/apis/{Api}/connections/{connName}";
        var wrapper = new
        {
            properties = new
            {
                connectionReferences = new System.Collections.Generic.Dictionary<string, object>
                {
                    [Api] = new
                    {
                        runtimeSource = "embedded",
                        connection = new { id = connectionId },
                        api = new { name = Api }
                    }
                },
                definition = body
            },
            schemaVersion = "1.0.0.0"
        };
        var entity = new Entity("workflow")
        {
            ["category"] = new OptionSetValue(5),
            ["name"] = Name,
            ["description"] = "Inert FM11 Outlook trigger: no expense writes or approvals; keep Off.",
            ["type"] = new OptionSetValue(1),
            ["primaryentity"] = "none",
            ["clientdata"] = JsonSerializer.Serialize(wrapper),
        };
        var id = client.Create(entity);
        try
        {
            var read = client.Retrieve("workflow", id,
                new ColumnSet("workflowid", "name", "category", "statecode", "clientdata"));
            VerifyInert(read);
            client.Execute(new AddSolutionComponentRequest
            {
                ComponentId = id,
                ComponentType = 29,
                SolutionUniqueName = Solution,
                AddRequiredComponents = false,
            });
            Console.WriteLine("FM11_MAIL_QUARANTINE_CREATED=" + id);
            Console.WriteLine("FM11_MAIL_QUARANTINE_OFF=true");
            Console.WriteLine("FM11_MAIL_QUARANTINE_FINANCIAL_WRITES=0");
            return 0;
        }
        catch
        {
            try { client.Delete("workflow", id); } catch { }
            throw;
        }
    }

    private static void VerifyInert(Entity entity)
    {
        if (entity.GetAttributeValue<string>("name") != Name ||
            entity.GetAttributeValue<OptionSetValue>("category")?.Value != 5 ||
            entity.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new InvalidOperationException("Quarantine workflow identity/state not safe.");
        var data = JsonDocument.Parse(entity.GetAttributeValue<string>("clientdata") ?? "");
        var properties = data.RootElement.GetProperty("properties");
        var refs = properties.GetProperty("connectionReferences");
        if (refs.EnumerateObject().Count() != 1 || !refs.TryGetProperty(Api, out _))
            throw new InvalidOperationException("Unexpected connector references.");
        ValidateDefinition(properties.GetProperty("definition"));
    }

    private static void ValidateDefinition(JsonElement root)
    {
        var actions = root.GetProperty("actions");
        var trigger = root.GetProperty("triggers");
        if (actions.EnumerateObject().Count() != 1 ||
            trigger.EnumerateObject().Count() != 1)
            throw new InvalidOperationException("Not an inert single-action mail flow.");
        var terminate = actions.GetProperty("Quarantine_no_financial_writes");
        if (terminate.GetProperty("type").GetString() != "Terminate" ||
            terminate.GetProperty("inputs").GetProperty("runStatus").GetString() != "Cancelled")
            throw new InvalidOperationException("Unexpected or mutating mail flow action.");
        var eventTrigger = trigger.GetProperty("When_a_new_email_arrives_V3");
        if (eventTrigger.GetProperty("type").GetString() != "OpenApiConnectionNotification")
            throw new InvalidOperationException("Unexpected Outlook trigger type.");
        var inputs = eventTrigger.GetProperty("inputs");
        var host = inputs.GetProperty("host");
        var values = inputs.GetProperty("parameters");
        if (host.GetProperty("apiId").GetString() !=
                "/providers/Microsoft.PowerApps/apis/shared_office365" ||
            host.GetProperty("operationId").GetString() != "OnNewEmailV3" ||
            host.GetProperty("connectionName").GetString() != Api ||
            values.GetProperty("subjectFilter").GetString() != Subject ||
            values.GetProperty("folderPath").GetString() != "Inbox" ||
            values.GetProperty("includeAttachments").GetBoolean() ||
            values.GetProperty("fetchOnlyWithAttachment").GetBoolean())
            throw new InvalidOperationException("Outlook connector trigger is not approved.");
    }
}
