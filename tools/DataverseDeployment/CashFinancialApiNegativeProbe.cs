using System;
using System.Linq;
using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// A DEV-only negative transport probe. Uses nonexistent random accounts
/// and zero approved grants. It NEVER creates financial data as part of a
/// successful test; if either API unexpectedly returns success, abort.
/// No second person is needed to verify closed-by-default authorization.
/// </summary>
internal static class CashFinancialApiNegativeProbe
{
    private static bool Exists(ServiceClient client, string table,
        string field, object value)
    {
        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(false), TopCount = 1
        };
        query.Criteria.AddCondition(field, ConditionOperator.Equal, value);
        var result = client.RetrieveMultiple(query);
        if (result == null || result.MoreRecords)
            throw new InvalidOperationException("Incomplete F5 negative-probe preflight.");
        return result.Entities.Count != 0;
    }

    public static int Run(ServiceClient client)
    {
        CashGuardDeployment.Run(client, false, true);
        CashGrantGuardDeployment.Steps(client, false, true);
        CashGrantRequestGuardDeployment.Steps(client, false, true);
        CashFinancialExecutionPackageDeployment.RequireRegistered(client, true);
        CashFinancialApiDeployment.Run(client, false);
        CashFinancialStepReadiness.Inspect(client);
        CashSchemaReadiness.Verify(client);

        if (Exists(client, "fmg_cashaccessgrant", "fmg_isactive", true))
            throw new InvalidOperationException("Cannot safely probe when active grants exist.");
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        if (Exists(client, "fmg_cashaccount", "fmg_cashaccountid", a) ||
            Exists(client, "fmg_cashaccount", "fmg_cashaccountid", b))
            throw new InvalidOperationException("Synthetic test accounts collided with a real record.");

        var keyPrefix = "F5-DENY-" + Guid.NewGuid().ToString("N");
        foreach (var spec in new[] {
            (Api: "fmg_SettleCashReceipt", Table: "fmg_cashreceipt", Transfer: false),
            (Api: "fmg_PostCashTransfer", Table: "fmg_cashtransfer", Transfer: true)
        })
        {
            var key = keyPrefix + (spec.Transfer ? "-TR" : "-RC");
            if (Exists(client, spec.Table, "fmg_externalrequestid", key))
                throw new InvalidOperationException("Negative probe key already used.");

            var request = new OrganizationRequest(spec.Api);
            request["RequestKey"] = key;
            request["SourceAccount"] = new EntityReference("fmg_cashaccount", a);
            if (spec.Transfer)
                request["DestinationAccount"] =
                    new EntityReference("fmg_cashaccount", b);
            request["Amount"] = new Money(0.01m);
            request["ValueDate"] = DateTime.Today;
            request["EvidenceReference"] = "F5 synthetic denied execution; no actual account";
            string denial = "";
            try
            {
                client.Execute(request);
            }
            catch (FaultException<OrganizationServiceFault> e)
            {
                denial = e.Detail?.Message ?? e.Message;
            }
            catch (InvalidPluginExecutionException e)
            {
                denial = e.Message;
            }
            if (string.IsNullOrWhiteSpace(denial))
                throw new InvalidOperationException(
                    "UNSAFE: ungranted financial Custom API succeeded: " + spec.Api);
            if (Exists(client, spec.Table, "fmg_externalrequestid", key) ||
                Exists(client, "fmg_cashposting", "fmg_externalrequestid",
                    "F5-" + key + "-0"))
                throw new InvalidOperationException(
                    "UNSAFE: rejected financial Custom API persisted a ledger row.");
            Console.WriteLine("F5_UNGRANTED_API_REJECTED_WITHOUT_ROWS " + spec.Api);
        }
        CashGuardDeployment.Run(client, false, true);
        CashGrantGuardDeployment.Steps(client, false, true);
        CashGrantRequestGuardDeployment.Steps(client, false, true);
        Console.WriteLine("F5_UNGRANTED_FINANCIAL_APIS_FAIL_CLOSED_REAL_DATAVERSE");
        return 0;
    }
}
