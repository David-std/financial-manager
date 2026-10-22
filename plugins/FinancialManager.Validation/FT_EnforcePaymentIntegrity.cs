using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.Validation
{
    /// <summary>Register ONLY against fmg_financialtransaction Create/Update/Delete in synchronous PreValidation.</summary>
    public sealed class FT_EnforcePaymentIntegrity : PluginBase
    {
        public FT_EnforcePaymentIntegrity(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(FT_EnforcePaymentIntegrity)) { }

        protected override void ExecuteDataversePlugin(ILocalPluginContext local)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));
            var context = local.PluginExecutionContext;
            if (context.Stage != 10 || context.Mode != 0)
                throw new InvalidPluginExecutionException("FTV000: This plug-in must run synchronously in PreValidation.");
            if (!String.Equals(context.PrimaryEntityName, "fmg_financialtransaction", StringComparison.OrdinalIgnoreCase))
                throw new InvalidPluginExecutionException("FTV000: Wrong registered table.");
            if (!context.InputParameters.Contains("Target"))
                throw new InvalidPluginExecutionException("FTV000: Missing Target.");

            Entity target = context.InputParameters["Target"] as Entity;
            if (context.MessageName == "Delete")
            {
                var reference = context.InputParameters["Target"] as EntityReference;
                if (reference == null) throw new InvalidPluginExecutionException("FTV000: Delete requires EntityReference.");
                target = new Entity(reference.LogicalName) { Id = reference.Id };
            }
            if (target == null) throw new InvalidPluginExecutionException("FTV000: Invalid Target.");

            Entity before = null;
            if (context.MessageName != "Create" && context.PreEntityImages.Contains("Before"))
                before = context.PreEntityImages["Before"];

            // Never silently bypass missing images or unauthorized updates.
            new PaymentIntegrity(new DataverseFacts(local.PluginUserService))
                .Validate(context.MessageName, target, before, context.InitiatingUserId);
        }

        private sealed class DataverseFacts : IFinancialFacts
        {
            private static readonly Guid SysAdminTemplate =
                new Guid("627090FF-40A3-4053-8790-584EDC5BE201");
            private readonly IOrganizationService service;

            public DataverseFacts(IOrganizationService service)
            {
                this.service = service;
            }

            public Entity Get(string logicalName, Guid id, params string[] columns)
            {
                return service.Retrieve(logicalName, id, new ColumnSet(columns));
            }

            public bool IsSystemAdministrator(Guid userId)
            {
                var direct = new QueryExpression("role")
                {
                    ColumnSet = new ColumnSet("roleid"),
                    TopCount = 1
                };
                direct.Criteria.AddCondition("roletemplateid", ConditionOperator.Equal, SysAdminTemplate);
                var membership = direct.AddLink("systemuserroles", "roleid", "roleid");
                membership.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
                if (service.RetrieveMultiple(direct).Entities.Count > 0) return true;

                // Also honor administrator roles assigned via Dataverse owner teams.
                var teamQuery = new QueryExpression("role")
                {
                    ColumnSet = new ColumnSet("roleid"),
                    TopCount = 1
                };
                teamQuery.Criteria.AddCondition("roletemplateid", ConditionOperator.Equal, SysAdminTemplate);
                var teamRole = teamQuery.AddLink("teamroles", "roleid", "roleid");
                var teamUser = teamRole.AddLink("teammembership", "teamid", "teamid");
                teamUser.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
                return service.RetrieveMultiple(teamQuery).Entities.Count > 0;
            }
        }
    }
}
