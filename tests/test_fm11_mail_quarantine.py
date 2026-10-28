"""FM11 mail quarantine cloud flow must never create financial records."""
from __future__ import annotations
import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / "integrations/power-automate"
spec = importlib.util.spec_from_file_location("fm11_mail_flow", FOLDER / "build_mail_quarantine.py")
if spec is None or spec.loader is None:
    raise RuntimeError("Mail flow preview builder unavailable")
builder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)


class MailQuarantineSafetyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.body = builder.build()

    def test_generator_is_deterministic_and_no_pii_in_source(self):
        actual = json.loads(builder.OUT.read_text("utf-8"))
        self.assertEqual(actual, self.body)
        self.assertEqual(builder.SUBJECT, "FM11-DRAFT-V1")
        text = builder.OUT.read_text("utf-8")
        for bad in ("onmicrosoft.com", "token", "fmg_financialtransaction",
                    "Add a new row", "StartAndWaitForAnApproval"):
            self.assertNotIn(bad, text)

    def test_connector_is_real_office_365_outlook_trigger(self):
        trigger = self.body["triggers"]["When_a_new_email_arrives_V3"]
        self.assertEqual(len(self.body["triggers"]), 1)
        self.assertEqual(trigger["type"], "OpenApiConnectionNotification")
        self.assertEqual(trigger["inputs"]["host"]["operationId"], "OnNewEmailV3")
        self.assertEqual(trigger["inputs"]["host"]["connectionName"], "shared_office365")
        self.assertEqual(trigger["inputs"]["parameters"]["subjectFilter"], "FM11-DRAFT-V1")
        self.assertEqual(trigger["inputs"]["parameters"]["folderPath"], "Inbox")
        self.assertFalse(trigger["inputs"]["parameters"]["includeAttachments"])

    def test_action_must_only_cancel_with_no_record_or_approval(self):
        self.assertEqual(self.body["actions"], {
            "Quarantine_no_financial_writes": {
                "type": "Terminate", "runAfter": {},
                "inputs": {"runStatus": "Cancelled"},
            },
        })
        self.assertEqual(len(self.body["actions"]), 1)

    def test_deployment_has_existing_solution_and_explicit_apply_guard(self):
        csharp = (ROOT / "tools/DataverseDeployment/Fm11MailQuarantineFlow.cs").read_text("utf-8")
        program = (ROOT / "tools/DataverseDeployment/Program.cs").read_text("utf-8")
        self.assertIn('private const string Solution = "FinancialManager"', csharp)
        self.assertIn('FM11_OUTLOOK_CONNECTION_NAME', csharp)
        self.assertIn("VerifyInert(read)", csharp)
        self.assertIn('statecode")?.Value != 0', csharp)
        self.assertIn("ComponentType = 29", csharp)
        self.assertIn('mode == "fm11-mail-create"', program)
        self.assertIn('mode != "fm11-mail-inspect"', program)
        self.assertIn('Mutation requires explicit --apply.', program)
        self.assertNotIn("SetStateRequest", csharp)
        self.assertNotIn('new Entity("fmg_financialtransaction"', csharp)


if __name__ == "__main__":
    unittest.main()
