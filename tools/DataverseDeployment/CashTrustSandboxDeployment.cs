using System;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Security.Cryptography;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

internal static class CashTrustSandboxDeployment
{
    private const string Solution = "FinancialManager";
    private const string Table = "fmg_cashtrustprobe";
    private const string Package = "fmg_CashTrustProbe";
    private const string Guard = "FinancialManager.CashTrustProbe.F5_CashTrustGuard";
    private const string Main = "FinancialManager.CashTrustProbe.F5_CashTrustProof";
    private const string Api = "fmg_CashNestedTrustProbe";
    private const string Privilege = "prvCreatefmg_cashtrustprobe";
    private static readonly string[] Messages = { "Create", "Update", "Delete" };

    private static Entity[] Find(ServiceClient svc, string entity, string field, object value,
        params string[] columns)
    {
        var query = new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet(columns), TopCount = 3
        };
        query.Criteria.AddCondition(field, ConditionOperator.Equal, value);
        var rows = svc.RetrieveMultiple(query);
        if (rows == null || rows.MoreRecords || rows.Entities.Count > 2)
            throw new InvalidOperationException("Ambiguous F5 isolated probe metadata.");
        return rows.Entities.ToArray();
    }

    private static Guid TypeId(ServiceClient svc, string name)
    {
        var result = Find(svc, "plugintype", "typename", name, "plugintypeid");
        if (result.Length > 1)
            throw new InvalidOperationException("Duplicate diagnostic plugin type.");
        return result.Length == 1 ? result[0].Id : Guid.Empty;
    }

    private static string ResolveExecutePrivilege(ServiceClient svc)
    {
        var records = Find(svc, "privilege", "name", Privilege, "privilegeid", "name");
        if (records.Length != 1)
            throw new InvalidOperationException("Generated sandbox Create privilege missing.");
        var actual = records[0].GetAttributeValue<string>("name");
        if (string.IsNullOrWhiteSpace(actual) ||
            !string.Equals(actual, Privilege, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected generated sandbox privilege.");
        return actual; // Exact Dataverse case is significant when registering an API.
    }

    private static EntityMetadata? ReadSchema(ServiceClient svc)
    {
        try
        {
            return ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
            {
                LogicalName = Table, EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            })).EntityMetadata;
        }
        catch (FaultException<OrganizationServiceFault> error)
        {
            if (error.Detail?.ErrorCode == unchecked((int)0x80040217))
                return null;
            throw;
        }
    }

    private static void RequireExistingGuardrails(ServiceClient svc)
    {
        CashGuardDeployment.Run(svc, false, true);
        CashGrantGuardDeployment.Steps(svc, false, true);
        CashGrantRequestGuardDeployment.Steps(svc, false, true);
        CashSchemaReadiness.Verify(svc);
        var grants = svc.RetrieveMultiple(new QueryExpression("fmg_cashaccessgrant")
        {
            ColumnSet = new ColumnSet(false), TopCount = 1
        });
        var reviews = svc.RetrieveMultiple(new QueryExpression("fmg_cashgrantrequest")
        {
            ColumnSet = new ColumnSet(false), TopCount = 1
        });
        if (grants.MoreRecords || grants.Entities.Count != 0 ||
            reviews.MoreRecords || reviews.Entities.Count != 0)
            throw new InvalidOperationException("No live grants or requests permitted during diagnostic.");
    }

    public static int Run(ServiceClient svc, string mode)
    {
        RequireExistingGuardrails(svc);
        if (mode == "cash-trust-finish")
        {
            // Resume a partial test-only provision without reinstalling
            // a package or recreating any previously installed table.
            foreach (var phase in new[] {
                "cash-trust-inspect", "cash-trust-steps",
                "cash-trust-api", "cash-trust-probe" })
            {
                Console.WriteLine("F5_TRUST_RESUME_PHASE_START=" + phase);
                RunPhase(svc, phase);
                Console.WriteLine("F5_TRUST_RESUME_PHASE_DONE=" + phase);
            }
            return 0;
        }
        if (mode == "cash-trust-setup")
        {
            foreach (var phase in new[] {
                "cash-trust-package", "cash-trust-schema",
                "cash-trust-steps", "cash-trust-api", "cash-trust-probe" })
            {
                Console.WriteLine("F5_TRUST_PHASE_START=" + phase);
                RunPhase(svc, phase);
                Console.WriteLine("F5_TRUST_PHASE_DONE=" + phase);
            }
            return 0;
        }
        return RunPhase(svc, mode);
    }

    private static int RunPhase(ServiceClient svc, string mode)
    {
        if (mode == "cash-trust-inspect")
        {
            var meta = ReadSchema(svc);
            Console.WriteLine("F5_TRUST_PROBE_TABLE_EXISTS=" + (meta != null));
            if (meta != null)
                Console.WriteLine("F5_TRUST_PROBE_CANONICAL_PRIVILEGE=" +
                    ResolveExecutePrivilege(svc));
            Console.WriteLine("F5_TRUST_PROBE_GUARD_PRESENT=" + (TypeId(svc, Guard) != Guid.Empty));
            Console.WriteLine("F5_TRUST_PROBE_MAIN_PRESENT=" + (TypeId(svc, Main) != Guid.Empty));
            Console.WriteLine("F5_TRUST_PROBE_API_EXISTS=" + (Find(svc, "customapi", "uniquename", Api,
                "customapiid").Length == 1));
            if (meta != null)
            {
                var q = new QueryExpression(Table) {
                    ColumnSet = new ColumnSet(false), TopCount = 1
                };
                Console.WriteLine("F5_TRUST_PROBE_ROWS_EXIST=" +
                    (svc.RetrieveMultiple(q).Entities.Count != 0));
            }
            return 0;
        }
        if (mode == "cash-trust-package")
        {
            if (TypeId(svc, Main) != Guid.Empty || TypeId(svc, Guard) != Guid.Empty ||
                Find(svc, "pluginpackage", "name", Package, "pluginpackageid").Length != 0)
                throw new InvalidOperationException("Never overwrite an existing trust probe package.");
            var path = Environment.GetEnvironmentVariable("FM_CASH_TRUST_PROBE_PACKAGE_PATH");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
                Path.GetFileName(path) != "fmg_CashTrustProbe.1.0.0.nupkg")
                throw new InvalidOperationException("Isolated signed-off test NuGet path missing.");
            var data = File.ReadAllBytes(path);
            if (data.Length < 2000 || data.Length > 2000000)
                throw new InvalidOperationException("Unexpected test plugin package size.");
            var row = new Entity("pluginpackage");
            row["name"] = Package;
            row["uniquename"] = Package;
            row["version"] = "1.0.0";
            row["content"] = Convert.ToBase64String(data);
            var req = new CreateRequest { Target = row };
            req.Parameters["SolutionUniqueName"] = Solution;
            var result = (CreateResponse)svc.Execute(req);
            if (result.id == Guid.Empty || TypeId(svc, Main) == Guid.Empty ||
                TypeId(svc, Guard) == Guid.Empty)
                throw new InvalidOperationException("Expected both sandbox guard and API types.");
            Console.WriteLine("F5_TRUST_PROBE_PACKAGE_REGISTERED");
            return 0;
        }
        if (mode == "cash-trust-package-id")
        {
            // Read-only: PAC plugin push accepts this exact package ID.
            // Update via official PAC rather than direct Content SDK write.
            var packages = Find(svc, "pluginpackage", "name", Package,
                "pluginpackageid", "name", "version");
            if (packages.Length != 1 || packages[0].Id == Guid.Empty ||
                packages[0].GetAttributeValue<string>("version") != "1.0.0" ||
                TypeId(svc, Main) == Guid.Empty || TypeId(svc, Guard) == Guid.Empty)
                throw new InvalidOperationException("Unverified isolated package identity.");
            Console.WriteLine("F5_TRUST_SYNTHETIC_PACKAGE_ID=" + packages[0].Id.ToString("D"));
            return 0;
        }        if (mode == "cash-trust-schema")
        {
            if (ReadSchema(svc) == null)
            {
                if (TypeId(svc, Guard) == Guid.Empty)
                    throw new InvalidOperationException("Register test-only guard package first.");
                svc.Execute(new CreateEntityRequest
                {
                    SolutionUniqueName = Solution,
                    Entity = new EntityMetadata
                    {
                        SchemaName = "fmg_CashTrustProbe",
                        DisplayName = new Label("Cash Trust Proof (synthetic)", 3082),
                        DisplayCollectionName = new Label("Cash Trust Proofs (synthetic)", 3082),
                        Description = new Label("Isolated test-only rollback diagnostic, NOT financial rows.", 3082),
                        OwnershipType = OwnershipTypes.UserOwned,
                        IsAuditEnabled = new BooleanManagedProperty(true)
                    },
                    PrimaryAttribute = new StringAttributeMetadata
                    {
                        SchemaName = "fmg_Name",
                        DisplayName = new Label("Synthetic test key", 3082),
                        MaxLength = 150,
                        RequiredLevel = new AttributeRequiredLevelManagedProperty(
                            AttributeRequiredLevel.ApplicationRequired)
                    }
                });
                Console.WriteLine("F5_TRUST_PROBE_TABLE_CREATED");
            }
            var metadata = ReadSchema(svc) ??
                throw new InvalidOperationException("Diagnostic test table could not be created.");
            if (metadata.OwnershipType != OwnershipTypes.UserOwned ||
                metadata.IsAuditEnabled?.Value != true)
                throw new InvalidOperationException("Unsafe diagnostic table configuration.");
            svc.Execute(new PublishXmlRequest
            {
                ParameterXml = "<importexportxml><entities><entity>" +
                    Table + "</entity></entities></importexportxml>"
            });
            Console.WriteLine("F5_TRUST_PROBE_SCHEMA_PUBLISHED");
            return 0;
        }
        if (mode == "cash-trust-steps")
        {
            if (ReadSchema(svc) == null || TypeId(svc, Guard) == Guid.Empty)
                throw new InvalidOperationException("Test table/guard type missing.");
            var typeId = TypeId(svc, Guard);
            foreach (var name in Messages)
            {
                var stepName = "F5_SyntheticTrustDeny_" + name;
                var matches = Find(svc, "sdkmessageprocessingstep", "name", stepName,
                    "sdkmessageprocessingstepid", "eventhandler", "stage", "mode",
                    "statecode", "sdkmessagefilterid");
                if (matches.Length > 1)
                    throw new InvalidOperationException("Duplicated test guard.");
                if (matches.Length == 1)
                {
                    var step = matches[0];
                    if (step.GetAttributeValue<EntityReference>("eventhandler")?.Id != typeId ||
                        step.GetAttributeValue<OptionSetValue>("stage")?.Value != 10 ||
                        step.GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                        step.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                        throw new InvalidOperationException("Test guard altered.");
                    Console.WriteLine("F5_TRUST_GUARD_ACTIVE " + name);
                    continue;
                }
                var sdk = Find(svc, "sdkmessage", "name", name, "sdkmessageid");
                if (sdk.Length != 1)
                    throw new InvalidOperationException("Missing synthetic SDK message.");
                var q = new QueryExpression("sdkmessagefilter")
                {
                    ColumnSet = new ColumnSet("sdkmessagefilterid"), TopCount = 2
                };
                q.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, sdk[0].Id);
                q.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, Table);
                var filters = svc.RetrieveMultiple(q);
                if (filters.MoreRecords || filters.Entities.Count != 1)
                    throw new InvalidOperationException("Synthetic guard filter missing.");
                var row = new Entity("sdkmessageprocessingstep");
                row["name"] = stepName;
                row["eventhandler"] = new EntityReference("plugintype", typeId);
                row["sdkmessageid"] = new EntityReference("sdkmessage", sdk[0].Id);
                row["sdkmessagefilterid"] =
                    new EntityReference("sdkmessagefilter", filters.Entities[0].Id);
                row["stage"] = new OptionSetValue(10);
                row["mode"] = new OptionSetValue(0);
                row["rank"] = 1;
                row["supporteddeployment"] = new OptionSetValue(0);
                var req = new CreateRequest { Target = row };
                req.Parameters["SolutionUniqueName"] = Solution;
                svc.Execute(req);
                Console.WriteLine("F5_TRUST_GUARD_REGISTERED " + name);
            }
            return 0;
        }
        if (mode == "cash-trust-api")
        {
            if (ReadSchema(svc) == null || TypeId(svc, Main) == Guid.Empty)
                throw new InvalidOperationException("Synthetic schema/plugin missing.");
            foreach (var name in Messages)
            {
                if (Find(svc, "sdkmessageprocessingstep", "name",
                    "F5_SyntheticTrustDeny_" + name, "sdkmessageprocessingstepid").Length != 1)
                    throw new InvalidOperationException("Missing test guard step.");
            }
            var canonicalPrivilege = ResolveExecutePrivilege(svc);
            var existing = Find(svc, "customapi", "uniquename", Api,
                "customapiid", "plugintypeid", "executeprivilegename");
            if (existing.Length > 1)
                throw new InvalidOperationException("Multiple test APIs.");
            Guid apiId;
            if (existing.Length == 0)
            {
                var api = new Entity("customapi");
                api["name"] = Api;
                api["uniquename"] = Api;
                api["displayname"] = "F5 synthetic nested-write rollback test (dev only)";
                api["description"] = "Creates two isolated synthetic rows and intentionally rolls them back.";
                api["bindingtype"] = new OptionSetValue(0);
                api["allowedcustomprocessingsteptype"] = new OptionSetValue(0);
                api["isfunction"] = false;
                api["isprivate"] = true;
                api["workflowsdkstepenabled"] = false;
                api["executeprivilegename"] = canonicalPrivilege;
                api["plugintypeid"] = new EntityReference("plugintype", TypeId(svc, Main));
                var req = new CreateRequest { Target = api };
                req.Parameters["SolutionUniqueName"] = Solution;
                apiId = ((CreateResponse)svc.Execute(req)).id;
                if (apiId == Guid.Empty)
                    throw new InvalidOperationException("API diagnostic registration failed.");
                Console.WriteLine("F5_TRUST_ROLLBACK_API_CREATED");
            }
            else
            {
                if (existing[0].GetAttributeValue<EntityReference>("plugintypeid")?.Id !=
                    TypeId(svc, Main) ||
                    existing[0].GetAttributeValue<string>("executeprivilegename") != canonicalPrivilege)
                    throw new InvalidOperationException("Untrusted synthetic API drift.");
                apiId = existing[0].Id;
            }
            var paramsNow = Find(svc, "customapirequestparameter", "uniquename", "RequestKey",
                "customapirequestparameterid", "customapiid", "type");
            var linked = paramsNow.Where(x =>
                x.GetAttributeValue<EntityReference>("customapiid")?.Id == apiId).ToArray();
            if (linked.Length > 1)
                throw new InvalidOperationException("Duplicated test RequestKey.");
            if (linked.Length == 0)
            {
                var par = new Entity("customapirequestparameter");
                par["name"] = Api + ".RequestKey";
                par["uniquename"] = "RequestKey";
                par["customapiid"] = new EntityReference("customapi", apiId);
                par["type"] = new OptionSetValue(10);
                par["isoptional"] = false;
                var req = new CreateRequest { Target = par };
                req.Parameters["SolutionUniqueName"] = Solution;
                svc.Execute(req);
                Console.WriteLine("F5_TRUST_REQUEST_PARAMETER_CREATED");
            }
            else if (linked[0].GetAttributeValue<OptionSetValue>("type")?.Value != 10)
                throw new InvalidOperationException("Wrong diagnostic input type.");
            Console.WriteLine("F5_TRUST_SYNTHETIC_API_REGISTERED_NO_FINANCIAL_WRITES");
            return 0;
        }
        if (mode == "cash-trust-mainstep-inspect")
        {
            var sdk = Find(svc, "sdkmessage", "name", Api, "sdkmessageid", "name");
            var api = Find(svc, "customapi", "uniquename", Api,
                "customapiid", "plugintypeid", "allowedcustomprocessingsteptype");
            if (sdk.Length != 1 || api.Length != 1 ||
                api[0].GetAttributeValue<EntityReference>("plugintypeid")?.Id !=
                    TypeId(svc, Main) ||
                api[0].GetAttributeValue<OptionSetValue>(
                    "allowedcustomprocessingsteptype")?.Value != 0)
                throw new InvalidOperationException("Synthetic Custom API registration drift.");
            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("name", "eventhandler", "stage",
                    "mode", "statecode", "sdkmessageid"),
                TopCount = 40
            };
            query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, sdk[0].Id);
            var steps = svc.RetrieveMultiple(query);
            if (steps == null || steps.MoreRecords)
                throw new InvalidOperationException("Ambiguous Custom API main step metadata.");
            var matching = steps.Entities.Where(step =>
                step.GetAttributeValue<EntityReference>("eventhandler")?.Id ==
                    TypeId(svc, Main) &&
                step.GetAttributeValue<OptionSetValue>("stage")?.Value == 30 &&
                step.GetAttributeValue<OptionSetValue>("mode")?.Value == 0 &&
                step.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0)
                .ToArray();
            Console.WriteLine("F5_TRUST_MAIN_STEP_CANDIDATES=" + steps.Entities.Count);
            Console.WriteLine("F5_TRUST_MAIN_STEP_MATCHES=" + matching.Length);
            if (matching.Length == 1)
                Console.WriteLine("F5_TRUST_MAIN_STEP_ID=" + matching[0].Id.ToString("D"));
            else
                throw new InvalidOperationException(
                    "Cannot independently attest the MainOperation SDK step.");
            return 0;
        }
        if (mode == "cash-trust-pin")
        {
            // Idempotent DEV-only configuration. Pin the three synthetic
            // guard steps to the exact live MainOperation SDK step ID.
            var sdk = Find(svc, "sdkmessage", "name", Api, "sdkmessageid");
            var currentApi = Find(svc, "customapi", "uniquename", Api,
                "customapiid", "plugintypeid", "allowedcustomprocessingsteptype");
            if (sdk.Length != 1 || currentApi.Length != 1 ||
                currentApi[0].GetAttributeValue<EntityReference>("plugintypeid")?.Id !=
                    TypeId(svc, Main) ||
                currentApi[0].GetAttributeValue<OptionSetValue>(
                    "allowedcustomprocessingsteptype")?.Value != 0)
                throw new InvalidOperationException("Untrusted synthetic API MainOperation.");
            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("stage", "mode", "statecode", "eventhandler"),
                TopCount = 10
            };
            query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, sdk[0].Id);
            var candidates = svc.RetrieveMultiple(query);
            if (candidates.MoreRecords)
                throw new InvalidOperationException("Ambiguous test MainOperation.");
            var mainSteps = candidates.Entities.Where(s =>
                s.GetAttributeValue<EntityReference>("eventhandler")?.Id ==
                    TypeId(svc, Main) &&
                s.GetAttributeValue<OptionSetValue>("stage")?.Value == 30 &&
                s.GetAttributeValue<OptionSetValue>("mode")?.Value == 0 &&
                s.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0).ToArray();
            if (mainSteps.Length != 1 || mainSteps[0].Id == Guid.Empty)
                throw new InvalidOperationException("No unique, active, trusted test MainOperation step.");
            var pin = mainSteps[0].Id.ToString("D");
            var guardType = TypeId(svc, Guard);
            if (guardType == Guid.Empty)
                throw new InvalidOperationException("Synthetic guard type not installed.");
            foreach (var message in Messages)
            {
                var rows = Find(svc, "sdkmessageprocessingstep", "name",
                    "F5_SyntheticTrustDeny_" + message,
                    "sdkmessageprocessingstepid", "eventhandler", "stage",
                    "mode", "statecode", "configuration");
                if (rows.Length != 1 ||
                    rows[0].GetAttributeValue<EntityReference>("eventhandler")?.Id != guardType ||
                    rows[0].GetAttributeValue<OptionSetValue>("stage")?.Value != 10 ||
                    rows[0].GetAttributeValue<OptionSetValue>("mode")?.Value != 0 ||
                    rows[0].GetAttributeValue<OptionSetValue>("statecode")?.Value != 0)
                    throw new InvalidOperationException("Untrusted synthetic guard binding.");
                var prior = rows[0].GetAttributeValue<string>("configuration");
                if (!string.IsNullOrWhiteSpace(prior) && prior != pin)
                    throw new InvalidOperationException("Existing synthetic step pin has drifted.");
                if (prior == pin)
                {
                    Console.WriteLine("F5_TRUST_SYNTHETIC_GUARD_PIN_ALREADY_VALID " + message);
                    continue;
                }
                var update = new Entity("sdkmessageprocessingstep", rows[0].Id);
                update["configuration"] = pin;
                svc.Update(update);
                Console.WriteLine("F5_TRUST_SYNTHETIC_GUARD_PINNED " + message);
            }
            return 0;
        }
        if (mode == "cash-trust-probe")
        {
            foreach (var name in Messages)
                if (Find(svc, "sdkmessageprocessingstep", "name",
                    "F5_SyntheticTrustDeny_" + name, "sdkmessageprocessingstepid").Length != 1)
                    throw new InvalidOperationException("Synthetic guard missing.");
            var key = Guid.NewGuid().ToString("D");
            string Label(int i) => "F5-SYNTHETIC-TRUST-" + key + "-" + i;
            var fake = new Entity(Table, Guid.NewGuid());
            fake["fmg_name"] = Label(8);
            try
            {
                svc.Create(fake);
                throw new InvalidOperationException("UNSAFE: Direct synthetic CRUD was accepted.");
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                if (!(ex.Detail?.Message ?? ex.Message).Contains("F5S002"))
                    throw new InvalidOperationException("Unexpected direct synthetic guard error.", ex);
                Console.WriteLine("F5_TRUST_DIRECT_CREATE_DENIED_F5S002");
            }
            var request = new OrganizationRequest(Api);
            request.Parameters["RequestKey"] = key;
            try
            {
                svc.Execute(request);
                throw new InvalidOperationException("UNSAFE: Rollback diagnostic unexpectedly committed.");
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                var message = ex.Detail?.Message ?? ex.Message;
                if (!message.Contains("F5P_ROLLBACK_EXPECTED"))
                    throw new InvalidOperationException("Nested trust or rollback diagnostic did not reach two writes.", ex);
                Console.WriteLine("F5_TRUST_TWO_NESTED_CREATES_REACHED_EXPECTED_ROLLBACK");
            }
            for (int i = 0; i < 2; i++)
            {
                var q = new QueryExpression(Table)
                {
                    ColumnSet = new ColumnSet(false), TopCount = 1
                };
                q.Criteria.AddCondition("fmg_name", ConditionOperator.Equal, Label(i));
                if (svc.RetrieveMultiple(q).Entities.Count != 0)
                    throw new InvalidOperationException("UNSAFE: Diagnostic rollback left rows.");
            }
            Console.WriteLine("F5_TRUST_TWO_WRITES_ROLLED_BACK_REAL_DATAVERSE");
            return 0;
        }
        throw new InvalidOperationException("Unsupported trust probe mode.");
    }
}
