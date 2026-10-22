using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.Validation
{
    /// <summary>Validated constants from the 2026-10-22 Dataverse unmanaged solution export.</summary>
    public static class FmgOptions
    {
        public const int Draft = 797280000;
        public const int Posted = 797280001;
        public const int Voided = 797280002;
        public const int Branch = 797280000;
        public const int Shared = 797280001;
        public const int OperatingPayment = 797280000;
        public const int CapitalPayment = 797280001;
        public const int TaxPayment = 797280002;
        public const int OtherPayment = 797280003;
    }

    public interface IFinancialFacts
    {
        Entity Get(string logicalName, Guid id, params string[] columns);
        bool IsSystemAdministrator(Guid initiatingUserId);
    }

    /// <summary>Pure workflow and cross-record checks. Never writes data or silently fixes invalid inputs.</summary>
    public sealed class PaymentIntegrity
    {
        private const string Table = "fmg_financialtransaction";
        private readonly IFinancialFacts facts;

        public PaymentIntegrity(IFinancialFacts facts)
        {
            this.facts = facts ?? throw new ArgumentNullException(nameof(facts));
        }

        public void Validate(string message, Entity target, Entity before, Guid actor)
        {
            if (target == null || !String.Equals(target.LogicalName, Table, StringComparison.OrdinalIgnoreCase))
                Fail("FTV000", "Unexpected Financial Transaction target.");

            switch (message)
            {
                case "Create":
                    if (before != null) Fail("FTV000", "Create must not have a before image.");
                    ValidateCreate(target);
                    return;
                case "Update":
                    if (before == null) Fail("FTV000", "Missing required Before pre-image on Update.");
                    ValidateUpdate(target, before, actor);
                    return;
                case "Delete":
                    if (before == null) Fail("FTV000", "Missing required Before pre-image on Delete.");
                    ValidateDelete(before);
                    return;
                default:
                    Fail("FTV000", "Unsupported Dataverse message.");
                    return;
            }
        }

        private void ValidateCreate(Entity target)
        {
            var status = Option(target, "fmg_status") ?? FmgOptions.Draft;
            if (status != FmgOptions.Draft)
                Fail("FTV005", "Payments can only be created as Draft; posting must be an authorized update.");
        }

        private void ValidateUpdate(Entity target, Entity before, Guid actor)
        {
            var oldStatus = Option(before, "fmg_status");
            var combined = Merge(before, target);
            var newStatus = Option(combined, "fmg_status");
            if (oldStatus == FmgOptions.Draft)
            {
                if (newStatus == FmgOptions.Draft) return;
                if (newStatus != FmgOptions.Posted)
                    Fail("FTV005", "Only Draft to Posted is a valid Draft transition.");
                RequireAdmin(actor, "FTV007");
                ValidatePosting(combined);
                return;
            }

            if (oldStatus == FmgOptions.Posted)
            {
                if (newStatus != FmgOptions.Voided || !target.Contains("fmg_status"))
                    Fail("FTV005", "Posted is immutable except for an authorized transition to Voided.");
                foreach (var key in target.Attributes.Keys)
                {
                    if (key != "fmg_status" && key != "fmg_voidreason" &&
                        key != "fmg_financialtransactionid")
                        Fail("FTV006", "Cannot change financial fields of an already Posted payment.");
                }
                RequireAdmin(actor, "FTV007");
                if (String.IsNullOrWhiteSpace(Text(combined, "fmg_voidreason")))
                    Fail("FTV007", "Voiding requires a reason.");
                return;
            }

            if (oldStatus == FmgOptions.Voided)
                Fail("FTV005", "Voided transactions are immutable.");
            Fail("FTV005", "The original transaction status is invalid.");
        }

        private static void ValidateDelete(Entity before)
        {
            if (Option(before, "fmg_status") != FmgOptions.Draft)
                Fail("FTV008", "Posted or Voided transactions cannot be deleted.");
        }

        private void ValidatePosting(Entity item)
        {
            DateTime transactionDate;
            if (!item.Contains("fmg_transactiondate") ||
                !(item["fmg_transactiondate"] is DateTime) ||
                (transactionDate = (DateTime)item["fmg_transactiondate"]) == DateTime.MinValue)
                Fail("FTV001", "Transaction Date is required to post.");

            Money amount = item.GetAttributeValue<Money>("fmg_amount");
            if (amount == null || amount.Value <= 0 || Decimal.Round(amount.Value, 2) != amount.Value)
                Fail("FTV001", "Amount must be positive with at most two decimal places.");

            var flow = Option(item, "fmg_flowclass");
            if (flow != FmgOptions.OperatingPayment && flow != FmgOptions.CapitalPayment &&
                flow != FmgOptions.TaxPayment && flow != FmgOptions.OtherPayment)
                Fail("FTV001", "A valid expense Flow Class is required.");

            var currency = Ref(item, "transactioncurrencyid");
            if (currency == null) Fail("FTV001", "Transaction currency is required.");
            var currencyRow = Read("transactioncurrency", currency, "isocurrencycode");
            if (!String.Equals(Text(currencyRow, "isocurrencycode"), "PEN", StringComparison.OrdinalIgnoreCase))
                Fail("FTV001", "Only PEN transactions are supported.");

            var category = Ref(item, "fmg_category");
            if (category == null) Fail("FTV002", "Category is required.");
            var cat = Read("fmg_category", category, "fmg_isactive", "fmg_isgroup", "fmg_code", "fmg_parentcategory");
            if (cat.GetAttributeValue<bool?>("fmg_isactive") != true ||
                cat.GetAttributeValue<bool?>("fmg_isgroup") != false ||
                String.Equals(Text(cat, "fmg_code"), "999", StringComparison.OrdinalIgnoreCase) ||
                Ref(cat, "fmg_parentcategory") == null)
                Fail("FTV002", "The category must be an active expense leaf other than 999.");

            var scope = Option(item, "fmg_scope");
            var branch = Ref(item, "fmg_branch");
            if (scope == FmgOptions.Branch)
            {
                if (branch == null) Fail("FTV003", "Branch is required for Branch scope.");
                var branchRow = Read("fmg_branch", branch, "fmg_isactive");
                if (branchRow.GetAttributeValue<bool?>("fmg_isactive") != true)
                    Fail("FTV003", "Branch is inactive.");
            }
            else if (scope == FmgOptions.Shared)
            {
                if (branch != null) Fail("FTV003", "Shared payments cannot reference a Branch.");
            }
            else Fail("FTV003", "Scope must be Branch or Shared.");

            var account = Ref(item, "fmg_cashaccount");
            if (account == null) Fail("FTV004", "Cash Account is required.");
            var accountRow = Read("fmg_cashaccount", account, "fmg_isactive", "fmg_branch");
            if (accountRow.GetAttributeValue<bool?>("fmg_isactive") != true)
                Fail("FTV004", "Cash Account is inactive.");
            var assigned = Ref(accountRow, "fmg_branch");
            if (scope == FmgOptions.Shared && assigned != null)
                Fail("FTV004", "Shared payments require a shared Cash Account.");
            if (scope == FmgOptions.Branch && assigned != null && assigned.Id != branch.Id)
                Fail("FTV004", "Cash Account belongs to a different Branch.");
        }

        private Entity Read(string table, EntityReference reference, params string[] columns)
        {
            if (!String.Equals(table, reference.LogicalName, StringComparison.OrdinalIgnoreCase) ||
                reference.Id == Guid.Empty)
                Fail("FTV000", "Invalid related-record reference.");
            Entity value = facts.Get(table, reference.Id, columns);
            if (value == null) Fail("FTV000", "Referenced record does not exist or is inaccessible.");
            return value;
        }

        private void RequireAdmin(Guid actor, string code)
        {
            // Until branch teams and finance roles exist, privileged actions are fail-closed.
            if (actor == Guid.Empty || !facts.IsSystemAdministrator(actor))
                Fail(code, "Posting and voiding are restricted to System Administrators until finance roles are configured.");
        }

        private static Entity Merge(Entity before, Entity target)
        {
            Entity result = new Entity(Table);
            foreach (var pair in before.Attributes) result[pair.Key] = pair.Value;
            foreach (var pair in target.Attributes) result[pair.Key] = pair.Value;
            return result;
        }

        private static int? Option(Entity e, string name)
        {
            var val = e.GetAttributeValue<OptionSetValue>(name);
            return val == null ? (int?)null : val.Value;
        }

        private static EntityReference Ref(Entity e, string name)
        {
            return e.GetAttributeValue<EntityReference>(name);
        }

        private static string Text(Entity e, string name)
        {
            return e.GetAttributeValue<string>(name);
        }

        private static void Fail(string code, string message)
        {
            throw new InvalidPluginExecutionException(code + ": " + message);
        }
    }
}
