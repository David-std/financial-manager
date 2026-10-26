using System;
using System.IO;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class CashGuardRegistration
{
    private const string PackageName = "fmg_CashIntegrity";
    private const string PluginType = "FinancialManager.CashIntegrity.F5_BlockDirectCashWrites";
    public static int Run(ServiceClient svc)
    {
        var q = new QueryExpression("pluginpackage")
        {
            ColumnSet = new ColumnSet("pluginpackageid", "name"), TopCount = 2
        };
        q.Criteria.AddCondition("name", ConditionOperator.Equal, PackageName);
        var existing = svc.RetrieveMultiple(q).Entities;
        if (existing.Count != 0)
            throw new InvalidOperationException("F5 package already exists. Never overwrite it through initial registration.");

        var path = Environment.GetEnvironmentVariable("FM_CASH_PACKAGE_PATH");
        if (String.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path) != "fmg_CashIntegrity.1.0.0.nupkg")
            throw new InvalidOperationException("FM_CASH_PACKAGE_PATH must point to a validated fresh cash guard package.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 2000 || bytes.Length > 2000000)
            throw new InvalidOperationException("Unexpected package size.");

        var package = new Entity("pluginpackage");
        package["name"] = PackageName;
        package["uniquename"] = "fmg_CashIntegrity";
        package["version"] = "1.0.0";
        package["content"] = Convert.ToBase64String(bytes);
        var create = new CreateRequest { Target = package };
        create.Parameters["SolutionUniqueName"] = "FinancialManager";
        var response = (CreateResponse)svc.Execute(create);
        if (response.id == Guid.Empty)
            throw new InvalidOperationException("Plugin package creation returned no ID.");

        var types = new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet("plugintypeid"), TopCount = 2
        };
        types.Criteria.AddCondition("typename", ConditionOperator.Equal, PluginType);
        if (svc.RetrieveMultiple(types).Entities.Count != 1)
            throw new InvalidOperationException("Cash guard package was registered but plugin class not found.");
        Console.WriteLine("F5_CASH_GUARD_PACKAGE_REGISTERED");
        return 0;
    }
}
