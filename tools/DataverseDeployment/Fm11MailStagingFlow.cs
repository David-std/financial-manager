using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Crm.Sdk.Messages;

/// <summary>
/// Deploys an OFF Outlook-to-Dataverse mail intake. Writes only unverified
/// fmg_mailintake rows. No financial table, cash operation or approval action.
/// </summary>
internal static class Fm11MailStagingFlow
{
    private const string Name = "FM11 - Mail Intake (disabled)";
    private const string Solution = "FinancialManager";
    private const string Outlook = "shared_office365";
    private const string Dataverse = "shared_commondataserviceforapps";
    private const string Table = "fmg_mailintake";
    private const string KeyName = "fmg_MI_RequestId";

    public static int Run(ServiceClient client, bool create)
    {
        var existing = new QueryExpression("workflow") {
            TopCount = 5,
            ColumnSet = new ColumnSet("workflowid", "name", "category",
                                    "statecode", "clientdata")
        };
        existing.Criteria.AddCondition("name", ConditionOperator.Equal, Name);
        existing.Criteria.AddCondition("category", ConditionOperator.Equal, 5);
        var hits = client.RetrieveMultiple(existing).Entities;
        if (hits.Count > 1) throw new InvalidOperationException("Ambiguous FM11 intake flow.");
        if (hits.Count == 1)
        {
            VerifyOff(hits[0], acceptLegacy: true);
            using (var data = JsonDocument.Parse(
                hits[0].GetAttributeValue<string>("clientdata") ?? ""))
            {
                Console.WriteLine("FM11_MAIL_STAGING_TEXT_READ_CONFIGURED=" +
                    (data.RootElement.GetProperty("properties").GetProperty("definition")
                        .GetProperty("actions").EnumerateObject().Count() >= 3));
            }
            Console.WriteLine("FM11_MAIL_STAGING_REFERENCE_SHAPE_VERIFIED=true");
            Console.WriteLine("FM11_MAIL_STAGING_EXISTS=true");
            Console.WriteLine("FM11_MAIL_STAGING_OFF=true");
            return 0;
        }
        if (!create)
        {
            Console.WriteLine("FM11_MAIL_STAGING_EXISTS=false");
            return 0;
        }

        var entity = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest {
            LogicalName = Table, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata;
        var key = entity.Keys?.SingleOrDefault(k =>
            string.Equals(k.SchemaName, KeyName, StringComparison.OrdinalIgnoreCase));
        if (key == null || key.EntityKeyIndexStatus == EntityKeyIndexStatus.Failed ||
            entity.EntitySetName != "fmg_mailintakes")
            throw new InvalidOperationException("Intake table/key not ready.");
        // The workflow is created OFF. The index must reach Active before
        // anyone is allowed to enable or run the staging connector.
        Console.WriteLine("FM11_MAIL_STAGING_KEY_ACTIVE=" +
            (key.EntityKeyIndexStatus == EntityKeyIndexStatus.Active));

        var outlookId = RequiredConnection("FM11_OUTLOOK_CONNECTION_NAME");
        var dataverseId = RequiredConnection("FM11_DATAVERSE_CONNECTION_NAME");
        var path = Path.Combine(Directory.GetCurrentDirectory(), "integrations",
            "power-automate", "fm11_mail_staging.workflow.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        ValidateDefinition(json.RootElement);
        var references = new System.Collections.Generic.Dictionary<string, object> {
            [Outlook] = new {
                runtimeSource = "embedded",
                connection = new {
                    id = $"/providers/Microsoft.PowerApps/apis/{Outlook}/connections/{outlookId}"
                },
                api = new { name = Outlook }
            },
            [Dataverse] = new {
                runtimeSource = "embedded",
                connection = new {
                    id = $"/providers/Microsoft.PowerApps/apis/{Dataverse}/connections/{dataverseId}"
                },
                api = new { name = Dataverse }
            },
        };
        var wrapper = new {
            properties = new {
                connectionReferences = references,
                definition = json.RootElement.Clone()
            },
            schemaVersion = "1.0.0.0"
        };
        var draft = new Entity("workflow") {
            ["category"] = new OptionSetValue(5),
            ["name"] = Name,
            ["description"] = "Email to unverified intake table only. Keep disabled until live parsing and permission checks.",
            ["type"] = new OptionSetValue(1),
            ["primaryentity"] = "none",
            ["clientdata"] = JsonSerializer.Serialize(wrapper)
        };
        var created = client.Create(draft);
        try
        {
            var read = client.Retrieve("workflow", created,
                new ColumnSet("workflowid","name","category","statecode","clientdata"));
            VerifyOff(read);
            client.Execute(new AddSolutionComponentRequest {
                ComponentId = created, ComponentType = 29,
                SolutionUniqueName = Solution, AddRequiredComponents = false
            });
            Console.WriteLine("FM11_MAIL_STAGING_CREATED=true");
            Console.WriteLine("FM11_MAIL_STAGING_OFF=true");
            Console.WriteLine("FM11_MAIL_STAGING_FINANCIAL_WRITES=0");
            return 0;
        }
        catch
        {
            try { client.Delete("workflow", created); } catch { }
            throw;
        }
    }

    public static int InspectConnectionReferences(ServiceClient client)
    {
        var query = new QueryExpression("connectionreference") {
            ColumnSet = new ColumnSet(
                "connectionreferencelogicalname",
                "connectorid", "connectionid"),
            TopCount = 200
        };
        var refs = client.RetrieveMultiple(query).Entities;
        foreach (var connector in new [] { Outlook, Dataverse })
        {
            var matches = refs.Where(x =>
                (x.GetAttributeValue<string>("connectorid") ?? "")
                    .EndsWith("/" + connector, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var connectionName = Environment.GetEnvironmentVariable(
                connector == Outlook ?
                    "FM11_OUTLOOK_CONNECTION_NAME" : "FM11_DATAVERSE_CONNECTION_NAME");
            var bound = matches.Where(x =>
                !string.IsNullOrWhiteSpace(connectionName) &&
                (x.GetAttributeValue<string>("connectionid") ?? "")
                    .EndsWith(connectionName,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
            Console.WriteLine("FM11_" + connector + "_REFERENCE_COUNT=" + matches.Length);
            Console.WriteLine("FM11_" + connector + "_BOUND_REFERENCE_COUNT=" + bound.Length);
            Console.WriteLine("FM11_" + connector + "_RAW_ID_MATCHES=" +
                matches.Count(x => !string.IsNullOrWhiteSpace(connectionName) &&
                    (x.GetAttributeValue<string>("connectionid") ?? "")
                        .EndsWith(connectionName, StringComparison.OrdinalIgnoreCase)));
            foreach (var reference in matches)
            {
                var component = new QueryExpression("solutioncomponent") {
                    ColumnSet = new ColumnSet("componenttype"),
                    TopCount = 5
                };
                component.Criteria.AddCondition("objectid",
                    ConditionOperator.Equal, reference.Id);
                var links = client.RetrieveMultiple(component).Entities;
                Console.WriteLine("FM11_" + connector + "_SOLUTION_COMPONENT_TYPES=" +
                    string.Join(",", links.Select(x =>
                        x.GetAttributeValue<OptionSetValue>("componenttype")?.Value.ToString())));
            }
            Console.WriteLine("FM11_" + connector + "_REFERENCE_HAS_LOGICAL_NAME=" +
                matches.Any(x => !string.IsNullOrWhiteSpace(
                    x.GetAttributeValue<string>("connectionreferencelogicalname"))));
        }
        return 0;
    }

    public static int TurnOff(ServiceClient client)
    {
        var query = new QueryExpression("workflow") {
            ColumnSet = new ColumnSet("workflowid","name","category","statecode","clientdata"),
            TopCount = 2
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, Name);
        query.Criteria.AddCondition("category", ConditionOperator.Equal, 5);
        var rows = client.RetrieveMultiple(query).Entities;
        if (rows.Count != 1)
            throw new InvalidOperationException("Expected exactly one FM11 staging flow.");
        var row = rows[0];
        var state = row.GetAttributeValue<OptionSetValue>("statecode")?.Value;
        if (state != 0 && state != 1)
            throw new InvalidOperationException("Unexpected FM11 state.");
        using var data = JsonDocument.Parse(row.GetAttributeValue<string>("clientdata") ?? "");
        var properties = data.RootElement.GetProperty("properties");
        var references = properties.GetProperty("connectionReferences");
        if (references.EnumerateObject().Count() != 2 ||
            !references.TryGetProperty(Outlook, out _) ||
            !references.TryGetProperty(Dataverse, out _))
            throw new InvalidOperationException("Unapproved flow connector references.");
        var definition = properties.GetProperty("definition");
        if (definition.GetProperty("actions").EnumerateObject().Count() == 2)
            ValidateLegacyDefinition(definition);
        else
            ValidateDefinition(definition, allowPrevious: true);
        if (state == 1)
            client.Execute(new SetStateRequest {
                EntityMoniker = new EntityReference("workflow", row.Id),
                State = new OptionSetValue(0),
                Status = new OptionSetValue(1)
            });
        var saved = client.Retrieve("workflow", row.Id, new ColumnSet("statecode"));
        if (saved.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new InvalidOperationException("FM11 flow did not turn Off.");
        Console.WriteLine("FM11_MAIL_STAGING_OFF=true");
        return 0;
    }

    public static int UpgradeOff(ServiceClient client)
    {
        var query = new QueryExpression("workflow") {
            ColumnSet = new ColumnSet("workflowid", "name", "category", "statecode", "clientdata"),
            TopCount = 2
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, Name);
        query.Criteria.AddCondition("category", ConditionOperator.Equal, 5);
        var rows = client.RetrieveMultiple(query).Entities;
        if (rows.Count != 1)
            throw new InvalidOperationException("Expected one existing staging flow.");
        var current = rows[0];
        VerifyOff(current, acceptLegacy: true);
        var oldText = current.GetAttributeValue<string>("clientdata")!;
        using var document = JsonDocument.Parse(oldText);
        var existingDefinition = document.RootElement.GetProperty("properties")
            .GetProperty("definition");
        if (existingDefinition.GetProperty("actions").EnumerateObject().Count() == 5)
        {
            ValidateDefinition(existingDefinition);
            Console.WriteLine("FM11_MAIL_STAGING_TEXT_READ_ALREADY_CONFIGURED=true");
            return 0;
        }
        if (existingDefinition.GetProperty("actions").EnumerateObject().Count() == 3)
            ValidateDefinition(existingDefinition, allowPrevious: true);
        else
            ValidateLegacyDefinition(existingDefinition);

        var metadata = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest {
            LogicalName = Table, EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        })).EntityMetadata;
        var key = metadata.Keys?.SingleOrDefault(k =>
            string.Equals(k.SchemaName, KeyName, StringComparison.OrdinalIgnoreCase));
        if (key?.EntityKeyIndexStatus != EntityKeyIndexStatus.Active)
            throw new InvalidOperationException("Unique mail intake key not Active.");

        var path = Path.Combine(Directory.GetCurrentDirectory(), "integrations",
            "power-automate", "fm11_mail_staging.workflow.json");
        using var incoming = JsonDocument.Parse(File.ReadAllText(path));
        ValidateDefinition(incoming.RootElement);
        // The previous embedded connection IDs were never solution references.
        // Bind the two existing authenticated connections to solution references
        // before adding any new connector action.
        Fm11ConnectionReferences.Run(client, false);
        var root = JsonNode.Parse(oldText)
            ?? throw new InvalidOperationException("Invalid stored workflow JSON.");
        var props = root["properties"]
            ?? throw new InvalidOperationException("Missing workflow properties.");
        var refs = props["connectionReferences"]
            ?? throw new InvalidOperationException("Missing flow connection references.");
        refs[Outlook]!["connection"] = new JsonObject {
            ["connectionReferenceLogicalName"] = Fm11ConnectionReferences.OutlookLogicalName
        };
        refs[Dataverse]!["connection"] = new JsonObject {
            ["connectionReferenceLogicalName"] = Fm11ConnectionReferences.DataverseLogicalName
        };
        props["definition"] = JsonNode.Parse(incoming.RootElement.GetRawText());
        var proposed = root.ToJsonString();
        client.Update(new Entity("workflow", current.Id) {
            ["clientdata"] = proposed
        });
        var saved = client.Retrieve("workflow", current.Id,
            new ColumnSet("name", "category", "statecode", "clientdata"));
        VerifyOff(saved);
        Console.WriteLine("FM11_MAIL_STAGING_TEXT_READ_UPGRADED=true");
        Console.WriteLine("FM11_MAIL_STAGING_OFF=true");
        Console.WriteLine("FM11_MAIL_STAGING_FINANCIAL_WRITES=0");
        return 0;
    }

    private static string RequiredConnection(string name)
    {
        var id = Environment.GetEnvironmentVariable(name)?.Trim();
        if (string.IsNullOrWhiteSpace(id) ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                id, @"^[a-zA-Z0-9-]{10,128}$"))
            throw new InvalidOperationException("Connection ID is missing: " + name);
        return id;
    }

    private static void VerifyOff(Entity workflow, bool acceptLegacy = false)
    {
        if (workflow.GetAttributeValue<string>("name") != Name ||
            workflow.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
            workflow.GetAttributeValue<OptionSetValue>("category")?.Value != 5)
            throw new InvalidOperationException("Staging flow identity/state is unsafe.");
        using var stored = JsonDocument.Parse(
            workflow.GetAttributeValue<string>("clientdata") ?? "");
        var props = stored.RootElement.GetProperty("properties");
        var refs = props.GetProperty("connectionReferences");
        if (refs.EnumerateObject().Count() != 2 ||
            !refs.TryGetProperty(Outlook, out _) ||
            !refs.TryGetProperty(Dataverse, out _))
            throw new InvalidOperationException("Staging flow has unexpected connections.");
        var definition = props.GetProperty("definition");
        if (acceptLegacy &&
            definition.GetProperty("actions").EnumerateObject().Count() == 2)
            ValidateLegacyDefinition(definition);
        else
        {
            ValidateDefinition(definition, allowPrevious: acceptLegacy);
            foreach (var entry in new [] {
                (Outlook, Fm11ConnectionReferences.OutlookLogicalName),
                (Dataverse, Fm11ConnectionReferences.DataverseLogicalName) })
            {
                var id = refs.GetProperty(entry.Item1)
                    .GetProperty("connection")
                    .GetProperty("connectionReferenceLogicalName").GetString();
                if (id != entry.Item2)
                    throw new InvalidOperationException("Missing expected solution reference.");
            }
        }
    }

    private static void ValidateLegacyDefinition(JsonElement root)
    {
        var actions = root.GetProperty("actions");
        var triggers = root.GetProperty("triggers");
        if (actions.EnumerateObject().Count() != 2 ||
            triggers.EnumerateObject().Count() != 1)
            throw new InvalidOperationException("Unexpected legacy staging shape.");
        var parsed = actions.GetProperty("Parse_request");
        if (parsed.GetProperty("type").GetString() != "ParseJson" ||
            parsed.GetProperty("inputs").GetProperty("content").GetString() !=
                "@triggerOutputs()?['body/body']" ||
            parsed.GetProperty("inputs").GetProperty("schema")
                .GetProperty("additionalProperties").GetBoolean())
            throw new InvalidOperationException("Legacy parse contract invalid.");
        var write = actions.GetProperty("Stage_unverified_request");
        if (write.GetProperty("type").GetString() != "OpenApiConnection" ||
            write.GetProperty("inputs").GetProperty("parameters")
                .GetProperty("entityName").GetString() != "fmg_mailintakes" ||
            write.GetProperty("inputs").GetProperty("parameters")
                .GetProperty("item/fmg_reviewstate").GetString() != "Unverified" ||
            write.GetProperty("runAfter").GetProperty("Parse_request")[0]
                .GetString() != "Succeeded")
            throw new InvalidOperationException("Legacy staging mutation is unsafe.");
        var trigger = triggers.GetProperty("When_a_new_email_arrives_V3");
        if (trigger.GetProperty("inputs").GetProperty("host")
                .GetProperty("operationId").GetString() != "OnNewEmailV3" ||
            trigger.GetProperty("inputs").GetProperty("parameters")
                .GetProperty("subjectFilter").GetString() != "FM11-DRAFT-V1")
            throw new InvalidOperationException("Legacy trigger not approved.");
    }

    private static void ValidateDefinition(JsonElement root, bool allowPrevious = false)
    {
        var actions = root.GetProperty("actions");
        var triggers = root.GetProperty("triggers");
        if ((actions.EnumerateObject().Count() != 5 &&
             !(allowPrevious && actions.EnumerateObject().Count() == 3)) ||
            triggers.EnumerateObject().Count() != 1)
            throw new InvalidOperationException("Unexpected staging flow shape.");
        var text = actions.GetProperty("Read_message_as_text");
        var requestInputs = text.GetProperty("inputs");
        var requestHost = requestInputs.GetProperty("host");
        var requestValues = requestInputs.GetProperty("parameters");
        if (text.GetProperty("type").GetString() != "OpenApiConnection" ||
            requestHost.GetProperty("apiId").GetString() !=
                "/providers/Microsoft.PowerApps/apis/shared_office365" ||
            requestHost.GetProperty("connectionName").GetString() != Outlook ||
            requestHost.GetProperty("operationId").GetString() != "HttpRequest" ||
            requestValues.GetProperty("Method").GetString() != "GET" ||
            requestValues.GetProperty("Uri").GetString() !=
                "@concat('https://graph.microsoft.com/v1.0/me/messages/', encodeUriComponent(triggerOutputs()?['body/id']), '?$select=id,subject,body')" ||
            requestValues.GetProperty("CustomHeader1").GetString() !=
                "Prefer: outlook.body-content-type=\"text\"")
            throw new InvalidOperationException("Outlook Graph request must obtain the same message as text.");

        var parse = actions.GetProperty("Parse_request");
        if (parse.GetProperty("type").GetString() != "ParseJson" ||
            parse.GetProperty("inputs").GetProperty("content").GetString() !=
                "@body('Read_message_as_text')?['body']?['content']" ||
            parse.GetProperty("inputs").GetProperty("schema")
                .GetProperty("additionalProperties").GetBoolean())
            throw new InvalidOperationException("Missing strict email JSON validation.");
        var fromText = parse.GetProperty("runAfter")
            .GetProperty("Read_message_as_text");
        if (fromText.GetArrayLength() != 1 ||
            fromText[0].GetString() != "Succeeded")
            throw new InvalidOperationException("Parse must depend on plain-text Outlook retrieval.");
        var requestSchema = parse.GetProperty("inputs").GetProperty("schema");
        var required = requestSchema.GetProperty("required")
            .EnumerateArray().Select(x=>x.GetString()).ToArray();
        if (!required.Contains("requestId") || !required.Contains("categoryCode") ||
            !required.Contains("amountPen"))
            throw new InvalidOperationException("Missing required expense fields.");
        var categoryValues = requestSchema.GetProperty("properties")
            .GetProperty("categoryCode").GetProperty("enum")
            .EnumerateArray().Select(x => x.GetString()).ToArray();
        if (categoryValues.Length < 25 || !categoryValues.Contains("103") ||
            categoryValues.Distinct().Count() != categoryValues.Length)
            throw new InvalidOperationException("Invalid category list.");
        var write = actions.GetProperty("Stage_unverified_request");
        var inputs = write.GetProperty("inputs");
        var host = inputs.GetProperty("host");
        var values = inputs.GetProperty("parameters");
        if (write.GetProperty("type").GetString() != "OpenApiConnection" ||
            host.GetProperty("apiId").GetString() !=
                "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps" ||
            host.GetProperty("operationId").GetString() != "CreateRecord" ||
            values.GetProperty("entityName").GetString() != "fmg_mailintakes" ||
            values.GetProperty("item/fmg_reviewstate").GetString() != "Unverified" ||
            values.GetProperty("item/fmg_requestid").GetString() !=
                "@body('Parse_request')?['requestId']")
            throw new InvalidOperationException("Staging action is not restricted to unverified intake.");
        var depends = write.GetProperty("runAfter").GetProperty("Parse_request");
        if (depends.GetArrayLength() != 1 ||
            depends[0].GetString() != "Succeeded")
            throw new InvalidOperationException("Staging write must follow successful parse.");
        if (actions.EnumerateObject().Count() == 5)
        {
            var expected = new [] {
                ("item/fmg_concept", "name"),
                ("item/fmg_amountpen", "amountPen"),
                ("item/fmg_requesteddate", "date"),
                ("item/fmg_categorycode", "categoryCode"),
                ("item/fmg_branchcode", "branchCode"),
                ("item/fmg_accountname", "accountName"),
                ("item/fmg_flowclass", "flowClass")
            };
            foreach (var (field, source) in expected)
                if (values.GetProperty(field).GetString() !=
                    "@body('Parse_request')?['" + source + "']")
                    throw new InvalidOperationException("Intake display field mismatch: " + field);
            if (values.GetProperty("item/fmg_validationstate").GetString() != "FormatChecked" ||
                values.GetProperty("item/fmg_rawbody").GetString() !=
                "@body('Read_message_as_text')?['body']?['content']" ||
                !inputs.TryGetProperty("retryPolicy", out var retry) ||
                retry.GetProperty("type").GetString() != "none")
                throw new InvalidOperationException("FM11 intake raw body or retry guard invalid.");
            var classify = actions.GetProperty("Classify_stage_failure");
            if (classify.GetProperty("type").GetString() != "Compose" ||
                !classify.GetProperty("inputs").GetString()!.Contains(
                    "DUPLICATE_REQUEST", StringComparison.Ordinal) ||
                classify.GetProperty("runAfter").GetProperty("Stage_unverified_request")
                    .GetArrayLength() != 2)
                throw new InvalidOperationException("Missing duplicate/error classifier.");
            var terminate = actions.GetProperty("Stop_stage_failure");
            if (terminate.GetProperty("type").GetString() != "Terminate" ||
                terminate.GetProperty("inputs").GetProperty("runStatus").GetString() != "Failed" ||
                terminate.GetProperty("inputs").GetProperty("runError")
                    .GetProperty("code").GetString() != "FM11_STAGE_REJECTED" ||
                terminate.GetProperty("runAfter").GetProperty("Classify_stage_failure")[0]
                    .GetString() != "Succeeded")
                throw new InvalidOperationException("Staging failures must remain failures.");
        }
        var trigger = triggers.GetProperty("When_a_new_email_arrives_V3");
        if (trigger.GetProperty("type").GetString() != "OpenApiConnectionNotification" ||
            trigger.GetProperty("inputs").GetProperty("host")
                .GetProperty("operationId").GetString() != "OnNewEmailV3" ||
            trigger.GetProperty("inputs").GetProperty("parameters")
                .GetProperty("subjectFilter").GetString() != "FM11-DRAFT-V1")
            throw new InvalidOperationException("Unknown mail trigger.");
    }
}
