using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// Registers or verifies fail-closed F5 cash writes, without modifying the
/// payment or budget plugin packages. Only an existing validated CashIntegrity
/// plugin type can be used. No production settlement or cash posting API.
/// </summary>
internal static class CashGuardDeployment
{
    internal const string TypeName = "FinancialManager.CashIntegrity.F5_BlockDirectCashWrites";
    private const string Solution = "FinancialManager";
    private static readonly string[] Tables =
    {
        "fmg_cashreceipt", "fmg_cashtransfer", "fmg_cashposting",
        "fmg_accountopening", "fmg_bankstatement", "fmg_bankreconciliation"
    };
    private static readonly string[] Messages = { "Create", "Update", "Delete" };
    private static string StepName(string table, string message) => "F5_Block_" + table + "_" + message;

    private static EntityCollection Query(ServiceClient svc, string logical, string field,
                                          object value, params string[] columns)
    {
        var query = new QueryExpression(logical)
        {
            ColumnSet = new ColumnSet(columns), TopCount = 3
        };
        query.Criteria.AddCondition(field, ConditionOperator.Equal, value);
        return svc.RetrieveMultiple(query);
    }

    public static int Run(ServiceClient svc, bool apply, bool requireComplete = false)
    {
        var types = Query(svc, "plugintype", "typename", TypeName, "plugintypeid", "typename");
        if (types.Entities.Count > 1)
            throw new InvalidOperationException("Duplicate F5 plugin types; abort.");
        Guid typeId = types.Entities.Count == 1 ? types.Entities[0].Id : Guid.Empty;
        if (apply && typeId == Guid.Empty)
            throw new InvalidOperationException("F5 guard package/type must be validated and registered first.");
        int missing = 0;
        foreach (var table in Tables)
        {
            foreach (var message in Messages)
            {
                string stepName = StepName(table, message);
                var steps = Query(svc, "sdkmessageprocessingstep", "name", stepName,
                    "sdkmessageprocessingstepid", "stage", "mode", "statecode",
                    "eventhandler", "sdkmessageid", "sdkmessagefilterid");
                if (steps.Entities.Count > 1)
                    throw new InvalidOperationException("Duplicate F5 step: " + stepName);
                if (steps.Entities.Count == 1)
                {
                    var s = steps.Entities[0];
                    var handler = s.GetAttributeValue<EntityReference>("eventhandler");
                    if (typeId == Guid.Empty || handler == null || handler.Id != typeId ||
                        s.GetAttributeValue<OptionSetValue>("stage")?.Value != 10 ||
                        s.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                        s.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                        throw new InvalidOperationException("Unexpected F5 guard step configuration: " + stepName);
                    Console.WriteLine("F5_GUARD_ACTIVE " + stepName);
                    continue;
                }
                missing++;
                Console.WriteLine("F5_GUARD_MISSING " + stepName);
                if (!apply) continue;
                var sdkMessage = Query(svc, "sdkmessage", "name", message, "sdkmessageid");
                if (sdkMessage.Entities.Count != 1)
                    throw new InvalidOperationException("Expected unique SDK message: " + message);
                var q = new QueryExpression("sdkmessagefilter")
                {
                    ColumnSet = new ColumnSet("sdkmessagefilterid"), TopCount = 3
                };
                q.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, sdkMessage.Entities[0].Id);
                q.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, table);
                var filters = svc.RetrieveMultiple(q).Entities;
                if (filters.Count != 1)
                    throw new InvalidOperationException("Expected SDK filter for " + stepName);
                var step = new Entity("sdkmessageprocessingstep");
                step["name"] = stepName;
                step["eventhandler"] = new EntityReference("plugintype", typeId);
                step["sdkmessageid"] = new EntityReference("sdkmessage", sdkMessage.Entities[0].Id);
                step["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filters[0].Id);
                step["stage"] = new OptionSetValue(10); // PreValidation
                step["mode"] = new OptionSetValue(0); // Synchronous
                step["rank"] = 1;
                step["supporteddeployment"] = new OptionSetValue(0);
                var create = new CreateRequest { Target = step };
                create.Parameters["SolutionUniqueName"] = Solution;
                svc.Execute(create);
                Console.WriteLine("F5_GUARD_CREATED " + stepName);
            }
        }
        if (requireComplete && missing != 0)
            throw new InvalidOperationException("F5 guard is incomplete: " + missing + " missing steps.");
        Console.WriteLine("F5_GUARD_STEPS " + (18 - missing) + "/18 already valid, " +
            (apply ? missing + " newly created" : missing + " missing") + ".");
        return 0;
    }

    /// <summary>Asserts that every F5 Create is rejected with F5G001.
    /// No financial records are created or edited; a single Create request must fail.</summary>
    public static int Probe(ServiceClient svc)
    {
        Run(svc, false, true);
        foreach (var table in Tables)
        {
            var data = new Entity(table);
            data["fmg_name"] = "F5-REJECT-" + Guid.NewGuid().ToString("N");
            try
            {
                svc.Create(data);
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> exception)
            {
                if (!(exception.Detail?.Message ?? exception.Message).Contains("F5G001"))
                    throw new InvalidOperationException(
                        "F5 guard did not reject with expected code for " + table, exception);
                Console.WriteLine("F5_GUARD_DENIED_CREATE " + table);
                continue;
            }
            throw new InvalidOperationException(
                "UNSAFE: cash table was directly writable without server guard: " + table);
        }
        Console.WriteLine("F5_GUARD_DENIED_ALL_SIX_CREATES");
        return 0;
    }
}
