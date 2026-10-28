using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;
using System.IO;

const string Solution = "FinancialManager";
const string AssemblyName = "fmg_FinancialManagerValidation";
const string PluginClass = "FinancialManager.Validation.FT_EnforcePaymentIntegrity";
const string Table = "fmg_financialtransaction";

string mode = args.Length > 0 ? args[0] : "inspect";
if (mode != "inspect" && mode != "register" && mode != "steps" && mode != "verify" && mode != "inspect-schema" && mode != "schema" && mode != "publish-schema" && mode != "verify-schema" && mode != "app-nav-inspect" && mode != "app-nav-fix" && mode != "budget-approval-inspect" && mode != "budget-backup" && mode != "reporting-inspect" && mode != "expense-readiness" && mode != "cash-keys-inspect" && mode != "cash-keys" && mode != "cash-transaction-probe" && mode != "cash-schema-publish" && mode != "cash-schema-verify" && mode != "cash-schema-inspect" && mode != "cash-schema" && mode != "cash-trust-pin" && mode != "cash-trust-mainstep-inspect" && mode != "cash-trust-finish" && mode != "cash-trust-setup" && mode != "cash-trust-inspect" && mode != "cash-trust-package" && mode != "cash-trust-package-id" && mode != "cash-trust-schema" && mode != "cash-trust-steps" && mode != "cash-trust-api" && mode != "cash-trust-probe" && mode != "cash-review-package-inspect" && mode != "cash-review-package-install" && mode != "cash-review-api-inspect" && mode != "cash-review-api-register" && mode != "cash-review-step-inspect" && mode != "cash-review-standby-inspect" && mode != "cash-review-standby-register" && mode != "cash-review-privileges-inspect" && mode != "cash-review-inspect" && mode != "cash-review-guard-inspect" && mode != "cash-review-guard-register" && mode != "cash-review-schema" && mode != "cash-review-guard-steps" && mode != "cash-review-guard-probe" && mode != "cash-grant-expiry" && mode != "cash-grant-expiry-inspect" && mode != "cash-grant-publish" && mode != "cash-grant-inspect" && mode != "cash-grant-schema" && mode != "cash-grant-guard-register" && mode != "cash-grant-guard-inspect" && mode != "cash-grant-guard-steps" && mode != "cash-grant-guard-probe" && mode != "cash-functional-prerequisites-inspect" && mode != "cash-financial-direct-update-probe" && mode != "cash-financial-direct-deny-probe" && mode != "cash-financial-negative-probe" && mode != "cash-financial-step-inspect" && mode != "cash-financial-package-new" && mode != "cash-financial-package-new-inspect" &&  mode != "cash-financial-api-register" && mode != "cash-financial-api-inspect" && mode != "cash-ledger-contract-inspect" && mode != "cash-operation-contract-inspect" && mode != "cash-operation-contract" && mode != "cash-runtime-inspect" && mode != "cash-runtime-register" && mode != "cash-runtime-api" && mode != "cash-runtime-probe" && mode != "cash-contract-inspect" && mode != "cash-guard-inspect" && mode != "cash-guard-register" && mode != "cash-guard-steps" && mode != "cash-guard-probe" && !mode.StartsWith("budget-", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage: cash-schema-inspect | cash-schema --apply | cash-schema-publish --apply | cash-schema-verify | cash-keys-inspect | cash-keys --apply | cash-transaction-probe --apply | inspect | app-nav-inspect | app-nav-fix --apply | inspect-schema | verify-schema | budget-inspect | budget-key-status | budget-key --apply | budget-register --apply | budget-steps --apply | schema --apply | publish-schema --apply | register --apply | steps --apply | verify --apply");
    return 2;
}
bool apply = args.Length > 1 && args[1] == "--apply";
if (mode != "inspect" && mode != "inspect-schema" && mode != "verify-schema" && mode != "budget-inspect" && mode != "budget-key-status" && mode != "budget-approval-inspect" && mode != "budget-backup" && mode != "reporting-inspect" && mode != "expense-readiness" && mode != "cash-keys-inspect" && mode != "cash-schema-verify" && mode != "cash-schema-inspect" && mode != "cash-trust-mainstep-inspect" && mode != "cash-trust-inspect" && mode != "cash-trust-package-id" && mode != "cash-review-package-inspect" && mode != "cash-review-api-inspect" && mode != "cash-review-step-inspect" && mode != "cash-review-standby-inspect" && mode != "cash-review-privileges-inspect" && mode != "cash-review-inspect" && mode != "cash-review-guard-inspect" && mode != "cash-grant-expiry-inspect" && mode != "cash-grant-inspect" && mode != "cash-grant-guard-inspect" && mode != "cash-functional-prerequisites-inspect" && mode != "cash-financial-step-inspect" && mode != "cash-financial-package-new-inspect" &&  mode != "cash-financial-api-inspect" && mode != "cash-ledger-contract-inspect" && mode != "cash-operation-contract-inspect" && mode != "cash-runtime-inspect" && mode != "cash-runtime-probe" && mode != "cash-contract-inspect" && mode != "cash-guard-inspect" && mode != "app-nav-inspect" && !apply)
{
    Console.Error.WriteLine("Mutation requires explicit --apply.");
    return 2;
}
var url = Environment.GetEnvironmentVariable("FM_ENV_URL");
var expectedId = Environment.GetEnvironmentVariable("FM_ORGANIZATION_ID");
if (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
    u.Scheme != Uri.UriSchemeHttps || !u.Host.EndsWith(".dynamics.com", StringComparison.OrdinalIgnoreCase) ||
    !Guid.TryParse(expectedId, out var organizationId))
{
    Console.Error.WriteLine("Set FM_ENV_URL and FM_ORGANIZATION_ID to the intended Dataverse dev organization.");
    return 2;
}
var conn = "AuthType=OAuth;Url=" + url +
    ";AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost;LoginPrompt=Never;SkipDiscovery=true";
using var client = new ServiceClient(conn);
if (!client.IsReady) { Console.Error.WriteLine("Authentication failed; no changes. " + client.LastError); return 3; }

var who = (WhoAmIResponse)client.Execute(new WhoAmIRequest());
if (who.OrganizationId != organizationId)
{
    Console.Error.WriteLine("SAFETY STOP: organization ID differs from expected environment.");
    return 4;
}
var sq = new QueryExpression("solution") { ColumnSet = new ColumnSet("uniquename", "version", "isvisible"), TopCount = 2 };
sq.Criteria.AddCondition("uniquename", ConditionOperator.Equal, Solution);
var found = client.RetrieveMultiple(sq).Entities;
if (found.Count != 1) { Console.Error.WriteLine("SAFETY STOP: FinancialManager solution not found exactly once."); return 4; }

EntityCollection List(string name, string attribute, string value, params string[] cols)
{
    var q = new QueryExpression(name) { ColumnSet = new ColumnSet(cols), TopCount = 10 };
    q.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
    return client.RetrieveMultiple(q);
}
var packages = List("pluginpackage", "name", AssemblyName, "pluginpackageid", "name");
var types = List("plugintype", "typename", PluginClass, "plugintypeid", "typename");
Console.WriteLine("Authenticated into expected FinancialManager organization.");
Console.WriteLine("Plugin packages found: " + packages.Entities.Count + "; plug-in classes found: " + types.Entities.Count);

if (mode == "inspect") return 0;
if (mode=="app-nav-inspect" || mode=="app-nav-fix") return AppNavigation.Run(client,mode=="app-nav-fix");
if (mode == "budget-approval-inspect") return BudgetApprovalReadiness.Inspect(client);
if (mode == "reporting-inspect") return ReportingReadiness.Run(client);
if (mode == "expense-readiness") return ExpenseReadiness.Inspect(client);
if (mode == "cash-contract-inspect") return CashContractReadiness.Inspect(client);
if (mode.StartsWith("cash-trust-", StringComparison.Ordinal))
    return CashTrustSandboxDeployment.Run(client, mode);
// Installing the independent review package registers no Custom API or CRUD step.
// F5Q009/F5A009 keep all mutations denied until a separately validated cutover.
if (mode == "cash-review-package-inspect" || mode == "cash-review-package-install")
    return CashGrantReviewApiDeployment.PackageRun(client, mode == "cash-review-package-install");
// API metadata is permitted only while the 24 original deny guards remain active.
// This registers no grant and never switches any CRUD step to allow.
if (mode == "cash-review-api-inspect" || mode == "cash-review-api-register")
    return CashGrantReviewApiDeployment.ApiRun(client, mode == "cash-review-api-register");
if (mode == "cash-review-standby-inspect" || mode == "cash-review-standby-register")
    return CashGrantReviewStandbyDeployment.Run(client, mode == "cash-review-standby-register");
if (mode == "cash-review-step-inspect") return CashGrantReviewStepReadiness.Inspect(client);
if (mode == "cash-review-privileges-inspect") return CashGrantPrivilegeReadiness.Inspect(client);
if (mode == "cash-review-inspect") return CashGrantRequestSchema.Run(client, false);
if (mode == "cash-review-guard-inspect")
    return CashGrantRequestGuardDeployment.Steps(client, false);
if (mode == "cash-review-guard-register")
    return CashGrantRequestGuardDeployment.Register(client);
if (mode == "cash-review-schema") return CashGrantRequestSchema.Run(client, true);
if (mode == "cash-review-guard-steps")
    return CashGrantRequestGuardDeployment.Steps(client, true);
if (mode == "cash-review-guard-probe")
    return CashGrantRequestGuardDeployment.Probe(client);
if (mode == "cash-grant-expiry" || mode == "cash-grant-expiry-inspect")
    return CashGrantExpiryMetadata.Run(client, mode == "cash-grant-expiry");
if (mode == "cash-grant-publish") return CashGrantKeyPublication.Run(client);
if (mode == "cash-grant-inspect") return CashGrantSchema.Run(client, false);
if (mode == "cash-grant-guard-inspect") return CashGrantGuardDeployment.Steps(client, false);
if (mode == "cash-grant-guard-register") return CashGrantGuardDeployment.Register(client);
if (mode == "cash-grant-schema")
{
    CashGuardDeployment.Run(client, false, true);
    // Require the validated guard plugin to be pre-registered before
    // creating a new sensitive authorization table.
    if (client.RetrieveMultiple(new QueryExpression("plugintype")
    {
        ColumnSet = new ColumnSet("plugintypeid"), Criteria = new FilterExpression
        {
            Conditions = { new ConditionExpression("typename", ConditionOperator.Equal,
                "FinancialManager.CashAccessIntegrity.F5_BlockCashAccessWrites") }
        }
    }).Entities.Count != 1)
        throw new InvalidOperationException("Grant guard type must be deployed first.");
    return CashGrantSchema.Run(client, true);
}
if (mode == "cash-grant-guard-steps")
    return CashGrantGuardDeployment.Steps(client, true);
if (mode == "cash-grant-guard-probe") return CashGrantGuardDeployment.Probe(client);
if (mode == "cash-functional-prerequisites-inspect") return CashFunctionalPrerequisites.Inspect(client);
if (mode == "cash-financial-direct-update-probe") return CashFinancialDirectCrudNegativeProbe.Run(client, false);
if (mode == "cash-financial-direct-deny-probe") return CashFinancialDirectCrudNegativeProbe.Run(client, true);
if (mode == "cash-financial-negative-probe") return CashFinancialApiNegativeProbe.Run(client);
if (mode == "cash-financial-step-inspect") return CashFinancialStepReadiness.Inspect(client);
if (mode == "cash-financial-package-new" || mode == "cash-financial-package-new-inspect")
    return CashFinancialExecutionPackageDeployment.Run(client, mode == "cash-financial-package-new");
if (mode == "cash-financial-api-inspect" || mode == "cash-financial-api-register")
    return CashFinancialApiDeployment.Run(client, mode == "cash-financial-api-register");
if (mode == "cash-ledger-contract-inspect") return CashLedgerContractReadiness.Inspect(client);
if (mode == "cash-operation-contract-inspect") return CashOperationMetadataContract.Run(client, false);
if (mode == "cash-operation-contract")
{
    CashGuardDeployment.Run(client, false, true);
    CashSchemaReadiness.Verify(client);
    return CashOperationMetadataContract.Run(client, true);
}
if (mode.StartsWith("cash-runtime-", StringComparison.Ordinal)) return CashRuntimeReadiness.Run(client, mode);
if (mode == "cash-guard-inspect" || mode == "cash-guard-steps")
    return CashGuardDeployment.Run(client, mode == "cash-guard-steps");
if (mode == "cash-guard-register") return CashGuardRegistration.Run(client);
if (mode == "cash-guard-probe") return CashGuardDeployment.Probe(client);
if (mode == "cash-schema-verify") return CashSchemaReadiness.Verify(client);
if (mode == "cash-transaction-probe") return CashTransactionProbe.Run(client);
if (mode == "cash-keys" || mode == "cash-keys-inspect")
    return CashUniqueKeys.Run(client, mode == "cash-keys");
if (mode == "cash-schema-publish")
{
    client.Execute(new PublishXmlRequest { ParameterXml =
        "<importexportxml><entities><entity>fmg_cashreceipt</entity><entity>fmg_cashtransfer</entity><entity>fmg_cashposting</entity><entity>fmg_accountopening</entity><entity>fmg_bankstatement</entity><entity>fmg_bankreconciliation</entity></entities></importexportxml>" });
    Console.WriteLine("F5_SCHEMA_PUBLISHED");
    return 0;
}
if (mode == "cash-schema-inspect" || mode == "cash-schema")
    return CashSchemaDeployment.Run(client, mode == "cash-schema");
if (mode == "budget-approval-probe") return BudgetApprovalReadiness.ProbeUnauthorized(client);
if (mode == "budget-approval-role-cleanup") return ApprovalRoleCleanup.Run(client);
if (mode == "budget-approval-schema" || mode == "budget-approval-role" ||
    mode == "budget-approval-api" || mode == "budget-approval-role-permissions")
    return BudgetApprovalProvisioning.Run(client, mode);
if (mode.StartsWith("budget-", StringComparison.Ordinal)) return BudgetDeployment.Run(client, mode);
if (mode == "inspect-schema" || mode == "schema")
    return SchemaDeployment.Run(client, mode == "schema");
if (mode == "verify-schema") return SchemaVerifier.Run(client);
if (mode == "publish-schema")
{
    SchemaDeployment.Run(client, false);
    client.Execute(new PublishXmlRequest { ParameterXml =
        "<importexportxml><entities><entity>fmg_budget</entity><entity>fmg_budgetallocation</entity><entity>fmg_activityevent</entity></entities></importexportxml>" });
    Console.WriteLine("PUBLISHED THREE FINANCIAL MANAGER TABLES");
    return 0;
}

if (mode == "verify")
{
    foreach(var name in new[]{"FT_ValidateCreate","FT_ValidateUpdate","FT_ProtectDeletion"})
    {
        var steps = List("sdkmessageprocessingstep", "name", name, "sdkmessageprocessingstepid", "stage", "mode", "statecode");
        if(steps.Entities.Count != 1) throw new InvalidOperationException("Expected exactly one step: "+name);
        var step = steps.Entities[0];
        if(step.GetAttributeValue<OptionSetValue>("stage")?.Value != 10 ||
           step.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
           step.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
            throw new InvalidOperationException("Step must be active sync PreValidation: "+name);
        if(name != "FT_ValidateCreate")
        {
            var img = new QueryExpression("sdkmessageprocessingstepimage") {ColumnSet=new ColumnSet("entityalias","imagetype"),TopCount=3};
            img.Criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.Equal, step.Id);
            var images = client.RetrieveMultiple(img).Entities;
            if(images.Count!=1 || images[0].GetAttributeValue<string>("entityalias")!="Before" ||
               images[0].GetAttributeValue<OptionSetValue>("imagetype")?.Value!=0)
                throw new InvalidOperationException("Required Before image missing: "+name);
        }
    }
    Console.WriteLine("All three active synchronous PreValidation steps and images verified.");
    Console.WriteLine("LIVE TESTS: "+LiveVerifier.Run(client));
    return 0;
}

if (mode == "register")
{
    if (packages.Entities.Count > 0)
    {
        Console.WriteLine("Package exists; not updating/replacing it. Use steps only after inspecting its type.");
        return 0;
    }
    var path = Environment.GetEnvironmentVariable("FM_PACKAGE_PATH");
    if (String.IsNullOrWhiteSpace(path) || !File.Exists(path) || !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("FM_PACKAGE_PATH must be a validated local NuGet package file.");
        return 5;
    }
    var bytes = File.ReadAllBytes(path);
    if (bytes.Length > 10_000_000) { Console.Error.WriteLine("Package unexpectedly large."); return 5; }
    var row = new Entity("pluginpackage");
    row["name"] = AssemblyName;
    row["uniquename"] = "fmg_FinancialManager_Validation";
    row["version"] = "1.0.0";
    row["content"] = Convert.ToBase64String(bytes);
    var req = new CreateRequest { Target = row };
    req.Parameters["SolutionUniqueName"] = Solution;
    var response = (CreateResponse)client.Execute(req);
    Console.WriteLine("Package created in solution (id present: " + (response.id != Guid.Empty) + ").");
    var registeredTypes = List("plugintype", "typename", PluginClass, "plugintypeid");
    Console.WriteLine("Required plugin type found after registration: " + (registeredTypes.Entities.Count == 1));
    return registeredTypes.Entities.Count == 1 ? 0 : 6;
}

if (types.Entities.Count != 1) { Console.Error.WriteLine("Step registration aborted: exact plug-in class not present."); return 6; }
var typeId = types.Entities[0].Id;
string[] names = { "FT_ValidateCreate", "FT_ValidateUpdate", "FT_ProtectDeletion" };
string[] messages = { "Create", "Update", "Delete" };
string imgCols = String.Join(",", new[]{
    "fmg_status","fmg_amount","fmg_transactiondate","fmg_flowclass","fmg_scope","fmg_category",
    "fmg_branch","fmg_cashaccount","fmg_description","fmg_documentreference",
    "fmg_voidreason","fmg_externalrequestid","transactioncurrencyid"
});
for(int i = 0; i < names.Length; i++)
{
    var exists = List("sdkmessageprocessingstep","name",names[i], "sdkmessageprocessingstepid","name");
    if (exists.Entities.Count > 0) { Console.WriteLine(names[i]+": already present; skip"); continue; }
    var msg = List("sdkmessage", "name", messages[i], "sdkmessageid");
    if (msg.Entities.Count != 1) throw new InvalidOperationException("Expected unique sdkmessage: "+messages[i]);
    var filterQuery = new QueryExpression("sdkmessagefilter") { ColumnSet = new ColumnSet("sdkmessagefilterid"), TopCount = 2 };
    filterQuery.Criteria.AddCondition("sdkmessageid",ConditionOperator.Equal,msg.Entities[0].Id);
    filterQuery.Criteria.AddCondition("primaryobjecttypecode",ConditionOperator.Equal,Table);
    var filters = client.RetrieveMultiple(filterQuery).Entities;
    if(filters.Count!=1) throw new InvalidOperationException("Expected unique SDK message filter for "+messages[i]+" and "+Table);

    var step = new Entity("sdkmessageprocessingstep");
    step["name"] = names[i];
    step["eventhandler"] = new EntityReference("plugintype",typeId);
    step["sdkmessageid"] = new EntityReference("sdkmessage",msg.Entities[0].Id);
    step["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter",filters[0].Id);
    step["stage"] = new OptionSetValue(10);
    step["mode"] = new OptionSetValue(0);
    step["rank"] = 10;
    step["supporteddeployment"] = new OptionSetValue(0);
    var create = new CreateRequest {Target=step};
    create.Parameters["SolutionUniqueName"]=Solution;
    var newStep = ((CreateResponse)client.Execute(create)).id;
    Console.WriteLine("Registered "+names[i]+" as synchronous PreValidation");
    if(i != 0)
    {
        var image = new Entity("sdkmessageprocessingstepimage");
        image["name"]="Before";
        image["entityalias"]="Before";
        image["imagetype"]=new OptionSetValue(0);
        image["messagepropertyname"]="Target";
        image["attributes"] = i == 1 ? imgCols : "fmg_status";
        image["sdkmessageprocessingstepid"]=new EntityReference("sdkmessageprocessingstep",newStep);
        var createImage = new CreateRequest { Target = image };
        createImage.Parameters["SolutionUniqueName"]=Solution;
        client.Execute(createImage);
        Console.WriteLine("  Before image registered for "+names[i]);
    }
}
return 0;
