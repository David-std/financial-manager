using System;
using System.IO;
using System.Linq;
using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class CashGrantRequestGuardDeployment
{
    private const string Package = "fmg_CashGrantRequestIntegrity";
    private const string Plugin = "FinancialManager.CashGrantRequestIntegrity.F5_BlockGrantRequestWrites";
    private const string Table = "fmg_cashgrantrequest";
    private const string Solution = "FinancialManager";
    private static readonly string[] Messages = { "Create", "Update", "Delete" };

    private static Entity[] Lookup(ServiceClient service, string name, string field,
        object value, params string[] columns)
    {
        var q = new QueryExpression(name)
        {
            ColumnSet = new ColumnSet(columns), TopCount = 3
        };
        q.Criteria.AddCondition(field, ConditionOperator.Equal, value);
        var response = service.RetrieveMultiple(q);
        if (response == null || response.MoreRecords || response.Entities.Count > 2)
            throw new InvalidOperationException("Unbounded F5 request guard metadata.");
        return response.Entities.ToArray();
    }

    private static Guid TypeId(ServiceClient service)
    {
        var types = Lookup(service, "plugintype", "typename", Plugin, "plugintypeid");
        if (types.Length > 1)
            throw new InvalidOperationException("Duplicate grant request guard plugin types.");
        return types.Length == 0 ? Guid.Empty : types[0].Id;
    }

    public static int Register(ServiceClient service)
    {
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashSchemaReadiness.Verify(service);
        var grants = service.RetrieveMultiple(new QueryExpression("fmg_cashaccessgrant")
        {
            ColumnSet = new ColumnSet(false), TopCount = 1
        });
        if (grants.MoreRecords || grants.Entities.Count != 0)
            throw new InvalidOperationException("Request bootstrap needs no existing grants.");
        if (TypeId(service) != Guid.Empty ||
            Lookup(service, "pluginpackage", "name", Package, "pluginpackageid").Length > 0)
            throw new InvalidOperationException("Request guard package already installed; no overwrite.");

        var path = Environment.GetEnvironmentVariable("FM_CASH_GRANT_REQUEST_PACKAGE_PATH");
        if (string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path) ||
            Path.GetFileName(path) != "fmg_CashGrantRequestIntegrity.1.0.0.nupkg")
            throw new InvalidOperationException("Fresh isolated guard NuGet required.");
        var payload = File.ReadAllBytes(path);
        if (payload.Length < 2000 || payload.Length > 2000000)
            throw new InvalidOperationException("Grant request guard package size unexpected.");
        var package = new Entity("pluginpackage");
        package["name"] = Package;
        package["uniquename"] = Package;
        package["version"] = "1.0.0";
        package["content"] = Convert.ToBase64String(payload);
        var create = new CreateRequest { Target = package };
        create.Parameters["SolutionUniqueName"] = Solution;
        var response = (CreateResponse)service.Execute(create);
        if (response.id == Guid.Empty || TypeId(service) == Guid.Empty)
            throw new InvalidOperationException("Grant request guard plug-in did not register.");
        Console.WriteLine("F5_REQUEST_GUARD_PACKAGE_REGISTERED");
        return 0;
    }

    public static int Steps(ServiceClient service, bool apply, bool requireAll = false)
    {
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        var type = TypeId(service);
        if (apply && type == Guid.Empty)
            throw new InvalidOperationException("Required request guard package not registered.");
        var existing = 0;
        foreach (var message in Messages)
        {
            var stepName = "F5_DenyGrantRequest_" + message;
            var steps = Lookup(service, "sdkmessageprocessingstep", "name",
                stepName, "sdkmessageprocessingstepid", "eventhandler", "stage",
                "mode", "statecode", "sdkmessageid", "sdkmessagefilterid");
            if (steps.Length > 1)
                throw new InvalidOperationException("Duplicate guard step " + stepName);
            if (steps.Length == 1)
            {
                var row = steps[0];
                var actualMessage = row.GetAttributeValue<EntityReference>("sdkmessageid");
                var actualFilter = row.GetAttributeValue<EntityReference>("sdkmessagefilterid");
                if (type == Guid.Empty ||
                    row.GetAttributeValue<EntityReference>("eventhandler")?.Id != type ||
                    row.GetAttributeValue<OptionSetValue>("stage")?.Value != 10 ||
                    row.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                    row.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    actualMessage?.Id == Guid.Empty || actualFilter?.Id == Guid.Empty ||
                    actualMessage == null || actualFilter == null)
                    throw new InvalidOperationException("Untrusted guard step " + stepName);
                var messageIdentity = service.Retrieve("sdkmessage", actualMessage.Id,
                    new ColumnSet("name"));
                var filterIdentity = service.Retrieve("sdkmessagefilter", actualFilter.Id,
                    new ColumnSet("sdkmessageid", "primaryobjecttypecode"));
                if (messageIdentity == null ||
                    messageIdentity.GetAttributeValue<string>("name") != message ||
                    filterIdentity == null ||
                    filterIdentity.GetAttributeValue<EntityReference>("sdkmessageid")?.Id != actualMessage.Id ||
                    filterIdentity.GetAttributeValue<string>("primaryobjecttypecode") != Table)
                    throw new InvalidOperationException("F5 guard bound to wrong table/message: " + stepName);
                existing++;
                Console.WriteLine("F5_REQUEST_GUARD_ACTIVE " + stepName);
                continue;
            }
            Console.WriteLine("F5_REQUEST_GUARD_MISSING " + stepName);
            if (!apply) continue;
            var sdkMessage = Lookup(service, "sdkmessage", "name", message, "sdkmessageid");
            if (sdkMessage.Length != 1)
                throw new InvalidOperationException("SDK message missing " + message);
            var q = new QueryExpression("sdkmessagefilter")
            {
                ColumnSet = new ColumnSet("sdkmessagefilterid"), TopCount = 3
            };
            q.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, sdkMessage[0].Id);
            q.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, Table);
            var filters = service.RetrieveMultiple(q);
            if (filters == null || filters.MoreRecords || filters.Entities.Count != 1)
                throw new InvalidOperationException("Request filter absent for " + message);
            var step = new Entity("sdkmessageprocessingstep");
            step["name"] = stepName;
            step["eventhandler"] = new EntityReference("plugintype", type);
            step["sdkmessageid"] = new EntityReference("sdkmessage", sdkMessage[0].Id);
            step["sdkmessagefilterid"] =
                new EntityReference("sdkmessagefilter", filters.Entities[0].Id);
            step["stage"] = new OptionSetValue(10);
            step["mode"] = new OptionSetValue(0);
            step["rank"] = 1;
            step["supporteddeployment"] = new OptionSetValue(0);
            var register = new CreateRequest { Target = step };
            register.Parameters["SolutionUniqueName"] = Solution;
            service.Execute(register);
            existing++;
            Console.WriteLine("F5_REQUEST_GUARD_CREATED " + stepName);
        }
        if (requireAll && existing != 3)
            throw new InvalidOperationException("Missing protected grant request guard steps.");
        Console.WriteLine("F5_REQUEST_GUARD_STEPS=" + existing + "/3");
        return 0;
    }

    public static int Probe(ServiceClient service)
    {
        Steps(service, false, true);
        var candidate = new Entity(Table);
        candidate["fmg_name"] = "GUARD-PROBE-" + Guid.NewGuid().ToString("N");
        try
        {
            service.Create(candidate);
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            if (!(ex.Detail?.Message ?? ex.Message).Contains("F5Q009"))
                throw new InvalidOperationException("Unexpected request guard failure.", ex);
            Console.WriteLine("F5_REQUEST_CREATE_DENIED_F5Q009");
            return 0;
        }
        throw new InvalidOperationException("UNSAFE: Direct request Create was accepted.");
    }
}
