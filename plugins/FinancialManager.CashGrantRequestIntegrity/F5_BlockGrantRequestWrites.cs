using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.CashGrantRequestIntegrity
{
    /// <summary>Separate fail-closed request boundary. Never approve a
    /// record from UI CRUD; a future reviewed MainOperation service must
    /// prove an authenticated transaction and immutable dual control.</summary>
    public sealed class F5_BlockGrantRequestWrites : IPlugin
    {
        public F5_BlockGrantRequestWrites(string unsecureConfiguration, string secureConfiguration) { }
        public void Execute(IServiceProvider provider)
        {
            var context = provider?.GetService(typeof(IPluginExecutionContext))
                as IPluginExecutionContext;
            if (context == null)
                throw new InvalidPluginExecutionException("F5Q000: Missing verified context.");
            GrantRequestWritePolicy.Validate(context.PrimaryEntityName,
                context.MessageName, context.Stage, context.Mode);
        }
    }

    public static class GrantRequestWritePolicy
    {
        public static void Validate(string entityName, string message, int stage, int mode)
        {
            if (entityName != "fmg_cashgrantrequest" || stage != 10 || mode != 0 ||
                (message != "Create" && message != "Update" && message != "Delete"))
                throw new InvalidPluginExecutionException("F5Q000: Unexpected request guard registration.");
            throw new InvalidPluginExecutionException(
                "F5Q009: Financial grant request mutations locked until dual-control API accepted.");
        }
    }
}
