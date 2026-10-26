using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.CashAccessIntegrity
{
    /// <summary>Never grant financial permissions through table CRUD.
    /// Future approved dual-control entitlement APIs must replace this
    /// deny-all policy without opening a direct-write gap.</summary>
    public sealed class F5_BlockCashAccessWrites : IPlugin
    {
        public F5_BlockCashAccessWrites(string unsecureConfiguration, string secureConfiguration) { }
        public void Execute(IServiceProvider provider)
        {
            if (provider == null)
                throw new InvalidPluginExecutionException("F5A000: Missing runtime.");
            var context = provider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext;
            if (context == null)
                throw new InvalidPluginExecutionException("F5A000: Missing execution context.");
            CashAccessWritePolicy.Validate(context.PrimaryEntityName,
                context.MessageName, context.Stage, context.Mode);
        }
    }

    public static class CashAccessWritePolicy
    {
        public static void Validate(string entity, string message, int stage, int mode)
        {
            if (entity != "fmg_cashaccessgrant" || stage != 10 || mode != 0 ||
                (message != "Create" && message != "Update" && message != "Delete"))
                throw new InvalidPluginExecutionException("F5A000: Invalid grant guard registration.");
            throw new InvalidPluginExecutionException(
                "F5A009: Financial account grants locked until controlled approval service is accepted.");
        }
    }
}
