using System;
using System.IO;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class CashRuntimeReadiness
{
    private const string Solution = "FinancialManager";
    private const string Package = "fmg_CashOperations";
    private const string Type = "FinancialManager.CashOperations.F5_RuntimeProbe";
    private const string Api = "fmg_CashTransactionReadiness";
    private const string Privilege = "prvReadfmg_CashTransfer";

    private static Entity[] Find(ServiceClient svc, string table, string field, object value,
        params string[] columns)
    {
        var q = new QueryExpression(table) { ColumnSet = new ColumnSet(columns), TopCount = 10 };
        q.Criteria.AddCondition(field, ConditionOperator.Equal, value);
        return svc.RetrieveMultiple(q).Entities.ToArray();
    }

    private static Guid TypeId(ServiceClient svc)
    {
        var list = Find(svc, "plugintype", "typename", Type, "plugintypeid");
        if (list.Length > 1) throw new InvalidOperationException("Ambiguous F5 runtime type.");
        return list.Length == 0 ? Guid.Empty : list[0].Id;
    }

    private static Entity[] FindApi(ServiceClient svc) =>
        Find(svc, "customapi", "uniquename", Api, "customapiid", "isprivate",
            "plugintypeid", "executeprivilegename", "bindingtype",
            "allowedcustomprocessingsteptype", "isfunction", "workflowsdkstepenabled");

    public static int Run(ServiceClient svc, string mode)
    {
        if (mode == "cash-runtime-inspect")
        {
            var pkgs = Find(svc, "pluginpackage", "name", Package, "pluginpackageid");
            var apis = FindApi(svc);
            if (pkgs.Length > 1 || apis.Length > 1)
                throw new InvalidOperationException("Ambiguous runtime package or API.");
            Console.WriteLine("F5_RUNTIME_PACKAGE_COUNT=" + pkgs.Length);
            Console.WriteLine("F5_RUNTIME_TYPE_PRESENT=" + (TypeId(svc) != Guid.Empty));
            Console.WriteLine("F5_RUNTIME_API_COUNT=" + apis.Length);
            if (apis.Length == 1) ValidateApi(apis[0], TypeId(svc));
            return 0;
        }
        CashGuardDeployment.Run(svc, false, true);
        if (mode == "cash-runtime-register")
        {
            if (Find(svc, "pluginpackage", "name", Package, "pluginpackageid").Length != 0)
                throw new InvalidOperationException("Never overwrite an existing F5 package.");
            var path = Environment.GetEnvironmentVariable("FM_CASH_RUNTIME_PACKAGE_PATH");
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
                Path.GetFileName(path) != "fmg_CashOperations.1.0.0.nupkg")
                throw new InvalidOperationException("Validated fresh runtime NuGet required.");
            var data = File.ReadAllBytes(path);
            if (data.Length < 2000 || data.Length > 2000000)
                throw new InvalidOperationException("Unexpected package size.");
            var row = new Entity("pluginpackage");
            row["name"] = Package;
            row["uniquename"] = Package;
            row["version"] = "1.0.0";
            row["content"] = Convert.ToBase64String(data);
            var request = new CreateRequest { Target = row };
            request.Parameters["SolutionUniqueName"] = Solution;
            var result = (CreateResponse)svc.Execute(request);
            if (result.id == Guid.Empty || TypeId(svc) == Guid.Empty)
                throw new InvalidOperationException("Registered package without expected type.");
            Console.WriteLine("F5_RUNTIME_READONLY_PACKAGE_REGISTERED");
            return 0;
        }
        if (mode == "cash-runtime-api")
        {
            var pluginId = TypeId(svc);
            if (pluginId == Guid.Empty || Find(svc, "privilege", "name", Privilege, "privilegeid").Length != 1)
                throw new InvalidOperationException("Exact plugin and read privilege required.");
            var apis = FindApi(svc);
            if (apis.Length > 1) throw new InvalidOperationException("Duplicate F5 runtime APIs.");
            if (apis.Length == 0)
            {
                var api = new Entity("customapi");
                api["name"] = Api;
                api["uniquename"] = Api;
                api["displayname"] = "F5 Dataverse transaction diagnostic (read only)";
                api["description"] = "Execution context only; no financial data or writes.";
                api["bindingtype"] = new OptionSetValue(0);
                api["allowedcustomprocessingsteptype"] = new OptionSetValue(0);
                api["isfunction"] = false;
                api["isprivate"] = true;
                api["workflowsdkstepenabled"] = false;
                api["executeprivilegename"] = Privilege;
                api["plugintypeid"] = new EntityReference("plugintype", pluginId);
                var create = new CreateRequest { Target = api };
                create.Parameters["SolutionUniqueName"] = Solution;
                if (((CreateResponse)svc.Execute(create)).id == Guid.Empty)
                    throw new InvalidOperationException("Runtime API create returned empty ID.");
                Console.WriteLine("F5_RUNTIME_READONLY_API_CREATED");
            }
            apis = FindApi(svc);
            if (apis.Length != 1) throw new InvalidOperationException("Runtime API not unique.");
            ValidateApi(apis[0], pluginId);
            foreach (var entry in new[] { ("InTransaction", 0), ("ObservedStage", 7),
                                           ("SameIdentity", 0), ("CashWritesEnabled", 0) })
            {
                var matches = Find(svc, "customapiresponseproperty", "uniquename", entry.Item1,
                    "customapiresponsepropertyid", "customapiid", "type");
                var linked = matches.Where(x =>
                    x.GetAttributeValue<EntityReference>("customapiid")?.Id == apis[0].Id).ToArray();
                if (linked.Length > 1) throw new InvalidOperationException("Duplicated response property.");
                if (linked.Length == 1)
                {
                    if (linked[0].GetAttributeValue<OptionSetValue>("type")?.Value != entry.Item2)
                        throw new InvalidOperationException("Response property type mismatch.");
                    continue;
                }
                var field = new Entity("customapiresponseproperty");
                field["name"] = Api + "." + entry.Item1;
                field["uniquename"] = entry.Item1;
                field["type"] = new OptionSetValue(entry.Item2);
                field["customapiid"] = new EntityReference("customapi", apis[0].Id);
                var create = new CreateRequest { Target = field };
                create.Parameters["SolutionUniqueName"] = Solution;
                svc.Execute(create);
                Console.WriteLine("F5_RUNTIME_RESPONSE_CREATED " + entry.Item1);
            }
            Console.WriteLine("F5_RUNTIME_READONLY_API_READY");
            return 0;
        }
        if (mode == "cash-runtime-probe")
        {
            var apis = FindApi(svc);
            if (apis.Length != 1) throw new InvalidOperationException("Runtime API missing.");
            ValidateApi(apis[0], TypeId(svc));
            var result = svc.Execute(new OrganizationRequest(Api));
            var stage = (int)result.Results["ObservedStage"];
            var inTransaction = (bool)result.Results["InTransaction"];
            var sameIdentity = (bool)result.Results["SameIdentity"];
            var writes = (bool)result.Results["CashWritesEnabled"];
            if (stage != 30 || writes)
                throw new InvalidOperationException("Unsafe runtime observation.");
            Console.WriteLine("F5_RUNTIME_OBSERVED_STAGE=" + stage);
            Console.WriteLine("F5_RUNTIME_IN_TRANSACTION=" + inTransaction);
            Console.WriteLine("F5_RUNTIME_SAME_IDENTITY=" + sameIdentity);
            Console.WriteLine("F5_RUNTIME_CASH_WRITES_ENABLED=False");
            Console.WriteLine("F5_RUNTIME_PROBE_NO_FINANCIAL_WRITES");
            return 0;
        }
        throw new InvalidOperationException("Unsupported F5 diagnostic mode.");
    }

    private static void ValidateApi(Entity api, Guid pluginId)
    {
        if (pluginId == Guid.Empty ||
            api.GetAttributeValue<EntityReference>("plugintypeid")?.Id != pluginId ||
            api.GetAttributeValue<string>("executeprivilegename") != Privilege ||
            !api.GetAttributeValue<bool>("isprivate") ||
            api.GetAttributeValue<OptionSetValue>("bindingtype")?.Value != 0 ||
            api.GetAttributeValue<OptionSetValue>("allowedcustomprocessingsteptype")?.Value != 0 ||
            api.GetAttributeValue<bool>("isfunction") ||
            api.GetAttributeValue<bool>("workflowsdkstepenabled"))
            throw new InvalidOperationException("F5 runtime API security mismatch.");
    }
}
