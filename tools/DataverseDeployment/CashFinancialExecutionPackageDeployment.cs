using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class CashFinancialExecutionPackageDeployment
{
    internal const string Name = "fmg_CashExecution";
    internal const string Endpoint = "FinancialManager.CashOperations.F5_CashOperationEndpoint";
    internal const string Guard = "FinancialManager.CashOperations.F5_ValidatedNestedCashGuard";
    private const string Solution = "FinancialManager";

    private static Entity[] Find(ServiceClient service, string logical,
        string name, object value, params string[] columns)
    {
        var q = new QueryExpression(logical)
        {
            ColumnSet = new ColumnSet(columns), TopCount = 5
        };
        q.Criteria.AddCondition(name, ConditionOperator.Equal, value);
        var result = service.RetrieveMultiple(q);
        if (result == null || result.MoreRecords || result.Entities.Count > 4)
            throw new InvalidOperationException("Ambiguous or partial F5 package metadata.");
        return result.Entities.ToArray();
    }
    public static Guid RequireRegistered(ServiceClient service, bool required)
    {
        var packages = Find(service, "pluginpackage", "name", Name,
            "pluginpackageid", "name", "version");
        if (packages.Length > 1)
            throw new InvalidOperationException("Ambiguous CashExecution package.");
        if (packages.Length == 0)
        {
            if (required)
                throw new InvalidOperationException("CashExecution package is not registered.");
            return Guid.Empty;
        }
        var pkg = packages[0];
        if (pkg.Id == Guid.Empty ||
            pkg.GetAttributeValue<string>("name") != Name ||
            pkg.GetAttributeValue<string>("version") != "1.0.0")
            throw new InvalidOperationException("Unexpected CashExecution package revision.");
        foreach (var type in new[] { Endpoint, Guard })
        {
            var matches = Find(service, "plugintype", "typename", type,
                "plugintypeid", "typename", "pluginassemblyid");
            if (matches.Length != 1 || matches[0].Id == Guid.Empty)
                throw new InvalidOperationException("Missing unique F5 plugin type: " + type);
            var assembly = matches[0].GetAttributeValue<EntityReference>("pluginassemblyid");
            if (assembly == null || assembly.Id == Guid.Empty)
                throw new InvalidOperationException("Missing financial plugin assembly.");
            var record = service.Retrieve("pluginassembly", assembly.Id,
                new ColumnSet("packageid", "name"));
            if (record == null || record.Id != assembly.Id ||
                record.GetAttributeValue<EntityReference>("packageid")?.Id != pkg.Id)
                throw new InvalidOperationException("Financial plugin assembly belongs to another package.");
        }
        Console.WriteLine("F5_CASH_EXECUTION_PACKAGE_VALIDATED");
        return pkg.Id;
    }

    public static int Run(ServiceClient service, bool apply)
    {
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashGrantRequestGuardDeployment.Steps(service, false, true);
        if (!apply)
        {
            var registered = RequireRegistered(service, false);
            Console.WriteLine("F5_CASH_EXECUTION_PACKAGE_PRESENT=" + (registered != Guid.Empty));
            if (registered != Guid.Empty)
                Console.WriteLine("F5_CASH_EXECUTION_PACKAGE_ID=" + registered.ToString("D"));
            return 0;
        }
        CashLedgerContractReadiness.Inspect(service);
        CashSchemaReadiness.Verify(service);
        if (RequireRegistered(service, false) != Guid.Empty)
            throw new InvalidOperationException("Never overwrite an already installed financial execution package.");
        foreach (var type in new[] { Endpoint, Guard })
        {
            if (Find(service, "plugintype", "typename", type, "plugintypeid").Length != 0)
                throw new InvalidOperationException("Financial type already exists in another package.");
        }
        var file = Environment.GetEnvironmentVariable("FM_CASH_EXECUTION_PACKAGE_PATH");
        var sha = Environment.GetEnvironmentVariable("FM_CASH_EXECUTION_PACKAGE_SHA256");
        if (string.IsNullOrWhiteSpace(file) || !Path.IsPathRooted(file) ||
            !File.Exists(file) || Path.GetFileName(file) != Name + ".1.0.0.nupkg" ||
            string.IsNullOrWhiteSpace(sha) || sha.Length != 64)
            throw new InvalidOperationException("Exact versioned package with expected SHA-256 required.");
        var bytes = File.ReadAllBytes(file);
        if (bytes.Length < 5000 || bytes.Length > 1000000)
            throw new InvalidOperationException("Unexpected new financial package size.");
        using (var algorithm = SHA256.Create())
        {
            var actual = BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "");
            if (!actual.Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CashExecution NuGet hash mismatch.");
        }
        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            var entries = archive.Entries.Select(x => x.FullName).ToArray();
            if (entries.Count(x => x == "lib/net48/FinancialManager.CashExecution.dll") != 1 ||
                entries.Count(x => x == "lib/net48/FinancialManager.CashDomain.dll") != 1 ||
                entries.Any(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                    x != "lib/net48/FinancialManager.CashExecution.dll" &&
                    x != "lib/net48/FinancialManager.CashDomain.dll"))
                throw new InvalidOperationException("Unexpected plugin dependencies in NuGet.");
        }
        var create = new Entity("pluginpackage");
        create["name"] = Name;
        create["uniquename"] = Name;
        create["version"] = "1.0.0";
        create["content"] = Convert.ToBase64String(bytes);
        var request = new CreateRequest { Target = create };
        request.Parameters["SolutionUniqueName"] = Solution;
        var response = (CreateResponse)service.Execute(request);
        if (response.id == Guid.Empty)
            throw new InvalidOperationException("CashExecution package did not return ID.");
        if (RequireRegistered(service, true) != response.id)
            throw new InvalidOperationException("CashExecution package or type identity mismatch after registration.");
        CashGuardDeployment.Run(service, false, true);
        CashGrantGuardDeployment.Steps(service, false, true);
        CashGrantRequestGuardDeployment.Steps(service, false, true);
        Console.WriteLine("F5_CASH_EXECUTION_NEW_PACKAGE_CREATED_WITHOUT_GUARD_CUTOVER");
        return 0;
    }
}
