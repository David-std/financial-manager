"""FM-11 unverified Dataverse intake and OFF Outlook flow contract tests."""
from __future__ import annotations
import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FLOW = ROOT / "integrations/power-automate"
p = FLOW / "build_mail_staging.py"
spec = importlib.util.spec_from_file_location("mail_staging", p)
assert spec and spec.loader
stage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage)


class MailStagingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = stage.build()

    def test_exact_source_and_closed_contract(self):
        self.assertEqual(self.workflow, json.loads(stage.OUTPUT.read_text("utf-8")))
        self.assertEqual(len(self.workflow["triggers"]), 1)
        actions = self.workflow["actions"]
        self.assertEqual(set(actions), {"Read_message_as_text", "Parse_request", "Stage_unverified_request", "Classify_stage_failure", "Stop_stage_failure"})
        parsed = actions["Parse_request"]
        self.assertEqual(parsed["type"], "ParseJson")
        self.assertEqual(parsed["inputs"]["content"],
                         "@body('Read_message_as_text')?['body']?['content']")
        schema = parsed["inputs"]["schema"]
        self.assertFalse(schema["additionalProperties"])
        for name in ("categoryCode", "requestId", "amountPen",
                     "branchCode", "accountName"):
            self.assertIn(name, schema["required"])
        self.assertEqual(len(schema["properties"]["categoryCode"]["enum"]), 32)
        self.assertIn("103", schema["properties"]["categoryCode"]["enum"])
        self.assertTrue(all("pattern" not in value
                            for value in schema["properties"].values()))

    def test_outlook_graph_fetches_same_message_as_plain_text(self):
        actions = self.workflow["actions"]
        read = actions["Read_message_as_text"]
        self.assertEqual(read["type"], "OpenApiConnection")
        self.assertEqual(read["runAfter"], {})
        self.assertEqual(read["inputs"]["host"]["operationId"], "HttpRequest")
        self.assertEqual(read["inputs"]["host"]["apiId"],
                         "/providers/Microsoft.PowerApps/apis/shared_office365")
        self.assertEqual(read["inputs"]["host"]["connectionName"], "shared_office365")
        parameters = read["inputs"]["parameters"]
        self.assertEqual(parameters["Method"], "GET")
        self.assertEqual(
            parameters["CustomHeader1"],
            'Prefer: outlook.body-content-type="text"',
        )
        self.assertEqual(
            parameters["Uri"],
            "@concat('https://graph.microsoft.com/v1.0/me/messages/', "
            "encodeUriComponent(triggerOutputs()?['body/id']), "
            "'?$select=id,subject,body')",
        )
        self.assertNotIn("users/", parameters["Uri"])
        self.assertEqual(actions["Parse_request"]["runAfter"],
                         {"Read_message_as_text": ["Succeeded"]})
        self.assertNotIn("HtmlToText", json.dumps(self.workflow))
        self.assertEqual(set(a["type"] for a in actions.values()),
                         {"ParseJson", "OpenApiConnection", "Compose", "Terminate"})

    def test_only_unverified_staging_after_successful_parse(self):
        write = self.workflow["actions"]["Stage_unverified_request"]
        self.assertEqual(write["type"], "OpenApiConnection")
        self.assertEqual(write["runAfter"], {"Parse_request": ["Succeeded"]})
        self.assertEqual(write["inputs"]["host"]["operationId"], "CreateRecord")
        values = write["inputs"]["parameters"]
        self.assertEqual(values["entityName"], "fmg_mailintakes")
        self.assertEqual(values["item/fmg_reviewstate"], "Unverified")
        self.assertEqual(values["item/fmg_requestid"],
                         "@body('Parse_request')?['requestId']")
        self.assertNotIn("fmg_financialtransactions", json.dumps(self.workflow))
        self.assertNotIn("Approval", json.dumps(self.workflow))

    def test_workspace_fields_and_fail_closed_duplicate_handling(self):
        actions = self.workflow["actions"]
        write = actions["Stage_unverified_request"]
        values = write["inputs"]["parameters"]
        for field, source in {
            "concept": "name", "amountpen": "amountPen",
            "requesteddate": "date", "categorycode": "categoryCode",
            "branchcode": "branchCode", "accountname": "accountName",
            "flowclass": "flowClass",
        }.items():
            self.assertEqual(values["item/fmg_"+field],
                             "@body('Parse_request')?['"+source+"']")
        self.assertEqual(values["item/fmg_validationstate"], "FormatChecked")
        self.assertEqual(values["item/fmg_rawbody"],
                         "@body('Read_message_as_text')?['body']?['content']")
        self.assertEqual(write["inputs"]["retryPolicy"], {"type": "none"})
        classify = actions["Classify_stage_failure"]
        self.assertEqual(classify["runAfter"], {
            "Stage_unverified_request": ["Failed", "TimedOut"]
        })
        self.assertIn("DUPLICATE_REQUEST", classify["inputs"])
        self.assertIn("STAGING_WRITE_FAILED", classify["inputs"])
        terminal = actions["Stop_stage_failure"]
        self.assertEqual(terminal["type"], "Terminate")
        self.assertEqual(terminal["runAfter"], {
            "Classify_stage_failure": ["Succeeded"]
        })
        self.assertEqual(terminal["inputs"]["runStatus"], "Failed")
        self.assertEqual(terminal["inputs"]["runError"]["code"], "FM11_STAGE_REJECTED")

    def test_trigger_reuses_existing_standard_outlook(self):
        trigger = self.workflow["triggers"]["When_a_new_email_arrives_V3"]
        self.assertEqual(trigger["type"], "OpenApiConnectionNotification")
        self.assertEqual(trigger["inputs"]["parameters"]["subjectFilter"],
                         "FM11-DRAFT-V1")
        self.assertEqual(trigger["inputs"]["host"]["operationId"], "OnNewEmailV3")
        self.assertEqual(trigger["inputs"]["host"]["connectionName"],
                         "shared_office365")

    def test_synthetic_duplicate_probe_cleans_up(self):
        schema = (ROOT / "tools/DataverseDeployment/Fm11IntakeSchema.cs").read_text(
            encoding="utf-8")
        program = (ROOT / "tools/DataverseDeployment/Program.cs").read_text(
            encoding="utf-8")
        self.assertIn("ProbeSyntheticDeduplication", schema)
        self.assertIn("FM11_MAIL_INTAKE_DUPLICATE_REJECTED=true", schema)
        self.assertIn("FM11_MAIL_INTAKE_SYNTHETIC_CLEANUP_VERIFIED=true", schema)
        self.assertIn('service.Delete(Table, first)', schema)
        self.assertIn("finally", schema)
        self.assertIn("EntityKeyIndexStatus.Active", schema)
        self.assertIn('mode == "fm11-mail-probe"', program)

    def test_solution_connection_references_reuse_existing_connections(self):
        code = (ROOT / "tools/DataverseDeployment/Fm11ConnectionReferences.cs").read_text(
            encoding="utf-8")
        self.assertIn("FM11_OUTLOOK_CONNECTION_NAME", code)
        self.assertIn("FM11_DATAVERSE_CONNECTION_NAME", code)
        self.assertIn('"fmg_FM11_Outlook"', code)
        self.assertIn('"fmg_FM11_Dataverse"', code)
        self.assertIn('new Entity("connectionreference")', code)
        self.assertIn('SolutionUniqueName = Solution', code)
        self.assertIn('ComponentType = type', code)
        self.assertIn('client.Delete("connectionreference", id)', code)
        self.assertIn('var connection = item.ConnectionName;', code)
        self.assertNotIn('ComponentType = 10167', code)
        self.assertNotIn('client.Create(new Entity("connection")', code)

    def test_existing_flow_upgrade_is_off_only_and_reuses_connections(self):
        src = (ROOT / "tools/DataverseDeployment/Fm11MailStagingFlow.cs").read_text("utf-8")
        program = (ROOT / "tools/DataverseDeployment/Program.cs").read_text("utf-8")
        self.assertIn('mode == "fm11-mail-stage-upgrade"', program)
        self.assertIn('mode != "fm11-mail-stage-upgrade"', program)
        self.assertIn("Mutation requires explicit --apply.", program)
        self.assertIn("public static int UpgradeOff", src)
        self.assertIn("VerifyOff(current, acceptLegacy: true)", src)
        self.assertIn('EntityKeyIndexStatus.Active', src)
        self.assertIn('props["definition"] =', src)
        self.assertIn("VerifyOff(saved)", src)
        self.assertIn("FM11_MAIL_STAGING_TEXT_READ_UPGRADED=true", src)
        self.assertIn("FM11_MAIL_STAGING_OFF=true", src)
        self.assertNotIn("SetStateRequest", src.split("public static int UpgradeOff", 1)[1])

    def test_sdk_requires_active_alternate_key_and_off_state(self):
        src = (ROOT / "tools/DataverseDeployment/Fm11MailStagingFlow.cs").read_text(
            "utf-8")
        schema = (ROOT / "tools/DataverseDeployment/Fm11IntakeSchema.cs").read_text(
            "utf-8")
        program = (ROOT / "tools/DataverseDeployment/Program.cs").read_text("utf-8")
        self.assertIn("EntityKeyIndexStatus.Active", src)
        self.assertIn("EntityKeyIndexStatus.Failed", src)
        self.assertIn("FM11_MAIL_STAGING_KEY_ACTIVE=", src)
        self.assertIn('entity.EntitySetName != "fmg_mailintakes"', src)
        self.assertIn('statecode")?.Value != 0', src)
        self.assertIn('ComponentType = 29', src)
        self.assertIn("FM11_DATAVERSE_CONNECTION_NAME", src)
        self.assertIn("FM11_OUTLOOK_CONNECTION_NAME", src)
        self.assertIn('values.GetProperty("item/fmg_reviewstate")', src)
        self.assertIn("CreateEntityKeyRequest", schema)
        self.assertIn('new [] { "fmg_requestid" }', schema)
        self.assertIn("IsAuditEnabled", schema)
        self.assertIn('mode == "fm11-mail-stage"', program)
        self.assertIn('mode == "fm11-mail-schema"', program)
        self.assertNotIn('new Entity("fmg_financialtransaction"', src)


if __name__ == "__main__":
    unittest.main()
