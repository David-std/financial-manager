"""Verify the native expense Shortcut and delivery configuration."""
from __future__ import annotations

import importlib.util
import plistlib
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / "integrations/ios-shortcuts"
spec = importlib.util.spec_from_file_location(
    "fm11_shortcut_v2", FOLDER / "build_shortcut_v2.py")
if spec is None or spec.loader is None:
    raise RuntimeError("No silent-mail Shortcut generator")
silent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(silent)


class NativeSilentMailTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = silent.native.build()
        cls.build = silent.build()

    def by_name(self, name):
        return next(
            a for a in self.build["WFWorkflowActions"]
            if a["WFWorkflowActionParameters"]["UUID"] == silent.native.uid(name)
        )

    def test_original_prompts_preserved_with_explicit_body_conversion(self):
        orig = self.source
        variant = self.build
        self.assertEqual(orig["WFWorkflowName"], "Registrar gasto")
        self.assertEqual(variant["WFWorkflowName"], silent.NAME)
        self.assertEqual(orig["WFWorkflowImportQuestions"],
                         variant["WFWorkflowImportQuestions"])
        self.assertEqual(len(orig["WFWorkflowActions"]) + 9,
                         len(variant["WFWorkflowActions"]))
        for action in orig["WFWorkflowActions"]:
            uuid = action["WFWorkflowActionParameters"]["UUID"]
            edited = next(
                a for a in variant["WFWorkflowActions"]
                if a["WFWorkflowActionParameters"]["UUID"] == uuid
            )
            if uuid in (silent.native.uid("mail"), silent.native.uid("json"),
                        silent.native.uid("categoryCode"), silent.native.uid("payload")):
                continue
            self.assertEqual(action, edited)

    def test_category_code_is_selected_from_split_text_and_not_empty(self):
        native = silent.native
        splitter = self.by_name("categoryParts")
        self.assertEqual(splitter["WFWorkflowActionIdentifier"],
                         "is.workflow.actions.text.split")
        self.assertEqual(splitter["WFWorkflowActionParameters"]["text"],
                         native.attachment("categoryChoice", "Selected Item"))
        self.assertNotIn("WFInput", splitter["WFWorkflowActionParameters"])
        self.assertEqual(splitter["WFWorkflowActionParameters"]["WFTextSeparator"],
                         "Custom")
        self.assertEqual(splitter["WFWorkflowActionParameters"]["WFTextCustomSeparator"],
                         " · ")

        first_item = self.by_name("categoryCode")
        self.assertEqual(first_item["WFWorkflowActionIdentifier"],
                         "is.workflow.actions.getitemfromlist")
        self.assertEqual(first_item["WFWorkflowActionParameters"]["WFItemSpecifier"],
                         "First Item")
        self.assertEqual(first_item["WFWorkflowActionParameters"]["WFInput"],
                         native.attachment("categoryParts", "Split Text"))
        self.assertEqual({
            option.split(" · ", 1)[0] for option in native.category_options()
        }, {
            option[:3] for option in native.category_options()
        })
        for option in native.category_options():
            self.assertRegex(option.split(" · ", 1)[0], r"^[0-9]{3}$")

        payload = self.by_name("payload")["WFWorkflowActionParameters"][
            "WFItems"]["Value"]["WFDictionaryFieldValueItems"]
        code_fields = [f for f in payload
                       if f["WFKey"]["Value"]["string"] == "categoryCode"]
        self.assertEqual(len(code_fields), 1)
        self.assertEqual(code_fields[0]["WFValue"],
                         native.token_string([("categoryCode", "Item from List")]))
        self.assertNotEqual(code_fields[0]["WFValue"]["Value"]["string"], "")

    def test_split_input_key_matches_shortcuts_native_action_contract(self):
        # Split Text uses 'text'; Get Item from List separately uses 'WFInput'.
        splitter = self.by_name("categoryParts")["WFWorkflowActionParameters"]
        picker = self.by_name("categoryCode")["WFWorkflowActionParameters"]
        self.assertEqual(
            set(splitter),
            {"UUID", "text", "WFTextSeparator", "WFTextCustomSeparator"},
        )
        self.assertIn("WFInput", picker)
        self.assertNotIn("text", picker)
        self.assertEqual(splitter["text"]["Value"]["OutputUUID"],
                         silent.native.uid("categoryChoice"))
        self.assertEqual(splitter["text"]["Value"]["OutputName"], "Selected Item")
        self.assertEqual(picker["WFInput"]["Value"]["OutputUUID"],
                         silent.native.uid("categoryParts"))

    def test_empty_category_code_prevents_mail(self):
        native = silent.native
        labels = [a["WFWorkflowActionParameters"]["UUID"]
                  for a in self.build["WFWorkflowActions"]]
        names = ("categoryParts", "categoryCode", "missingCategoryCodeIf",
                 "missingCategoryCodeAlert", "missingCategoryCodeExit",
                 "missingCategoryCodeEnd")
        positions = [labels.index(native.uid(n)) for n in names]
        self.assertEqual(positions, list(range(positions[0], positions[0]+6)))
        self.assertLess(positions[-1], labels.index(native.uid("mail")))
        start = self.by_name("missingCategoryCodeIf")["WFWorkflowActionParameters"]
        end = self.by_name("missingCategoryCodeEnd")["WFWorkflowActionParameters"]
        self.assertEqual(start["WFCondition"], 101)
        self.assertEqual(start["WFInput"], {
            "Type": "Variable",
            "Variable": native.attachment("categoryCode", "Item from List"),
        })
        self.assertEqual(start["GroupingIdentifier"], end["GroupingIdentifier"])
        self.assertEqual(end["WFControlFlowMode"], 2)
        self.assertEqual(self.by_name("missingCategoryCodeExit")[
            "WFWorkflowActionIdentifier"], "is.workflow.actions.exit")

    def test_email_has_text_body_reference_not_attachment_reference(self):
        native = silent.native
        converter = self.by_name("json")
        self.assertEqual(converter["WFWorkflowActionIdentifier"],
                         "is.workflow.actions.detect.text")
        self.assertEqual(converter["WFWorkflowActionParameters"]["WFInput"],
                         native.attachment("payload", "Dictionary"))
        mail = self.by_name("mail")["WFWorkflowActionParameters"]
        self.assertFalse(mail["WFSendEmailActionShowComposeSheet"])
        self.assertEqual(mail["WFSendEmailActionSubject"], "FM11-DRAFT-V1")
        self.assertEqual(mail["WFSendEmailActionInputAttachments"],
                         native.token_string([("json", "Text")]))
        self.assertEqual(mail["WFSendEmailActionToRecipients"],
                         native.attachment("recipient", "Text"))
        self.assertNotEqual(mail["WFSendEmailActionInputAttachments"]["WFSerializationType"],
                            "WFTextTokenAttachment")

    def test_empty_body_stops_before_sending(self):
        native = silent.native
        labels = [
            a["WFWorkflowActionParameters"]["UUID"]
            for a in self.build["WFWorkflowActions"]
        ]
        names = ("json", "missingMailBodyIf", "missingMailBodyAlert",
                 "missingMailBodyExit", "missingMailBodyEnd", "mail")
        positions = [labels.index(native.uid(name)) for name in names]
        self.assertEqual(positions, sorted(positions))
        self.assertEqual(positions, list(range(positions[0], positions[0]+6)))
        start = self.by_name("missingMailBodyIf")["WFWorkflowActionParameters"]
        end = self.by_name("missingMailBodyEnd")["WFWorkflowActionParameters"]
        self.assertEqual(start["WFCondition"], 101)
        self.assertEqual(start["WFControlFlowMode"], 0)
        self.assertEqual(start["WFInput"], {
            "Type": "Variable",
            "Variable": native.attachment("json", "Text"),
        })
        self.assertEqual(start["GroupingIdentifier"], end["GroupingIdentifier"])
        self.assertEqual(end["WFControlFlowMode"], 2)
        self.assertEqual(self.by_name("missingMailBodyExit")["WFWorkflowActionIdentifier"],
                         "is.workflow.actions.exit")
        self.assertIn("vacío",
                      self.by_name("missingMailBodyAlert")["WFWorkflowActionParameters"]
                      ["WFAlertActionMessage"])

    def test_preserves_selector_calendar_amount_and_cancel(self):
        labels = {
            a["WFWorkflowActionParameters"]["UUID"]:
            a for a in self.build["WFWorkflowActions"]
        }
        native = silent.native
        for key in ("name", "amountPen", "date", "dateISO", "categoryChoice",
                    "categoryCode", "branchCode", "accountName",
                    "flowClass", "description", "confirm", "invalidAmountExit"):
            self.assertIn(native.uid(key), labels)
        self.assertTrue(labels[native.uid("confirm")][
            "WFWorkflowActionParameters"]["WFAlertActionCancelButtonShown"])

    def test_binary_roundtrip_and_no_mailbox_or_secret(self):
        xml = FOLDER / "Registrar-gasto-v2.unsigned.plist"
        binary = FOLDER / "Registrar-gasto-v2.unsigned.shortcut"
        self.assertEqual(plistlib.loads(xml.read_bytes()), self.build)
        self.assertEqual(plistlib.loads(binary.read_bytes()), self.build)
        self.assertTrue(binary.read_bytes().startswith(b"bplist00"))
        self.assertEqual(len(self.build["WFWorkflowImportQuestions"]), 1)
        self.assertEqual(self.build["WFWorkflowActions"][0][
            "WFWorkflowActionParameters"]["WFTextActionText"], "")
        content = xml.read_text("utf-8").lower()
        for word in ("client_secret", "access_token", "refresh_token",
                     "bearer ", "onmicrosoft.com", "api.powerplatform.com"):
            self.assertNotIn(word, content)

    def test_no_false_cloud_confirmation_or_financial_write(self):
        identifiers = {
            a["WFWorkflowActionIdentifier"] for a in self.build["WFWorkflowActions"]
        }
        self.assertNotIn("is.workflow.actions.downloadurl", identifiers)
        self.assertNotIn("is.workflow.actions.runjavascript", identifiers)
        self.assertIn("is.workflow.actions.sendemail", identifiers)
        self.assertNotIn("fmg_expense", str(self.build).lower())
        self.assertNotIn("gasto registrado", str(self.build).lower())


if __name__ == "__main__":
    unittest.main()
