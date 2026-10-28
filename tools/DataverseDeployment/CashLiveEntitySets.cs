using System;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

/// <summary>Read-only schema check for F5 LIVE lists; no data disclosure.</summary>
internal static class CashLiveEntitySets
{
    private static readonly (string Logical, string[] Required)[] Tables = {
        ("fmg_cashaccount", new[]{"fmg_cashaccountid","fmg_name","fmg_isactive"}),
        ("fmg_cashreceipt", new[]{"fmg_cashreceiptid","fmg_name","fmg_amount","fmg_status","fmg_receivedon"}),
        ("fmg_cashtransfer",new[]{"fmg_cashtransferid","fmg_name","fmg_amount","fmg_status","fmg_transferredon"}),
        ("fmg_cashposting",new[]{"fmg_cashpostingid","fmg_name","fmg_signedamount","fmg_postedon"}),
        ("fmg_accountopening",new[]{"fmg_accountopeningid","fmg_name","fmg_amount","fmg_status","fmg_effectiveon"}),
        ("fmg_bankstatement",new[]{"fmg_bankstatementid","fmg_name","fmg_closingbalance","fmg_status","fmg_asof"}),
        ("fmg_bankreconciliation",new[]{"fmg_bankreconciliationid","fmg_name","fmg_difference","fmg_status","fmg_asof"})
    };
    public static int Inspect(ServiceClient service)
    {
        CashGuardDeployment.Run(service,false,true);
        CashGrantGuardDeployment.Steps(service,false,true);
        CashGrantRequestGuardDeployment.Steps(service,false,true);
        foreach (var (logical,required) in Tables)
        {
            var meta=((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest{
                LogicalName=logical,EntityFilters=EntityFilters.All,
                RetrieveAsIfPublished=false
            })).EntityMetadata;
            var missing = required.Except(meta?.Attributes?.Select(x=>x.LogicalName) ??
                Array.Empty<string>(),StringComparer.OrdinalIgnoreCase).ToArray();
            Console.WriteLine("F5_LIVE_METADATA "+logical+" entityset="+meta?.EntitySetName+
                " audited="+meta?.IsAuditEnabled?.Value+
                " concurrency="+meta?.IsOptimisticConcurrencyEnabled+
                " missing="+String.Join(",",missing));
            if(meta == null || String.IsNullOrWhiteSpace(meta.EntitySetName) ||
                missing.Length != 0 ||
                (logical != "fmg_cashaccount" &&
                  (meta.IsOptimisticConcurrencyEnabled != true ||
                   meta.IsAuditEnabled?.Value != true)))
                throw new InvalidOperationException("Missing published, guarded F5 list contract: "+logical);
            Console.WriteLine("F5_LIVE_SET_VERIFIED "+logical+" "+meta.EntitySetName);
        }
        Console.WriteLine("F5_LIVE_ENTITYSETS_READONLY_VERIFIED");
        return 0;
    }
}
