using System;
using System.IO;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class CashGrantGuardDeployment
{
    private const string Package = "fmg_CashAccessIntegrity";
    private const string Type = "FinancialManager.CashAccessIntegrity.F5_BlockCashAccessWrites";
    private const string Table = "fmg_cashaccessgrant";
    private const string Solution = "FinancialManager";
    private static readonly string[] Messages = { "Create", "Update", "Delete" };

    private static Entity[] Fetch(ServiceClient client, string table, string column, object value,
        params string[] attrs)
    {
        var q = new QueryExpression(table) { ColumnSet = new ColumnSet(attrs), TopCount = 3 };
        q.Criteria.AddCondition(column, ConditionOperator.Equal, value);
        return client.RetrieveMultiple(q).Entities.ToArray();
    }

    private static Guid RegisteredType(ServiceClient client)
    {
        var types = Fetch(client, "plugintype", "typename", Type, "plugintypeid");
        if (types.Length > 1)
            throw new InvalidOperationException("Ambiguous F5 cash grant policy plugin.");
        return types.Length == 1 ? types[0].Id : Guid.Empty;
    }

    public static int Register(ServiceClient svc)
    {
        // Keep all six F5 financial tables protected before registering metadata.
        CashGuardDeployment.Run(svc, false, true);
        if (Fetch(svc, "pluginpackage", "name", Package, "pluginpackageid").Length != 0)
            throw new InvalidOperationException("Existing grant package may not be overwritten.");
        var path = Environment.GetEnvironmentVariable("FM_CASH_ACCESS_PACKAGE_PATH");
        if (String.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            Path.GetFileName(path) != "fmg_CashAccessIntegrity.1.0.0.nupkg")
            throw new InvalidOperationException("Fresh signed-off F5 cash grant NuGet required.");
        var data = File.ReadAllBytes(path);
        if (data.Length < 2000 || data.Length > 2000000)
            throw new InvalidOperationException("Unexpected grant guard package size.");
        var package = new Entity("pluginpackage");
        package["name"] = Package;
        package["uniquename"] = Package;
        package["version"] = "1.0.0";
        package["content"] = Convert.ToBase64String(data);
        var create = new CreateRequest { Target = package };
        create.Parameters["SolutionUniqueName"] = Solution;
        var response = (CreateResponse)svc.Execute(create);
        if (response.id == Guid.Empty || RegisteredType(svc) == Guid.Empty)
            throw new InvalidOperationException("Missing registered financial grant plugin type.");
        Console.WriteLine("F5_GRANT_GUARD_PACKAGE_REGISTERED");
        return 0;
    }

    public static int Steps(ServiceClient svc, bool apply, bool required = false)
    {
        CashGuardDeployment.Run(svc, false, true);
        var typeId = RegisteredType(svc);
        if (apply && typeId == Guid.Empty)
            throw new InvalidOperationException("Missing validated grant guard package.");
        var missing = 0;
        foreach (var message in Messages)
        {
            var name = "F5_DenyGrant_" + message;
            var steps = Fetch(svc, "sdkmessageprocessingstep", "name", name,
                "sdkmessageprocessingstepid", "eventhandler", "stage", "mode",
                "statecode", "sdkmessageid", "sdkmessagefilterid");
            if (steps.Length > 1)
                throw new InvalidOperationException("Duplicate grant guard step " + name);
            if (steps.Length == 1)
            {
                var s = steps[0];
                if (typeId == Guid.Empty ||
                    s.GetAttributeValue<EntityReference>("eventhandler")?.Id != typeId ||
                    s.GetAttributeValue<OptionSetValue>("stage")?.Value != 10 ||
                    s.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                    s.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                    throw new InvalidOperationException("Unexpected grant guard registration: " + name);
                Console.WriteLine("F5_GRANT_GUARD_ACTIVE " + name);
                continue;
            }
            missing++;
            Console.WriteLine("F5_GRANT_GUARD_MISSING " + name);
            if (!apply) continue;
            var sdkMessage = Fetch(svc, "sdkmessage", "name", message, "sdkmessageid");
            if (sdkMessage.Length != 1)
                throw new InvalidOperationException("SDK message unavailable for " + name);
            var q = new QueryExpression("sdkmessagefilter")
            {
                ColumnSet = new ColumnSet("sdkmessagefilterid"), TopCount = 3
            };
            q.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, sdkMessage[0].Id);
            q.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, Table);
            var filters = svc.RetrieveMultiple(q).Entities;
            if (filters.Count != 1)
                throw new InvalidOperationException("SDK filter unavailable for " + name);
            var step = new Entity("sdkmessageprocessingstep");
            step["name"] = name;
            step["eventhandler"] = new EntityReference("plugintype", typeId);
            step["sdkmessageid"] = new EntityReference("sdkmessage", sdkMessage[0].Id);
            step["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filters[0].Id);
            step["stage"] = new OptionSetValue(10);
            step["mode"] = new OptionSetValue(0);
            step["rank"] = 1;
            step["supporteddeployment"] = new OptionSetValue(0);
            var create = new CreateRequest { Target = step };
            create.Parameters["SolutionUniqueName"] = Solution;
            svc.Execute(create);
            Console.WriteLine("F5_GRANT_GUARD_CREATED " + name);
        }
        if (required && missing != 0)
            throw new InvalidOperationException("Cash access grant guard incomplete.");
        Console.WriteLine("F5_GRANT_GUARD_STEPS=" + (3 - missing) +
            "/3, missing=" + missing);
        return 0;
    }

    public static int Probe(ServiceClient svc)
    {
        Steps(svc, false, true);
        var attempt = new Entity(Table);
        attempt["fmg_name"] = "F5-DENY-TEST-" + Guid.NewGuid().ToString("N");
        try
        {
            svc.Create(attempt);
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
        {
            if (!(ex.Detail?.Message ?? ex.Message).Contains("F5A009"))
                throw new InvalidOperationException("Wrong failure from grant guard.", ex);
            Console.WriteLine("F5_GRANT_CREATE_DENIED_F5A009");
            return 0;
        }
        throw new InvalidOperationException("UNSAFE: Direct grant CRUD accepted.");
    }
}
