using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.MailDraftExecution
{
#pragma warning disable CS0649
    [DataContract]
    internal sealed class ExpenseRequest
    {
        [DataMember(Name="requestId", IsRequired=true)] public string RequestId;
        [DataMember(Name="name", IsRequired=true)] public string Name;
        [DataMember(Name="amountPen", IsRequired=true)] public string Amount;
        [DataMember(Name="date", IsRequired=true)] public string Date;
        [DataMember(Name="categoryCode", IsRequired=true)] public string Category;
        [DataMember(Name="branchCode", IsRequired=true)] public string Branch;
        [DataMember(Name="accountName", IsRequired=true)] public string Account;
        [DataMember(Name="flowClass", IsRequired=true)] public string FlowClass;
        [DataMember(Name="description", IsRequired=false)] public string Description;
    }

    /// <summary>Private, authenticated review only; never invoked from the inbound mail flow.</summary>
 #pragma warning restore CS0649
    public sealed class MailDraftEndpoint : IPlugin
    {
        private const string Api = "fmg_CreateDraftFromMailIntake";
        private const string Intake = "fmg_mailintake";
        private const string Financial = "fmg_financialtransaction";
        private const int Draft = 797280000;
        private const string ReviewerRole = "Financial Manager Intake Reviewer";
        private static readonly Guid AdminRoleTemplate = new Guid("627090FF-40A3-4053-8790-584EDC5BE201");

        private static void Deny(string code, string message) =>
            throw new InvalidPluginExecutionException(code + ": " + message);

        public void Execute(IServiceProvider provider)
        {
            var ctx = provider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext;
            var factory = provider.GetService(typeof(IOrganizationServiceFactory)) as IOrganizationServiceFactory;
            if (ctx == null || factory == null ||
                ctx.MessageName != Api || ctx.Stage != 30 || ctx.Mode != 0 ||
                !ctx.IsInTransaction || ctx.UserId == Guid.Empty ||
                ctx.InitiatingUserId != ctx.UserId)
                Deny("FM11001", "Authenticated synchronous transactional Custom API required.");

            if (!ctx.InputParameters.Contains("IntakeId") || !(ctx.InputParameters["IntakeId"] is Guid) ||
                (Guid)ctx.InputParameters["IntakeId"] == Guid.Empty)
                Deny("FM11002", "A valid intake record is required.");
            if (!ctx.InputParameters.Contains("ExpectedRowVersion") ||
                !(ctx.InputParameters["ExpectedRowVersion"] is string))
                Deny("FM11002", "Intake row version required.");
            var expected = (string)ctx.InputParameters["ExpectedRowVersion"];
            if (String.IsNullOrWhiteSpace(expected) || expected.Length > 40 ||
                !Regex.IsMatch(expected, @"^[0-9]+$"))
                Deny("FM11002", "Invalid row version.");

            var svc = factory.CreateOrganizationService(ctx.InitiatingUserId);
            if (!HasReviewAuthority(svc, ctx.InitiatingUserId))
                Deny("FM11003", "The authenticated actor lacks review authority.");

            var intakeId = (Guid)ctx.InputParameters["IntakeId"];
            var source = svc.Retrieve(Intake, intakeId,
                new ColumnSet("fmg_requestid", "fmg_reviewstate", "fmg_validationstate",
                              "fmg_rawbody", "fmg_concept", "fmg_amountpen", "fmg_requesteddate", "fmg_categorycode",
                              "fmg_branchcode", "fmg_accountname", "fmg_flowclass"));
            if (source == null || source.Id != intakeId ||
                String.IsNullOrWhiteSpace(source.RowVersion) || source.RowVersion != expected ||
                source.GetAttributeValue<string>("fmg_reviewstate") != "Unverified" ||
                source.GetAttributeValue<string>("fmg_validationstate") != "FormatChecked")
                Deny("FM11004", "Intake state or row version changed; reload.");

            var requestId = source.GetAttributeValue<string>("fmg_requestid");
            var raw = source.GetAttributeValue<string>("fmg_rawbody");
            if (String.IsNullOrWhiteSpace(raw) || raw.Length > 15000)
                Deny("FM11004", "Original request is unavailable.");
            ExpenseRequest parsed;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw)))
                    parsed = (ExpenseRequest)new DataContractJsonSerializer(typeof(ExpenseRequest)).ReadObject(stream);
            }
            catch (Exception ex) when (ex is SerializationException || ex is InvalidCastException)
            {
                Deny("FM11005", "Invalid request JSON.");
                return;
            }
            if (parsed == null || !String.Equals(parsed.RequestId, requestId, StringComparison.Ordinal) ||
                String.IsNullOrWhiteSpace(requestId) ||
                !Regex.IsMatch(requestId, @"^[A-Za-z0-9_-]{12,80}$") ||
                String.IsNullOrWhiteSpace(parsed.Name) ||
                parsed.Name.Trim().Length < 4 || parsed.Name.Trim().Length > 100 ||
                parsed.Description != null && parsed.Description.Length > 1000 ||
                !Regex.IsMatch(parsed.Category ?? "", @"^[0-9]{3}$") ||
                !new [] { "LM", "SR" }.Contains(parsed.Branch) ||
                !new [] { "operativo", "capital", "impuesto", "otro" }.Contains(parsed.FlowClass) ||
                !String.Equals(parsed.Name.Trim(), source.GetAttributeValue<string>("fmg_concept"), StringComparison.Ordinal) ||
                parsed.Amount != source.GetAttributeValue<string>("fmg_amountpen") ||
                !source.Contains("fmg_requesteddate") ||
                parsed.Date != source.GetAttributeValue<string>("fmg_requesteddate") ||
                parsed.Category != source.GetAttributeValue<string>("fmg_categorycode") ||
                parsed.Branch != source.GetAttributeValue<string>("fmg_branchcode") ||
                parsed.Account != source.GetAttributeValue<string>("fmg_accountname") ||
                parsed.FlowClass != source.GetAttributeValue<string>("fmg_flowclass"))
                Deny("FM11005", "Request fields fail validation or disagree with staged values.");

            decimal amount = 0;
            DateTime date = DateTime.MinValue;
            if (!Regex.IsMatch(parsed.Amount ?? "", @"^(?:0|[1-9][0-9]*)(?:\.[0-9]{1,2})?$") ||
                !Decimal.TryParse(parsed.Amount, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out amount) ||
                amount <= 0 || amount > 999999999m ||
                !DateTime.TryParseExact(parsed.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date) || date.Year < 2000 || date.Year > 2100)
                Deny("FM11005", "Amount or calendar date invalid.");

            // Unique index is checked live, not inferred from a preliminary existence query.
            var md = ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest {
                LogicalName = Financial, EntityFilters = EntityFilters.All,
                RetrieveAsIfPublished = true
            })).EntityMetadata;
            if (md.Keys == null || !md.Keys.Any(k =>
                k.EntityKeyIndexStatus == EntityKeyIndexStatus.Active &&
                k.KeyAttributes != null && k.KeyAttributes.Length == 1 &&
                k.KeyAttributes[0] == "fmg_externalrequestid"))
                Deny("FM11006", "Financial request idempotency index is not active.");

            var branch = One(svc, "fmg_branch", "fmg_code", parsed.Branch,
                             "fmg_isactive");
            if (branch.GetAttributeValue<bool?>("fmg_isactive") != true)
                Deny("FM11007", "Inactive branch.");
            var category = One(svc, "fmg_category", "fmg_code", parsed.Category,
                               "fmg_isactive", "fmg_isgroup", "fmg_parentcategory");
            if (category.GetAttributeValue<bool?>("fmg_isactive") != true ||
                category.GetAttributeValue<bool?>("fmg_isgroup") != false ||
                category.GetAttributeValue<EntityReference>("fmg_parentcategory") == null ||
                parsed.Category == "999")
                Deny("FM11007", "Invalid expense category.");
            var account = One(svc, "fmg_cashaccount", "fmg_name", parsed.Account,
                              "fmg_isactive", "fmg_branch");
            if (account.GetAttributeValue<bool?>("fmg_isactive") != true ||
                account.GetAttributeValue<EntityReference>("fmg_branch")?.Id != branch.Id)
                Deny("FM11007", "Cash account is not active in the requested branch.");
            var currency = One(svc, "transactioncurrency", "isocurrencycode", "PEN",
                               "isocurrencycode");

            var flow = parsed.FlowClass == "operativo" ? 797280000 :
                       parsed.FlowClass == "capital" ? 797280001 :
                       parsed.FlowClass == "impuesto" ? 797280002 : 797280003;
            var draft = new Entity(Financial) {
                ["fmg_status"] = new OptionSetValue(Draft),
                ["fmg_transactiondate"] = date,
                ["fmg_amount"] = new Money(amount),
                ["fmg_flowclass"] = new OptionSetValue(flow),
                ["fmg_scope"] = new OptionSetValue(797280000),
                ["fmg_branch"] = new EntityReference("fmg_branch", branch.Id),
                ["fmg_category"] = new EntityReference("fmg_category", category.Id),
                ["fmg_cashaccount"] = new EntityReference("fmg_cashaccount", account.Id),
                ["transactioncurrencyid"] = new EntityReference("transactioncurrency", currency.Id),
                ["fmg_description"] = parsed.Description ?? "",
                ["fmg_externalrequestid"] = requestId
            };
            var created = svc.Create(draft); // Same Dataverse transaction as the state change below.
            if (created == Guid.Empty) Deny("FM11008", "Draft creation returned an empty identifier.");
            var update = new Entity(Intake, intakeId) { RowVersion = source.RowVersion };
            update["fmg_reviewstate"] = "DraftCreated";
            update["fmg_validationstate"] = "Reviewed";
            svc.Execute(new UpdateRequest {
                Target = update,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            });
            ctx.OutputParameters["DraftId"] = created;
            ctx.OutputParameters["Created"] = true;
        }

        private static Entity One(IOrganizationService svc, string table, string key,
                                  string value, params string[] columns)
        {
            var query = new QueryExpression(table) {
                ColumnSet = new ColumnSet(columns), TopCount = 2
            };
            query.Criteria.AddCondition(key, ConditionOperator.Equal, value);
            var matches = svc.RetrieveMultiple(query).Entities;
            if (matches.Count != 1)
                Deny("FM11007", "Reference missing or ambiguous: " + table);
            return matches[0];
        }

        private static bool HasReviewAuthority(IOrganizationService svc, Guid actor)
        {
            var q = new QueryExpression("role") {
                ColumnSet = new ColumnSet(false), TopCount = 1
            };
            var condition = new FilterExpression(LogicalOperator.Or);
            condition.AddCondition("name", ConditionOperator.Equal, ReviewerRole);
            condition.AddCondition("roletemplateid", ConditionOperator.Equal, AdminRoleTemplate);
            q.Criteria.AddFilter(condition);
            q.AddLink("systemuserroles", "roleid", "roleid").LinkCriteria
                .AddCondition("systemuserid", ConditionOperator.Equal, actor);
            if (svc.RetrieveMultiple(q).Entities.Count != 0) return true;

            var teams = new QueryExpression("role") {
                ColumnSet = new ColumnSet(false), TopCount = 1
            };
            teams.Criteria.AddFilter(condition);
            var roleTeams = teams.AddLink("teamroles", "roleid", "roleid");
            roleTeams.AddLink("teammembership", "teamid", "teamid")
                .LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, actor);
            return svc.RetrieveMultiple(teams).Entities.Count != 0;
        }
    }
}
