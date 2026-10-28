"""Check native HTTP request construction and authentication controls."""
from __future__ import annotations

import importlib.util
import json
import plistlib
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "integrations/ios-shortcuts"
PATH = BASE / "build_http_shortcut.py"
SOURCE = BASE / "Registrar-gasto-http.unsigned.plist"
BINARY = BASE / "Registrar-gasto-http.unsigned.shortcut"
SPEC = importlib.util.spec_from_file_location("fm11_http_lab", PATH)
assert SPEC and SPEC.loader
http = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(http)


class NativeHttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.built = http.build()
        cls.actions = cls.built["WFWorkflowActions"]

    def params(self, name):
        return next(
            action["WFWorkflowActionParameters"]
            for action in self.actions
            if action["WFWorkflowActionParameters"]["UUID"] == http.native.uid(name)
        )

    def test_same_native_ui_and_original_mail_shortcut_not_mutated(self):
        old = http.native.build()
        self.assertEqual(old["WFWorkflowName"], "Registrar gasto")
        self.assertEqual(old["WFWorkflowActions"][-1]["WFWorkflowActionIdentifier"],
                         "is.workflow.actions.sendemail")
        self.assertEqual(self.built["WFWorkflowName"], http.NAME)
        self.assertNotEqual(old["WFWorkflowName"], self.built["WFWorkflowName"])
        self.assertEqual(self.actions[:21], old["WFWorkflowActions"][1:-3])
        ids = [a["WFWorkflowActionIdentifier"] for a in self.actions]
        self.assertNotIn("is.workflow.actions.sendemail", ids)
        self.assertIn("is.workflow.actions.downloadurl", ids)
        for name in ("name", "amountPen", "date", "dateISO", "categoryChoice",
                     "branchCode", "accountName", "flowClass", "description", "confirm"):
            self.assertEqual(self.params(name),
                             next(a["WFWorkflowActionParameters"]
                                  for a in old["WFWorkflowActions"]
                                  if a["WFWorkflowActionParameters"]["UUID"] == http.native.uid(name)))

    def test_preview_is_before_any_auth_or_http_and_exit_in_branch(self):
        order = [a["WFWorkflowActionParameters"]["UUID"] for a in self.actions]
        index = lambda label: order.index(http.native.uid(label))
        self.assertLess(index("httpMode"), index("httpPreviewIf"))
        self.assertLess(index("httpPreviewIf"), index("httpPreviewDictionary"))
        self.assertLess(index("httpPreviewDictionary"), index("httpPreviewExit"))
        self.assertLess(index("httpPreviewExit"), index("httpPreviewEnd"))
        self.assertLess(index("httpPreviewEnd"), index("httpToken"))
        self.assertLess(index("httpToken"), index("httpHttpPost"))
        self.assertEqual(self.params("httpPreviewIf")["WFCondition"], 4)
        self.assertEqual(self.params("httpPreviewIf")["WFConditionalActionString"], http.PREVIEW)
        self.assertEqual(self.params("httpPreviewIf")["GroupingIdentifier"],
                         self.params("httpPreviewEnd")["GroupingIdentifier"])
        self.assertEqual(self.params("httpTokenMissingIf")["WFCondition"], 101)
        self.assertEqual(self.params("httpTokenMissingIf")["GroupingIdentifier"],
                         self.params("httpTokenMissingEnd")["GroupingIdentifier"])

    def test_both_files_reproducible_without_endpoint_token_or_client_secret(self):
        self.assertEqual(plistlib.loads(SOURCE.read_bytes()), self.built)
        self.assertEqual(plistlib.loads(BINARY.read_bytes()), self.built)
        self.assertEqual(SOURCE.read_bytes(),
                         plistlib.dumps(self.built, fmt=plistlib.FMT_XML))
        self.assertEqual(BINARY.read_bytes(),
                         plistlib.dumps(self.built, fmt=plistlib.FMT_BINARY))
        self.assertTrue(BINARY.read_bytes().startswith(b"bplist00"))
        questions = self.built["WFWorkflowImportQuestions"]
        self.assertEqual(len(questions), 1)
        self.assertEqual(
            self.actions[questions[0]["ActionIndex"]]["WFWorkflowActionParameters"]["UUID"],
            http.native.uid("httpEndpoint")
        )
        self.assertEqual(self.params("httpEndpoint")["WFTextActionText"], "")
        serialized = SOURCE.read_text("utf-8").lower()
        for secret in (
            "d5a49e040363e762ba82c4ac8c91c6",
            "authorization: bearer",
            "client_secret",
            "refresh_token",
            "access_token",
            "7515a307f6674c42bc0cfcdce6b9de84",
        ):
            self.assertNotIn(secret, serialized)

    def test_http_post_uses_real_json_fields_not_dictionary_as_text(self):
        post = self.params("httpHttpPost")
        self.assertEqual(post["WFHTTPMethod"], "POST")
        self.assertEqual(post["WFHTTPBodyType"], "JSON")
        self.assertNotIn("WFRequestVariable", post)
        self.assertEqual(post["WFURL"]["Value"]["OutputUUID"], http.native.uid("httpEndpoint"))
        self.assertEqual(post["WFURL"]["WFSerializationType"], "WFTextTokenAttachment")
        body = post["WFJSONValues"]
        self.assertEqual(body["WFSerializationType"], "WFDictionaryFieldValue")
        items = body["Value"]["WFDictionaryFieldValueItems"]
        names = {item["WFKey"]["Value"]["string"]: item for item in items}
        schema = json.loads((BASE / "expense-draft.schema.json").read_text("utf-8"))
        self.assertEqual(set(names), set(schema["properties"]))
        self.assertTrue(set(schema["required"]).issubset(names))
        for name, item in names.items():
            self.assertEqual(item["WFItemType"], 0, name)
            value = item["WFValue"]
            self.assertEqual(value["WFSerializationType"], "WFTextTokenString", name)
            self.assertEqual(len(value["Value"]["attachmentsByRange"]), 1, name)
            ref = next(iter(value["Value"]["attachmentsByRange"].values()))
            self.assertEqual(ref["OutputUUID"], http.native.uid(http.OUTPUTS[name][0]))
            self.assertEqual(ref["OutputName"], http.OUTPUTS[name][1])

    def test_auth_is_dynamic_bearer_never_stored_or_guessed(self):
        post = self.params("httpHttpPost")
        headers = {
            item["WFKey"]["Value"]["string"]: item["WFValue"]
            for item in post["WFHTTPHeaders"]["Value"]["WFDictionaryFieldValueItems"]
        }
        self.assertEqual(set(headers), {"Authorization", "Accept"})
        self.assertEqual(headers["Accept"]["Value"]["string"], "application/json")
        auth = headers["Authorization"]
        self.assertEqual(auth["WFSerializationType"], "WFTextTokenString")
        self.assertEqual(auth["Value"]["string"], "Bearer " + http.native.MARKER)
        self.assertEqual(next(iter(auth["Value"]["attachmentsByRange"].values()))["OutputUUID"],
                         http.native.uid("httpToken"))
        self.assertEqual(self.params("httpToken")["WFAskActionDefaultAnswer"], "")
        self.assertIn("temporal", self.params("httpToken")["WFAskActionPrompt"])
        self.assertEqual(self.params("httpDangerNotice")["WFAlertActionCancelButtonShown"], True)

    def test_no_success_claim_in_lab(self):
        text = json.dumps(self.built, ensure_ascii=False)
        self.assertNotIn("gasto registrado", text.lower())
        self.assertNotIn("gasto enviado correctamente", text.lower())
        self.assertIn("no se crea", text.lower())
        self.assertEqual(self.actions[-1]["WFWorkflowActionIdentifier"],
                         "is.workflow.actions.alert")


if __name__ == "__main__":
    unittest.main()
