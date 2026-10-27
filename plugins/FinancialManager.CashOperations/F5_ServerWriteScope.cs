using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// EXPERIMENTAL SOURCE ONLY — never deploy as a replacement for a deny-all
    /// guard without an isolated Dataverse nested-pipeline proof and rollback.
    /// No client-provided tag, ParentContext.MessageName alone, depth alone,
    /// role claim, or global administrator bypass is accepted.
    /// </summary>
    public static class F5_ServerWriteScope
    {
        private const string TicketKey =
            "fmg.financialmanager.2026.cash.server.write.authorization.730e7852";

        public static void Issue(IPluginExecutionContext parent,
            string table, string message, Guid recordId)
        {
            if (parent == null || parent.Stage != 30 || parent.Mode != 0 ||
                !parent.IsInTransaction || parent.InitiatingUserId == Guid.Empty ||
                parent.UserId != parent.InitiatingUserId ||
                parent.CorrelationId == Guid.Empty ||
                recordId == Guid.Empty || parent.SharedVariables == null ||
                !Allowed(parent.MessageName, table, message))
                throw new InvalidPluginExecutionException(
                    "F5S001: Unverified synchronous scoped write origin.");

            // Marker exists ONLY in plugin-server SharedVariables, with a key
            // distinct from the client-exposed 'tag'. The random nonce is
            // transaction-local and each request must mint exact record scope.
            var nonce = Guid.NewGuid();
            parent.SharedVariables[TicketKey] = string.Join("|",
                "v1", nonce.ToString("D"), parent.OperationId.ToString("D"),
                parent.CorrelationId.ToString("D"), parent.InitiatingUserId.ToString("D"),
                parent.MessageName, table, message, recordId.ToString("D"));
        }

        public static void Require(IPluginExecutionContext child)
        {
            var parent = child?.ParentContext;
            if (child == null || parent == null || child.Mode != 0 ||
                child.Stage != 10 || !child.IsInTransaction ||
                parent.Mode != 0 || parent.Stage != 30 || !parent.IsInTransaction ||
                child.Depth != parent.Depth + 1 ||
                child.OperationId != parent.OperationId ||
                parent.CorrelationId == Guid.Empty ||
                child.CorrelationId != parent.CorrelationId ||
                parent.InitiatingUserId == Guid.Empty ||
                child.InitiatingUserId != parent.InitiatingUserId ||
                parent.UserId != parent.InitiatingUserId ||
                child.UserId != parent.InitiatingUserId ||
                parent.SharedVariables == null ||
                !parent.SharedVariables.ContainsKey(TicketKey))
                throw new InvalidPluginExecutionException(
                    "F5S002: Missing verified nested transaction context.");

            // Only targeted CRUD with a preassigned GUID can be scoped.
            // An ordinary client Create often has an empty Entity.Id.
            var target = child.InputParameters?.ContainsKey("Target") == true
                ? child.InputParameters["Target"] : null;
            string table;
            Guid recordId;
            if (target is Entity entity)
            {
                table = entity.LogicalName;
                recordId = entity.Id;
            }
            else if (target is EntityReference reference)
            {
                table = reference.LogicalName;
                recordId = reference.Id;
            }
            else
                throw new InvalidPluginExecutionException("F5S003: Unscoped target.");

            var encoded = parent.SharedVariables[TicketKey] as string;
            var pieces = encoded?.Split('|');
            if (pieces == null || pieces.Length != 9 ||
                pieces[0] != "v1" ||
                !Guid.TryParse(pieces[1], out var nonce) || nonce == Guid.Empty ||
                !Guid.TryParse(pieces[2], out var operation) ||
                !Guid.TryParse(pieces[3], out var correlation) ||
                !Guid.TryParse(pieces[4], out var principal) ||
                !Guid.TryParse(pieces[8], out var authorizedRecord) ||
                authorizedRecord == Guid.Empty ||
                operation != child.OperationId ||
                correlation != child.CorrelationId ||
                principal != child.InitiatingUserId ||
                pieces[5] != parent.MessageName ||
                pieces[6] != table || pieces[7] != child.MessageName ||
                table != child.PrimaryEntityName || recordId != authorizedRecord ||
                !Allowed(parent.MessageName, table, child.MessageName))
                throw new InvalidPluginExecutionException(
                    "F5S004: Scoped server-only write authorization mismatch.");
        }

        private static bool Allowed(string api, string table, string verb)
        {
            if (verb == "Delete") return false;
            switch (api)
            {
                // Isolated DEV-only transaction probe (NEVER a financial table).
                case "fmg_CashNestedTrustProbe":
                    return table == "fmg_cashtrustprobe" && verb == "Create";
                case "fmg_SubmitCashGrantRequest":
                    return table == "fmg_cashgrantrequest" && verb == "Create";
                case "fmg_DecideCashGrantRequest":
                    return (table == "fmg_cashgrantrequest" && verb == "Update") ||
                           (table == "fmg_cashaccessgrant" &&
                            (verb == "Create" || verb == "Update"));
                case "fmg_SettleCashReceipt":
                    return (table == "fmg_cashreceipt" &&
                            (verb == "Create" || verb == "Update")) ||
                           (table == "fmg_cashposting" && verb == "Create");
                case "fmg_PostCashTransfer":
                    return (table == "fmg_cashtransfer" &&
                            (verb == "Create" || verb == "Update")) ||
                           (table == "fmg_cashposting" && verb == "Create");
                default:
                    return false;
            }
        }
    }
}
