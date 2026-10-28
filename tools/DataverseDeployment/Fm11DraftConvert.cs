using System;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static class Fm11DraftConvert
{
    public static int Run(ServiceClient client, bool apply)
    {
        var requestId = Environment.GetEnvironmentVariable("FM11_REQUEST_ID")?.Trim();
        if (String.IsNullOrWhiteSpace(requestId) || requestId.Length > 80 ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                requestId, @"^[A-Za-z0-9_-]{12,80}$"))
            throw new InvalidOperationException("Provide an exact FM11_REQUEST_ID.");
        var query = new QueryExpression("fmg_mailintake") {
            ColumnSet = new ColumnSet("fmg_requestid","fmg_reviewstate","fmg_validationstate"),
            TopCount = 2
        };
        query.Criteria.AddCondition("fmg_requestid", ConditionOperator.Equal, requestId);
        var rows = client.RetrieveMultiple(query).Entities;
        if(rows.Count != 1) throw new InvalidOperationException("Intake request not unique or missing.");
        var intake = rows[0];
        if(intake.GetAttributeValue<string>("fmg_reviewstate") != "Unverified" ||
           intake.GetAttributeValue<string>("fmg_validationstate") != "FormatChecked" ||
           String.IsNullOrWhiteSpace(intake.RowVersion))
            throw new InvalidOperationException("Intake not eligible for conversion.");
        Console.WriteLine("FM11_DRAFT_REQUEST_ELIGIBLE_FOR_AUTHORIZED_REVIEW=true");
        if(!apply)
        {
            Console.WriteLine("FM11_DRAFT_PREVIEW_ONLY=true");
            return 0;
        }
        var request = new OrganizationRequest("fmg_CreateDraftFromMailIntake") {
            ["IntakeId"] = intake.Id,
            ["ExpectedRowVersion"] = intake.RowVersion
        };
        var result = client.Execute(request);
        if(!result.Results.Contains("DraftId") ||
            !(result.Results["DraftId"] is Guid id) || id == Guid.Empty ||
            !result.Results.Contains("Created") ||
            !(result.Results["Created"] is bool complete) || !complete)
            throw new InvalidOperationException("Dataverse Draft conversion not confirmed.");
        Console.WriteLine("FM11_DRAFT_CREATED=true");
        Console.WriteLine("FM11_DRAFT_ID=" + id.ToString("D"));
        Console.WriteLine("FM11_DRAFT_POSTED=false");
        return 0;
    }
}
