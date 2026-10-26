using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.CashOperations
{
    // Explicitly read-only. No database access, financial mutation or privilege delegation.
    public sealed class F5_RuntimeProbe : IPlugin
    {
        public F5_RuntimeProbe(string unsecureConfiguration, string secureConfiguration) { }

        public void Execute(IServiceProvider services)
        {
            if (services == null)
                throw new InvalidPluginExecutionException("F5D000: Missing services.");
            var context = services.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext;
            if (context == null)
                throw new InvalidPluginExecutionException("F5D000: Missing context.");
            var report = F5_RuntimeProbePolicy.Inspect(context.MessageName, context.Stage,
                context.Mode, context.IsInTransaction, context.UserId, context.InitiatingUserId);
            context.OutputParameters["InTransaction"] = report.InTransaction;
            context.OutputParameters["ObservedStage"] = report.Stage;
            context.OutputParameters["SameIdentity"] = report.SameIdentity;
            context.OutputParameters["CashWritesEnabled"] = false;
        }
    }

    public sealed class F5_RuntimeReport
    {
        public bool InTransaction { get; internal set; }
        public int Stage { get; internal set; }
        public bool SameIdentity { get; internal set; }
    }

    public static class F5_RuntimeProbePolicy
    {
        public static F5_RuntimeReport Inspect(string message, int stage, int mode,
            bool inTransaction, Guid executionUser, Guid initiatingUser)
        {
            if (!String.Equals(message, "fmg_CashTransactionReadiness", StringComparison.Ordinal) ||
                stage != 30 || mode != 0)
                throw new InvalidPluginExecutionException("F5D001: Unexpected API execution pipeline.");
            if (executionUser == Guid.Empty || initiatingUser == Guid.Empty)
                throw new InvalidPluginExecutionException("F5D002: Missing authenticated identity.");
            return new F5_RuntimeReport
            {
                InTransaction = inTransaction,
                Stage = stage,
                SameIdentity = executionUser == initiatingUser
            };
        }
    }
}
