using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class PaymentPackageInstallation
{
    private const string Package = "fmg_PaymentExecution";
    public static int Run(ServiceClient svc, bool apply)
    {
        if (ExpenseReadiness.Inspect(svc) != 0)
            throw new InvalidOperationException("Payment concurrency unavailable.");
        var q=new QueryExpression("pluginpackage") {
            ColumnSet=new ColumnSet("name","version","pluginpackageid"),TopCount=3
        };
        q.Criteria.AddCondition("name",ConditionOperator.Equal,Package);
        var existing=svc.RetrieveMultiple(q).Entities;
        if(existing.Count>1) throw new InvalidOperationException("Ambiguous payment package.");
        if(existing.Count==1)
        {
            if(existing[0].GetAttributeValue<string>("version")!="1.0.0")
                throw new InvalidOperationException("Unexpected payment package version.");
            Console.WriteLine("F4_PAYMENT_PACKAGE_PRESENT=true");
            return 0;
        }
        Console.WriteLine("F4_PAYMENT_PACKAGE_PRESENT=false");
        if(!apply) return 0;
        var path=Environment.GetEnvironmentVariable("FM_PAYMENT_PACKAGE_PATH");
        var sha=Environment.GetEnvironmentVariable("FM_PAYMENT_PACKAGE_SHA256");
        if(String.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path) ||
            !File.Exists(path)||Path.GetFileName(path)!="fmg_PaymentExecution.1.0.0.nupkg" ||
            String.IsNullOrWhiteSpace(sha)||sha.Length!=64)
            throw new InvalidOperationException("Verified absolute package path and SHA256 required.");
        var bytes=File.ReadAllBytes(path);
        if(bytes.Length<5000||bytes.Length>500000)
            throw new InvalidOperationException("Invalid package size.");
        using(var hash=SHA256.Create())
        {
            var actual=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","");
            if(!String.Equals(actual,sha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Package hash mismatch.");
        }
        using(var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read))
        {
            var dll=zip.Entries.Where(e=>e.FullName.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(dll.Length!=1 || dll[0].FullName!="lib/net48/FinancialManager.PaymentExecution.dll")
                throw new InvalidOperationException("Plugin package has unexpected assemblies.");
        }
        var row=new Entity("pluginpackage");
        row["name"]=Package;row["uniquename"]=Package;row["version"]="1.0.0";
        row["content"]=Convert.ToBase64String(bytes);
        var req=new CreateRequest{Target=row};
        req.Parameters["SolutionUniqueName"]="FinancialManager";
        var created=(CreateResponse)svc.Execute(req);
        if(created.id==Guid.Empty) throw new InvalidOperationException("Package install not confirmed.");
        var q2=new QueryExpression("plugintype") {
            ColumnSet=new ColumnSet("plugintypeid"),TopCount=2
        };
        q2.Criteria.AddCondition("typename",ConditionOperator.Equal,
            "FinancialManager.PaymentExecution.PaymentCommandEndpoint");
        if(svc.RetrieveMultiple(q2).Entities.Count!=1)
            throw new InvalidOperationException("Installed API handler not found exactly once.");
        Console.WriteLine("F4_PAYMENT_PACKAGE_INSTALLED_WITH_FTV007_UNCHANGED");
        return 0;
    }
}
