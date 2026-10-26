using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.CashIntegrity
{
    /// <summary>Temporary fail-closed cash-domain gate. Register on Create, Update,
    /// Delete of all six F5 tables in synchronous PreValidation. Neither admins nor
    /// clients can bypass it. A separate approved server API must replace this gate.</summary>
    public sealed class F5_BlockDirectCashWrites : IPlugin
    {
        public F5_BlockDirectCashWrites(string unsecureConfiguration, string secureConfiguration) { }

        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new InvalidPluginExecutionException("F5G000: Missing service provider.");
            var context = serviceProvider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext;
            if (context == null)
                throw new InvalidPluginExecutionException("F5G000: Missing execution context.");
            CashWritePolicy.Validate(context.PrimaryEntityName, context.MessageName, context.Stage, context.Mode);
        }
    }

    public static class CashWritePolicy
    {
        public static readonly string[] ProtectedTables = {
            "fmg_cashreceipt", "fmg_cashtransfer", "fmg_cashposting",
            "fmg_accountopening", "fmg_bankstatement", "fmg_bankreconciliation"
        };

        public static void Validate(string tableName, string message, int stage, int mode)
        {
            if (stage != 10 || mode != 0)
                throw new InvalidPluginExecutionException("F5G000: Cash guard requires synchronous PreValidation.");
            bool validTable = false;
            foreach (var name in ProtectedTables)
                if (String.Equals(name, tableName, StringComparison.OrdinalIgnoreCase)) validTable = true;
            if (!validTable || !(String.Equals(message, "Create", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(message, "Update", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidPluginExecutionException("F5G000: Cash guard registration is invalid.");
            throw new InvalidPluginExecutionException(
                "F5G001: Cash operations are locked until authorized server APIs are accepted.");
        }
    }
}
