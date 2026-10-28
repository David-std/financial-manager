using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using FinancialManager.Validation;

namespace FinancialManager.PaymentExecution
{
    public sealed class PaymentCommandEndpoint : IPlugin
    {
        private const string Table = "fmg_financialtransaction";
        public void Execute(IServiceProvider provider)
        {
            if (provider == null) Fail("FPC001", "Missing provider.");
            var ctx = provider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext;
            var factory = provider.GetService(typeof(IOrganizationServiceFactory)) as IOrganizationServiceFactory;
            if (ctx == null || factory == null || ctx.Stage != 30 || ctx.Mode != 0 ||
                !ctx.IsInTransaction || ctx.UserId == Guid.Empty || ctx.InitiatingUserId != ctx.UserId ||
                (ctx.MessageName != "fmg_PostFinancialTransaction" && ctx.MessageName != "fmg_VoidFinancialTransaction"))
                Fail("FPC001", "Authenticated synchronous transactional Custom API required.");
            Guid id = ctx.InputParameters.Contains("RecordId") && ctx.InputParameters["RecordId"] is Guid
                ? (Guid)ctx.InputParameters["RecordId"] : Guid.Empty;
            if (id == Guid.Empty)
                Fail("FPC002", "An exact financial record ID is required.");
            string version = ctx.InputParameters.Contains("ExpectedRowVersion")
                ? ctx.InputParameters["ExpectedRowVersion"] as string : null;
            if (!PaymentCommandRules.ValidVersion(version))
                Fail("FPC002", "An exact row version is required, not an ETag.");
            var voiding = ctx.MessageName == "fmg_VoidFinancialTransaction";
            var reason = voiding && ctx.InputParameters.Contains("Reason")
                ? ctx.InputParameters["Reason"] as string : null;
            if (voiding && !PaymentCommandRules.ValidReason(reason))
                Fail("FPC003", "A meaningful void reason is required.");
            if (!voiding && ctx.InputParameters.Contains("Reason"))
                Fail("FPC003", "Post cannot supply a void reason.");

            // CreateOrganizationService(actor) never bypasses the delegated caller.
            var actor = ctx.InitiatingUserId;
            var service = factory.CreateOrganizationService(actor);
            var facts = new Facts(service);
            if (!facts.IsSystemAdministrator(actor))
                Fail("FPC007", "Only the currently approved administrator role can execute payment transitions.");
            var record = service.Retrieve(Table, id,
                new ColumnSet("fmg_status", "fmg_transactiondate", "fmg_amount",
                    "fmg_flowclass", "fmg_scope", "fmg_branch", "fmg_category",
                    "fmg_cashaccount", "transactioncurrencyid", "fmg_voidreason"));
            if (record == null || record.Id != id ||
                String.IsNullOrWhiteSpace(record.RowVersion) ||
                !String.Equals(record.RowVersion, version, StringComparison.Ordinal))
                Fail("FPC004", "Payment version obsolete or unavailable; reload.");
            var oldStatus = record.GetAttributeValue<OptionSetValue>("fmg_status")?.Value;
            if (oldStatus != (voiding ? FmgOptions.Posted : FmgOptions.Draft))
                Fail("FPC005", "Payment is not in the required state.");
            var change = new Entity(Table) { Id = id, RowVersion = record.RowVersion };
            change["fmg_status"] = new OptionSetValue(voiding ? FmgOptions.Voided : FmgOptions.Posted);
            if (voiding) change["fmg_voidreason"] = reason.Trim();
            // Repeat FTV validation inside the API; the existing FTV007 step
            // also runs on the nested Update. No guard bypass, no silent repair.
            new PaymentIntegrity(facts).Validate("Update", change, record, actor);
            service.Execute(new UpdateRequest
            {
                Target = change,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            });
            ctx.OutputParameters["Completed"] = true;
        }
        private static void Fail(string code, string message)
        {
            throw new InvalidPluginExecutionException(code + ": " + message);
        }
        private sealed class Facts : IFinancialFacts
        {
            private readonly IOrganizationService service;
            private static readonly Guid AdminTemplate = new Guid("627090FF-40A3-4053-8790-584EDC5BE201");
            public Facts(IOrganizationService service) { this.service = service; }
            public Entity Get(string table, Guid id, params string[] columns) =>
                service.Retrieve(table, id, new ColumnSet(columns));
            public bool IsSystemAdministrator(Guid actor)
            {
                if (actor == Guid.Empty) return false;
                var q = new QueryExpression("role") { ColumnSet = new ColumnSet(false), TopCount = 1 };
                q.Criteria.AddCondition("roletemplateid", ConditionOperator.Equal, AdminTemplate);
                q.AddLink("systemuserroles", "roleid", "roleid")
                    .LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, actor);
                if (service.RetrieveMultiple(q).Entities.Count > 0) return true;
                var teams = new QueryExpression("role") { ColumnSet = new ColumnSet(false), TopCount = 1 };
                teams.Criteria.AddCondition("roletemplateid", ConditionOperator.Equal, AdminTemplate);
                teams.AddLink("teamroles", "roleid", "roleid")
                    .AddLink("teammembership", "teamid", "teamid")
                    .LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, actor);
                return service.RetrieveMultiple(teams).Entities.Count != 0;
            }
        }
    }
    public static class PaymentCommandRules
    {
        public static bool ValidVersion(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 40) return false;
            foreach (var ch in value) if (ch < '0' || ch > '9') return false;
            return true;
        }
        public static bool ValidReason(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            var trimmed = value.Trim();
            return trimmed.Length >= 10 && trimmed.Length <= 500;
        }
    }
}
