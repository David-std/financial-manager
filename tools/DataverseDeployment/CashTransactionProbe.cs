using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Metadata;
using System.ServiceModel;

/// <summary>
/// Live server transaction probe with a mandatory failing final request.
/// Every created synthetic row MUST roll back; never use this as the production posting API.
/// </summary>
internal static class CashTransactionProbe
{
    public static int Run(ServiceClient svc)
    {
        CashSchemaReadiness.Verify(svc);
        foreach (var (entity, schema) in new[] {
            ("fmg_cashtransfer", "fmg_CT_Request"),
            ("fmg_cashposting", "fmg_CP_Leg") })
        {
            var metadata = ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest {
                LogicalName = entity, EntityFilters = EntityFilters.All,
                RetrieveAsIfPublished = true })).EntityMetadata;
            var key = metadata.Keys?.SingleOrDefault(k => k.SchemaName == schema);
            if (key?.EntityKeyIndexStatus != EntityKeyIndexStatus.Active)
                throw new InvalidOperationException("F5 idempotency key is not active: " + schema);
        }
        var accounts = new QueryExpression("fmg_cashaccount") {
            ColumnSet = new ColumnSet("fmg_cashaccountid"), TopCount = 5 };
        accounts.Criteria.AddCondition("fmg_isactive", ConditionOperator.Equal, true);
        var rows = svc.RetrieveMultiple(accounts).Entities;
        if (rows.Count < 2)
            throw new InvalidOperationException("Two active cash accounts required to verify a transfer.");
        var currency = new QueryExpression("transactioncurrency") {
            ColumnSet = new ColumnSet("transactioncurrencyid"), TopCount = 2 };
        currency.Criteria.AddCondition("isocurrencycode", ConditionOperator.Equal, "PEN");
        var pen = svc.RetrieveMultiple(currency).Entities;
        if (pen.Count != 1) throw new InvalidOperationException("Exactly one PEN currency is required.");

        var operation = Guid.NewGuid();
        var reference = "FM-F5-ROLLBACK-" + operation.ToString("N");
        var date = new DateTime(2026, 10, 25);
        Entity NewPosting(string key, Guid account, decimal signed)
        {
            var p = new Entity("fmg_cashposting") { Id = Guid.NewGuid() };
            p["fmg_name"] = reference + "-" + key;
            p["fmg_externalrequestid"] = reference + "-" + key;
            p["fmg_operationid"] = operation.ToString();
            p["fmg_kind"] = new OptionSetValue(797281202); // Transfer
            p["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", account);
            p["fmg_signedamount"] = new Money(signed);
            p["fmg_postedon"] = date;
            p["transactioncurrencyid"] = new EntityReference("transactioncurrency", pen[0].Id);
            return p;
        }

        var transfer = new Entity("fmg_cashtransfer") { Id = operation };
        transfer["fmg_name"] = reference;
        transfer["fmg_externalrequestid"] = reference;
        transfer["fmg_status"] = new OptionSetValue(797281100); // Draft only
        transfer["fmg_amount"] = new Money(17.25m);
        transfer["fmg_sourceaccount"] = new EntityReference("fmg_cashaccount", rows[0].Id);
        transfer["fmg_destinationaccount"] = new EntityReference("fmg_cashaccount", rows[1].Id);
        transfer["fmg_transferredon"] = date;
        transfer["transactioncurrencyid"] = new EntityReference("transactioncurrency", pen[0].Id);

        var tx = new ExecuteTransactionRequest {
            ReturnResponses = false,
            Requests = new OrganizationRequestCollection {
                new CreateRequest { Target = transfer },
                new CreateRequest { Target = NewPosting("OUT", rows[0].Id, -17.25m) },
                new CreateRequest { Target = NewPosting("IN", rows[1].Id, +17.25m) },
                // The last request MUST fail. An unknown record may never be updated.
                new UpdateRequest { Target = new Entity("fmg_cashtransfer") {
                    Id = Guid.NewGuid(),
                    ["fmg_description"] = "MANDATORY_ROLLBACK_SENTINEL" } }
            }
        };
        int index = -1;
        try
        {
            svc.Execute(tx);
            throw new InvalidOperationException("UNSAFE: rollback sentinel committed unexpectedly");
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            if (ex.Detail is ExecuteTransactionFault fault)
                index = fault.FaultedRequestIndex;
        }
        if (index != tx.Requests.Count - 1)
            throw new InvalidOperationException("F5 transaction failed before intended rollback sentinel; fault at " + index);

        foreach (var table in new[] { "fmg_cashtransfer", "fmg_cashposting" })
        {
            var q = new QueryExpression(table) { ColumnSet = new ColumnSet(false), TopCount = 1 };
            q.Criteria.AddCondition(table == "fmg_cashposting" ? "fmg_operationid" : "fmg_externalrequestid",
                ConditionOperator.Equal, table == "fmg_cashposting" ? operation.ToString() : reference);
            if (svc.RetrieveMultiple(q).Entities.Count > 0)
                throw new InvalidOperationException("UNSAFE: transaction left a persisted synthetic F5 row: " + table);
        }
        Console.WriteLine("F5_ROLLBACK_VERIFIED: 2 balanced legs and transfer reverted atomically; 0 persisted rows");
        return 0;
    }
}
