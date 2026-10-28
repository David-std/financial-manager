using System;
using System.Linq;
using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;

internal static class Fm11DraftApiProbe
{
    private const string Api = "fmg_CreateDraftFromMailIntake";
    private const string Intake = "fmg_mailintake";
    private const string Financial = "fmg_financialtransaction";

    public static int Rejection(ServiceClient svc)
    {
        var request = new OrganizationRequest(Api) {
            ["IntakeId"] = Guid.NewGuid(),
            ["ExpectedRowVersion"] = "invalid-version"
        };
        try { svc.Execute(request); }
        catch (FaultException<OrganizationServiceFault> fault)
        {
            if (fault.ToString().Contains("FM11002",StringComparison.Ordinal))
            {
                Console.WriteLine("FM11_DRAFT_API_REJECTS_INVALID_VERSION=true");
                Console.WriteLine("FM11_DRAFT_API_NEGATIVE_FINANCIAL_WRITES=0");
                return 0;
            }
            throw new InvalidOperationException("Unexpected Draft API rejection.",fault);
        }
        throw new InvalidOperationException("Draft API accepted invalid row version.");
    }

    public static int Synthetic(ServiceClient svc)
    {
        // A full DEV transaction test with exact cleanup. Never run against production.
        var requestId = "FM11-SYNTHETIC-" + Guid.NewGuid().ToString("N");
        var raw = "{" +
          "\"requestId\":\"" + requestId + "\"," +
          "\"name\":\"Synthetic FM11 draft\"," +
          "\"amountPen\":\"1.00\"," +
          "\"date\":\"2026-10-29\"," +
          "\"categoryCode\":\"103\"," +
          "\"branchCode\":\"LM\"," +
          "\"accountName\":\"Caja La Merced\"," +
          "\"flowClass\":\"capital\"," +
          "\"description\":\"Synthetic conversion acceptance test\"}";
        var source = new Entity(Intake) {
            ["fmg_name"] = "FM11 controlled draft test",
            ["fmg_requestid"] = requestId,
            ["fmg_sender"] = "test@example.invalid",
            ["fmg_mailmessageid"] = "synthetic-only",
            ["fmg_reviewstate"] = "Unverified",
            ["fmg_validationstate"] = "FormatChecked",
            ["fmg_rawbody"] = raw,
            ["fmg_concept"] = "Synthetic FM11 draft",
            ["fmg_amountpen"] = "1.00",
            ["fmg_requesteddate"] = "2026-10-29",
            ["fmg_categorycode"] = "103",
            ["fmg_branchcode"] = "LM",
            ["fmg_accountname"] = "Caja La Merced",
            ["fmg_flowclass"] = "capital"
        };
        Guid intakeId = Guid.Empty, draftId = Guid.Empty;
        try
        {
            intakeId = svc.Create(source);
            if (intakeId == Guid.Empty) throw new InvalidOperationException("Synthetic intake missing.");
            var loaded = svc.Retrieve(Intake, intakeId,
                new ColumnSet("fmg_reviewstate","fmg_validationstate"));
            if (String.IsNullOrWhiteSpace(loaded.RowVersion))
                throw new InvalidOperationException("Synthetic intake row version unavailable.");
            var request = new OrganizationRequest(Api) {
                ["IntakeId"] = intakeId,
                ["ExpectedRowVersion"] = loaded.RowVersion
            };
            var response = svc.Execute(request);
            draftId = response.Results.Contains("DraftId") && response["DraftId"] is Guid id
                ? id : Guid.Empty;
            if (draftId == Guid.Empty || response["Created"] is not bool created || !created)
                throw new InvalidOperationException("Synthetic API returned no Draft.");
            var draft = svc.Retrieve(Financial, draftId,
                new ColumnSet("fmg_status","fmg_externalrequestid","fmg_amount"));
            if (draft.GetAttributeValue<OptionSetValue>("fmg_status")?.Value != 797280000 ||
                draft.GetAttributeValue<string>("fmg_externalrequestid") != requestId ||
                draft.GetAttributeValue<Money>("fmg_amount")?.Value != 1m)
                throw new InvalidOperationException("Converted record not a valid Draft.");
            var staged = svc.Retrieve(Intake, intakeId, new ColumnSet("fmg_reviewstate"));
            if (staged.GetAttributeValue<string>("fmg_reviewstate") != "DraftCreated")
                throw new InvalidOperationException("Intake state not committed.");
            Console.WriteLine("FM11_DRAFT_SYNTHETIC_TRANSACTION_VERIFIED=true");

            // Same intake must never create a second financial transaction.
            try { svc.Execute(request); }
            catch (FaultException<OrganizationServiceFault>)
            {
                Console.WriteLine("FM11_DRAFT_REPLAY_REJECTED=true");
                return 0;
            }
            throw new InvalidOperationException("CRITICAL: replay was not rejected.");
        }
        finally
        {
            // Clean up even if a server unexpectedly accepted replay and returned a second Draft.
            var cleanup = new QueryExpression(Financial) {
                ColumnSet = new ColumnSet("fmg_status"), TopCount=10
            };
            cleanup.Criteria.AddCondition("fmg_externalrequestid", ConditionOperator.Equal, requestId);
            var matched = svc.RetrieveMultiple(cleanup);
            if (matched.MoreRecords)
                throw new InvalidOperationException("Incomplete synthetic Draft cleanup inventory.");
            foreach (var row in matched.Entities)
            {
                if (row.GetAttributeValue<OptionSetValue>("fmg_status")?.Value != 797280000)
                    throw new InvalidOperationException("Synthetic record is not safe to delete.");
                svc.Delete(Financial, row.Id);
            }
            if (intakeId != Guid.Empty) svc.Delete(Intake, intakeId);
            if (svc.RetrieveMultiple(cleanup).Entities.Count != 0)
                throw new InvalidOperationException("Synthetic Draft cleanup not confirmed.");
            Console.WriteLine("FM11_DRAFT_SYNTHETIC_CLEANUP_VERIFIED=true");
        }
    }
}
