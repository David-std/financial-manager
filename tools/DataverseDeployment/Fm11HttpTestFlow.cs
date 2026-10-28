using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

/// <summary>
/// FM-11 transport-only cloud flow: stores a disabled, inert 503 preview in DEV.
/// Never activates an HTTP trigger or grants an identity; never writes finance rows.
/// </summary>
internal static class Fm11HttpTestFlow
{
    private const string Name = "FM11 - HTTP Intake Test (disabled)";
    private const string SolutionName = "FinancialManager";
    private static readonly Guid ExistingTestFlowId = Guid.Parse("6b7eca28-51d3-f111-b1ec-70a8a5aca0ae");

    // Surgical repair for the known DEV test flow only. No trigger activation.
    // Blocks unexpected designer edits, other definitions and production rows.
    public static int RepairResponseKind(ServiceClient client)
    {
        var flow = client.Retrieve("workflow", ExistingTestFlowId,
            new ColumnSet("workflowid", "name", "category", "statecode", "clientdata"));
        if (flow.GetAttributeValue<string>("name") != Name ||
            flow.GetAttributeValue<OptionSetValue>("category")?.Value != 5 ||
            flow.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new InvalidOperationException("Unexpected FM-11 flow identity or state. Refusing repair.");

        var localPath = Path.Combine(Directory.GetCurrentDirectory(),
            "integrations", "power-automate", "fm11_http_preview.workflow.json");
        var expected = JsonNode.Parse(File.ReadAllText(localPath))
            ?? throw new InvalidOperationException("Missing preview definition.");
        var expectedAction = expected["actions"]?["DISABLED_NO_FINANCIAL_WRITES"]
            ?? throw new InvalidOperationException("Expected 503 action missing.");
        if (expectedAction["type"]?.GetValue<string>() != "Response" ||
            expectedAction["kind"]?.GetValue<string>() != "Http" ||
            expectedAction["inputs"]?["statusCode"]?.GetValue<int>() != 503 ||
            expected["actions"]?.AsObject().Count != 1 ||
            expected["triggers"]?.AsObject().Count != 1 ||
            expected["triggers"]?["manual"]?["kind"]?.GetValue<string>() != "Http")
            throw new InvalidOperationException("Expected source is not an inert HTTP response.");

        var raw = flow.GetAttributeValue<string>("clientdata")
            ?? throw new InvalidOperationException("Missing saved flow definition.");
        var wrapper = JsonNode.Parse(raw)
            ?? throw new InvalidOperationException("Invalid saved flow data.");
        var current = wrapper["properties"]?["definition"]
            ?? throw new InvalidOperationException("Missing live workflow definition.");
        var response = current["actions"]?["DISABLED_NO_FINANCIAL_WRITES"]
            ?? throw new InvalidOperationException("Live 503 action missing.");
        if (current["actions"]?.AsObject().Count != 1 ||
            current["triggers"]?.AsObject().Count != 1 ||
            response["type"]?.GetValue<string>() != "Response" ||
            response["inputs"]?["statusCode"]?.GetValue<int>() != 503)
            throw new InvalidOperationException("Live flow changed unexpectedly; no repair.");

        var expectedBefore = expected.DeepClone();
        expectedBefore["actions"]!["DISABLED_NO_FINANCIAL_WRITES"]!.AsObject().Remove("kind");
        if (!JsonNode.DeepEquals(current, expectedBefore))
        {
            // A changed definition must undergo manual diff review.
            if (JsonNode.DeepEquals(current, expected))
            {
                Console.WriteLine("FM11_RESPONSE_KIND_ALREADY_CORRECT=true");
                return 0;
            }
            throw new InvalidOperationException("Live definition is not the reviewed preview. No update.");
        }

        response.AsObject()["kind"] = "Http";
        client.Update(new Entity("workflow", ExistingTestFlowId)
        {
            ["clientdata"] = wrapper.ToJsonString()
        });
        var verified = client.Retrieve("workflow", ExistingTestFlowId,
            new ColumnSet("statecode", "clientdata"));
        if (verified.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new InvalidOperationException("Flow was enabled unexpectedly after repair.");
        var savedWrapper = JsonNode.Parse(verified.GetAttributeValue<string>("clientdata") ?? "")
            ?? throw new InvalidOperationException("Cannot verify response kind.");
        if (!JsonNode.DeepEquals(savedWrapper["properties"]?["definition"], expected))
            throw new InvalidOperationException("Readback definition differs from reviewed preview.");
        Console.WriteLine("FM11_RESPONSE_KIND_REPAIRED=Http");
        Console.WriteLine("FM11_HTTP_PREVIEW_STATE=OFF");
        Console.WriteLine("FM11_HTTP_PREVIEW_FINANCIAL_WRITES=0");
        return 0;
    }

    /// <summary>Read-only metadata audit. Emits no callback URL, identity or token.</summary>
    public static int InspectTriggerSecurity(ServiceClient client)
    {
        var record = client.Retrieve("workflow", ExistingTestFlowId,
            new ColumnSet("name", "category", "statecode", "clientdata"));
        if (record.GetAttributeValue<string>("name") != Name ||
            record.GetAttributeValue<OptionSetValue>("category")?.Value != 5)
            throw new InvalidOperationException("Not the approved DEV FM11 test workflow.");
        var state = record.GetAttributeValue<OptionSetValue>("statecode")?.Value;
        var root = JsonNode.Parse(record.GetAttributeValue<string>("clientdata") ?? "")
            ?? throw new InvalidOperationException("Flow definition is missing.");
        var definition = root["properties"]?["definition"];
        var actions = definition?["actions"]?.AsObject();
        var trigger = definition?["triggers"]?["manual"];
        bool onlyInertResponse = actions?.Count == 1 &&
            actions["DISABLED_NO_FINANCIAL_WRITES"]?["type"]?.GetValue<string>() == "Response" &&
            actions["DISABLED_NO_FINANCIAL_WRITES"]?["kind"]?.GetValue<string>() == "Http" &&
            actions["DISABLED_NO_FINANCIAL_WRITES"]?["inputs"]?["statusCode"]?.GetValue<int>() == 503;
        bool isHttp = trigger?["type"]?.GetValue<string>() == "Request" &&
            trigger?["kind"]?.GetValue<string>() == "Http";
        var exactPermittedAddress = Environment.GetEnvironmentVariable("FM11_EXPECTED_ALLOWED_USER")?.Trim();
        if (string.IsNullOrWhiteSpace(exactPermittedAddress) ||
            !exactPermittedAddress.Contains('@'))
            throw new InvalidOperationException("Set FM11_EXPECTED_ALLOWED_USER for the security audit.");
        bool permittedAddressRecorded = false;
        var emailLocations = new System.Collections.Generic.List<string>();
        var securityPaths = new System.Collections.Generic.List<string>();

        void Scan(JsonNode? node, string path)
        {
            if (node is JsonObject obj)
            {
                foreach (var entry in obj)
                    Scan(entry.Value, path + "/" + entry.Key);
            }
            else if (node is JsonArray array)
            {
                for (var i = 0; i < array.Count; i++)
                    Scan(array[i], path + "/" + i);
            }
            else if (node is JsonValue value)
            {
                if (value.TryGetValue<string>(out var str) && str != null)
                {
                    if (str.Equals(exactPermittedAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        permittedAddressRecorded = true;
                        emailLocations.Add(path);
                    }
                }
            }
            if (path.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("allowedUsers", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("allowedPrincipals", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("userIds", StringComparison.OrdinalIgnoreCase))
            {
                // Only schema paths, no field values that could contain callback URLs.
                if (node is JsonObject || node is JsonArray) securityPaths.Add(path);
            }
        }
        Scan(root, "clientdata");
        Console.WriteLine("FM11_FLOW_STATE_OFF=" + (state == 0));
        Console.WriteLine("FM11_HTTP_TRIGGER_PRESENT=" + isHttp);
        Console.WriteLine("FM11_ONLY_503_RESPONSE=" + onlyInertResponse);
        Console.WriteLine("FM11_USER_REFERENCE_PRESENT=" + permittedAddressRecorded);
        Console.WriteLine("FM11_SECURITY_METADATA_PATHS=" +
            string.Join(",", securityPaths.Take(20)));
        Console.WriteLine("FM11_ALLOWED_USER_PATHS=" + string.Join(",", emailLocations.Take(8)));
        Console.WriteLine("FM11_TRIGGER_NODE_KEYS=" +
            string.Join(",", trigger?.AsObject().Select(k => k.Key) ?? Enumerable.Empty<string>()));
        Console.WriteLine("FM11_TRIGGER_INPUT_KEYS=" +
            string.Join(",", trigger?["inputs"]?.AsObject().Select(k => k.Key) ?? Enumerable.Empty<string>()));
        // Email presence by itself does NOT verify restrictive trigger auth.
        var authMode = trigger?["inputs"]?["triggerAuthenticationType"]?.GetValue<string>() ?? "";
        var authModeLabel = authMode.Length <= 80 &&
            authMode.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-') ?
            authMode : "(invalid)";
        var allowedNode = trigger?["inputs"]?["triggerAllowedUsers"];
        bool onlyIntendedUser = allowedNode switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) =>
                s.Equals(exactPermittedAddress, StringComparison.OrdinalIgnoreCase),
            JsonArray a when a.Count == 1 =>
                a[0]?.GetValue<string>()?.Equals(exactPermittedAddress,
                    StringComparison.OrdinalIgnoreCase) == true,
            _ => false
        };
        Console.WriteLine("FM11_TRIGGER_AUTH_MODE=" + authModeLabel);
        Console.WriteLine("FM11_ONLY_EXPECTED_ALLOWED_USER=" + onlyIntendedUser);
        bool oauthRestricted = authMode == "User" && onlyIntendedUser;
        Console.WriteLine("FM11_TRIGGER_OAUTH_RESTRICTION_VERIFIED=" + oauthRestricted);
        return state == 0 && isHttp && onlyInertResponse && oauthRestricted ? 0 : 6;
    }

    /// <summary>
    /// Temporary test-only activation. Requires --apply, verified user, and
    /// a single inert 503 response. Must be returned to Off after the test.
    /// </summary>
    public static int SetInertTestState(ServiceClient client, bool activate)
    {
        var email = Environment.GetEnvironmentVariable("FM11_EXPECTED_ALLOWED_USER")?.Trim();
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            throw new InvalidOperationException("Missing FM11_EXPECTED_ALLOWED_USER.");
        var record = client.Retrieve("workflow", ExistingTestFlowId,
            new ColumnSet("name", "statecode", "category", "clientdata"));
        if (record.GetAttributeValue<string>("name") != Name ||
            record.GetAttributeValue<OptionSetValue>("category")?.Value != 5)
            throw new InvalidOperationException("Wrong flow identity.");
        var clientdata = JsonNode.Parse(record.GetAttributeValue<string>("clientdata") ?? "")
            ?? throw new InvalidOperationException("Missing workflow definition.");
        var definition = clientdata["properties"]?["definition"];
        var actions = definition?["actions"]?.AsObject();
        var response = actions?["DISABLED_NO_FINANCIAL_WRITES"];
        var triggers = definition?["triggers"]?.AsObject();
        var http = triggers?["manual"];
        var allowed = http?["inputs"]?["triggerAllowedUsers"];
        bool onlyExpected = allowed switch {
            JsonValue v when v.TryGetValue<string>(out var s) =>
                s.Equals(email, StringComparison.OrdinalIgnoreCase),
            JsonArray a when a.Count == 1 =>
                a[0]?.GetValue<string>()?.Equals(email, StringComparison.OrdinalIgnoreCase) == true,
            _ => false
        };
        bool inert = actions?.Count == 1 && triggers?.Count == 1 &&
            response?["type"]?.GetValue<string>() == "Response" &&
            response?["kind"]?.GetValue<string>() == "Http" &&
            response?["inputs"]?["statusCode"]?.GetValue<int>() == 503 &&
            response?["inputs"]?["body"]?["status"]?.GetValue<string>() == "NOT_CONFIGURED" &&
            http?["type"]?.GetValue<string>() == "Request" &&
            http?["kind"]?.GetValue<string>() == "Http" &&
            http?["inputs"]?["triggerAuthenticationType"]?.GetValue<string>() == "User" &&
            onlyExpected;
        if (!inert)
            throw new InvalidOperationException("Unsafe or modified test flow; refusing state change.");
        var state = record.GetAttributeValue<OptionSetValue>("statecode")?.Value;
        if (state != 0 && state != 1)
            throw new InvalidOperationException("Unexpected workflow state.");
        if (state == (activate ? 1 : 0))
        {
            Console.WriteLine("FM11_TEST_ALREADY_" + (activate ? "ON" : "OFF") + "=true");
            return 0;
        }
        client.Execute(new SetStateRequest {
            EntityMoniker = new EntityReference("workflow", ExistingTestFlowId),
            State = new OptionSetValue(activate ? 1 : 0),
            Status = new OptionSetValue(activate ? 2 : 1),
        });
        var saved = client.Retrieve("workflow", ExistingTestFlowId,
            new ColumnSet("statecode"));
        if (saved.GetAttributeValue<OptionSetValue>("statecode")?.Value != (activate ? 1 : 0))
            throw new InvalidOperationException("Flow state readback did not match request.");
        Console.WriteLine("FM11_TEST_STATE_" + (activate ? "ON" : "OFF") + "=true");
        return 0;
    }

    public static int Run(ServiceClient client, bool create)
    {
        var q = new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("workflowid", "name", "statecode", "category", "clientdata"),
            TopCount = 5,
            Criteria = new FilterExpression()
        };
        q.Criteria.AddCondition("name", ConditionOperator.Equal, Name);
        q.Criteria.AddCondition("category", ConditionOperator.Equal, 5);
        var current = client.RetrieveMultiple(q).Entities;
        if (current.Count > 1) throw new InvalidOperationException("Duplicate FM11 test flows; fail closed.");
        if (current.Count == 1)
        {
            var existing = current[0];
            if (existing.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                throw new InvalidOperationException("Unexpected active FM11 test flow.");
            Console.WriteLine("FM11_HTTP_PREVIEW_EXISTS=" + existing.Id);
            Console.WriteLine("FM11_HTTP_PREVIEW_STATE=OFF");
            var existingClientData = existing.GetAttributeValue<string>("clientdata") ?? "";
            var flowJson = JsonNode.Parse(existingClientData);
            var response = flowJson?["properties"]?["definition"]?["actions"]?["DISABLED_NO_FINANCIAL_WRITES"];
            var valid = response?["type"]?.GetValue<string>() == "Response" &&
                response?["kind"]?.GetValue<string>() == "Http" &&
                response?["inputs"]?["statusCode"]?.GetValue<int>() == 503 &&
                flowJson?["properties"]?["definition"]?["actions"]?.AsObject().Count == 1;
            Console.WriteLine("FM11_HTTP_RESPONSE_KIND_VALID=" + valid);
            if (!valid) return 6;
            return 0;
        }
        if (!create)
        {
            Console.WriteLine("FM11_HTTP_PREVIEW_EXISTS=false");
            return 0;
        }

        var path = Path.Combine(Directory.GetCurrentDirectory(), "integrations",
            "power-automate", "fm11_http_preview.workflow.json");
        if (!File.Exists(path)) throw new FileNotFoundException("Inert preview not generated", path);
        var raw = File.ReadAllText(path);
        using var parsed = JsonDocument.Parse(raw);
        var root = parsed.RootElement;
        var acts = root.GetProperty("actions");
        if (acts.EnumerateObject().Count() != 1 ||
            acts.GetProperty("DISABLED_NO_FINANCIAL_WRITES")
              .GetProperty("inputs").GetProperty("statusCode").GetInt32() != 503 ||
            acts.GetProperty("DISABLED_NO_FINANCIAL_WRITES")
              .GetProperty("type").GetString() != "Response" ||
            acts.GetProperty("DISABLED_NO_FINANCIAL_WRITES")
              .GetProperty("kind").GetString() != "Http")
            throw new InvalidOperationException("Preview must have only one 503 response action.");
        var trigger = root.GetProperty("triggers").GetProperty("manual");
        if (trigger.GetProperty("type").GetString() != "Request" ||
            trigger.GetProperty("kind").GetString() != "Http")
            throw new InvalidOperationException("Unexpected trigger type.");

        var wrapper = new
        {
            properties = new {
                connectionReferences = new { },
                definition = root,
            },
            schemaVersion = "1.0.0.0",
        };
        string clientData = JsonSerializer.Serialize(wrapper);
        var newFlow = new Entity("workflow")
        {
            ["category"] = new OptionSetValue(5),
            ["name"] = Name,
            ["description"] = "FM-11: inert HTTP preview only; must remain Off. No finance write, no approved trigger authentication.",
            ["type"] = new OptionSetValue(1),
            ["primaryentity"] = "none",
            ["clientdata"] = clientData,
        };
        var id = client.Create(newFlow);
        try
        {
            var readback = client.Retrieve("workflow", id,
                new ColumnSet("workflowid", "statecode", "category", "clientdata"));
            if (readback.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                throw new InvalidOperationException("Created workflow must be Off.");
            var saved = readback.GetAttributeValue<string>("clientdata") ?? "";
            if (saved.Length == 0 || !saved.Contains("DISABLED_NO_FINANCIAL_WRITES"))
                throw new InvalidOperationException("Missing inert 503 action after readback.");
            client.Execute(new AddSolutionComponentRequest
            {
                ComponentId = id,
                ComponentType = 29, // workflow
                SolutionUniqueName = SolutionName,
                AddRequiredComponents = false,
            });
            Console.WriteLine("FM11_HTTP_PREVIEW_CREATED=" + id);
            Console.WriteLine("FM11_HTTP_PREVIEW_STATE=OFF");
            Console.WriteLine("FM11_HTTP_PREVIEW_FINANCIAL_WRITES=0");
            Console.WriteLine("FM11_HTTP_PREVIEW_AUTHENTICATION_NOT_CONFIGURED=true");
            return 0;
        }
        catch
        {
            // Do not leave an unmanaged preview that was not linked to the intended solution.
            try { client.Delete("workflow", id); } catch { }
            throw;
        }
    }
}
