using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// Read-only, fail-closed F5 server schema contract for the SDK ledger writer.
/// No settlement, grant, metadata mutation, or synthetic financial row.
/// </summary>
internal static class CashLedgerContractReadiness
{
    private static readonly (string Table, string Key)[] Ledger =
    {
        ("fmg_cashreceipt", "fmg_CR_Request"),
        ("fmg_cashtransfer", "fmg_CT_Request"),
        ("fmg_cashposting", "fmg_CP_Leg")
    };

    private static EntityMetadata Metadata(ServiceClient service, string table)
    {
        var response = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = table,
            EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = true
        });
        return response.EntityMetadata ??
            throw new InvalidOperationException("Missing server F5 ledger schema: " + table);
    }

    private static T Attribute<T>(EntityMetadata meta, string field) where T : AttributeMetadata
    {
        var attributes = meta.Attributes.Where(a =>
            string.Equals(a.LogicalName, field, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (attributes.Length != 1 || !(attributes[0] is T typed))
            throw new InvalidOperationException("Ledger field type mismatch " + meta.LogicalName + "." + field);
        if (typed.IsValidForCreate != true && typed.IsValidForUpdate != true)
            throw new InvalidOperationException("Ledger field is not writeable " + meta.LogicalName + "." + field);
        return typed;
    }

    private static void Text(EntityMetadata meta, string field, int requiredMax)
    {
        var a = Attribute<StringAttributeMetadata>(meta, field);
        if (a.MaxLength < requiredMax)
            throw new InvalidOperationException("Ledger text field too short: " + meta.LogicalName + "." + field);
    }

    private static void Money(EntityMetadata meta, string field, bool signed)
    {
        var a = Attribute<MoneyAttributeMetadata>(meta, field);
        if (a.MaxValue < 1000000000d || a.MinValue > (signed ? -1000000000d : 0d))
            throw new InvalidOperationException("Ledger money bounds mismatch: " + meta.LogicalName + "." + field);
        if (a.PrecisionSource != 0 || a.Precision != 2)
            throw new InvalidOperationException("Ledger money PEN precision must be two decimal places.");
    }

    private static void Lookup(EntityMetadata meta, string field, string target)
    {
        var a = Attribute<LookupAttributeMetadata>(meta, field);
        if (a.Targets == null || a.Targets.Length != 1 ||
            !string.Equals(a.Targets[0], target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsafe ledger lookup " + meta.LogicalName + "." + field);
    }

    private static void Choices(EntityMetadata meta, string field, params int[] expected)
    {
        var a = Attribute<PicklistAttributeMetadata>(meta, field);
        var actual = a.OptionSet?.Options.Select(o => o.Value).OrderBy(v => v).ToArray();
        if (actual == null || !actual.SequenceEqual(expected.OrderBy(v => v).Select(v => (int?)v)))
            throw new InvalidOperationException("Unexpected ledger status/kind options " + meta.LogicalName + "." + field);
    }

    private static void Date(EntityMetadata meta, string field)
    {
        var a = Attribute<DateTimeAttributeMetadata>(meta, field);
        if (a.Format != DateTimeFormat.DateOnly)
            throw new InvalidOperationException("Ledger dates must be date-only: " + field);
    }

    internal static int Inspect(ServiceClient service)
    {
        foreach (var (name, keyName) in Ledger)
        {
            var meta = Metadata(service, name);
            if (meta.OwnershipType != OwnershipTypes.UserOwned ||
                meta.IsAuditEnabled?.Value != true ||
                meta.IsOptimisticConcurrencyEnabled != true)
                throw new InvalidOperationException("F5 ledger metadata guardrails drifted: " + name);
            var keys = meta.Keys?.Where(k => string.Equals(k.SchemaName,
                keyName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (keys?.Length != 1 || keys[0].EntityKeyIndexStatus != EntityKeyIndexStatus.Active)
                throw new InvalidOperationException("Active unique ledger key missing: " + keyName);
            Text(meta, "fmg_externalrequestid", 70);
            Attribute<StringAttributeMetadata>(meta, "fmg_name");
            Attribute<LookupAttributeMetadata>(meta, "transactioncurrencyid");
            if (name == "fmg_cashreceipt" || name == "fmg_cashtransfer")
            {
                Text(meta, "fmg_requestfingerprint", 64);
                Text(meta, "fmg_initiatinguserid", 36);
                Money(meta, "fmg_amount", false);
            }
            if (name == "fmg_cashreceipt")
            {
                Choices(meta, "fmg_status", 797281000, 797281001, 797281002);
                Choices(meta, "fmg_scope", 797281010, 797281011);
                Lookup(meta, "fmg_cashaccount", "fmg_cashaccount");
                Lookup(meta, "fmg_branch", "fmg_branch");
                Date(meta, "fmg_receivedon");
            }
            else if (name == "fmg_cashtransfer")
            {
                Choices(meta, "fmg_status", 797281100, 797281101, 797281102);
                Lookup(meta, "fmg_sourceaccount", "fmg_cashaccount");
                Lookup(meta, "fmg_destinationaccount", "fmg_cashaccount");
                Date(meta, "fmg_transferredon");
            }
            else
            {
                Choices(meta, "fmg_kind", 797281200, 797281201, 797281202, 797281203);
                Text(meta, "fmg_operationid", 36);
                Money(meta, "fmg_signedamount", true);
                Lookup(meta, "fmg_cashaccount", "fmg_cashaccount");
                Date(meta, "fmg_postedon");
            }
            if (service.RetrieveMultiple(new QueryExpression(name)
            {
                ColumnSet = new ColumnSet(false), TopCount = 1
            }).Entities.Count != 0)
                throw new InvalidOperationException("F5 ledger inspection requires zero current rows: " + name);
            Console.WriteLine("F5_LEDGER_CONTRACT_VERIFIED " + name);
        }
        Console.WriteLine("F5_LEDGER_CONTRACT_ACCEPTED_FOR_SOURCE_MAPPING_ONLY");
        return 0;
    }
}
