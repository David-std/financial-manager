"""FM-11 Shortcut source and Apple action-wiring regression tests."""
import importlib.util
import json
import plistlib
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "integrations/ios-shortcuts"
SCHEMA = BASE / "expense-draft.schema.json"
PLIST = BASE / "Registrar-gasto.unsigned.plist"
BINARY = BASE / "Registrar-gasto.unsigned.shortcut"

spec = importlib.util.spec_from_file_location("fm11_native", BASE / "build_shortcut.py")
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


class NativeShortcutTests(unittest.TestCase):
    def setUp(self):
        self.schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
        self.workflow = plistlib.loads(PLIST.read_bytes())
        self.actions = self.workflow["WFWorkflowActions"]
        labels = (
            "recipient", "randomPart", "requestId", "name", "amountPen",
            "invalidAmountStart", "invalidAmountAlert", "invalidAmountExit", "invalidAmountEnd",
            "date", "dateISO", "categoryChoiceList", "categoryChoice",
            "categoryCode", "branchCodeList", "branchCode", "accountNameList", "accountName",
            "flowClassList", "flowClass", "description", "confirm",
            "payload", "json", "mail",
        )
        self.by_label = {
            label: next(
                x for x in self.actions
                if x["WFWorkflowActionParameters"]["UUID"] == native.uid(label)
            )
            for label in labels
        }

    def params(self, label):
        return self.by_label[label]["WFWorkflowActionParameters"]

    def token_refs(self, token):
        self.assertEqual(token["WFSerializationType"], "WFTextTokenString")
        value = token["Value"]
        txt = value["string"]
        refs = value["attachmentsByRange"]
        offsets = []
        for pos, char in enumerate(txt):
            if char == native.MARKER:
                offsets.append(len(txt[:pos].encode("utf-16-le")) // 2)
        actual = [int(re.fullmatch(r"\{(\d+), 1\}", key).group(1)) for key in refs]
        self.assertEqual(sorted(actual), sorted(offsets))
        return list(refs.values())

    def test_source_matches_generator_and_both_plist_formats(self):
        self.assertEqual(self.workflow, native.build())
        self.assertEqual(self.workflow["WFWorkflowName"], "Registrar gasto")
        self.assertEqual(len(self.actions), 25)
        names = [
            a["WFWorkflowActionIdentifier"].removeprefix("is.workflow.actions.")
            for a in self.actions
        ]
        self.assertEqual(names, [
            "gettext", "number.random", "gettext", "ask", "ask",
            "conditional", "alert", "exit", "conditional", "ask", "format.date",
            "list", "choosefromlist", "text.replace", "list", "choosefromlist",
            "list", "choosefromlist", "list", "choosefromlist", "ask", "alert",
            "dictionary", "gettext", "sendemail",
        ])
        self.assertEqual(
            PLIST.read_bytes(), plistlib.dumps(native.build(), fmt=plistlib.FMT_XML)
        )
        self.assertTrue(BINARY.read_bytes().startswith(b"bplist00"))
        self.assertEqual(BINARY.read_bytes(),
                         plistlib.dumps(native.build(), fmt=plistlib.FMT_BINARY))

    def test_request_id_is_non_negative_text(self):
        self.assertLessEqual(native.RANDOM_MAX, (2 ** 31) - 1)
        self.assertEqual(self.params("randomPart")["WFRandomNumberMinimum"], native.RANDOM_MIN)
        self.assertEqual(self.params("randomPart")["WFRandomNumberMaximum"], native.RANDOM_MAX)
        actual = self.params("requestId")["WFTextActionText"]
        self.assertEqual(actual["Value"]["string"], "FM11-" + native.MARKER)
        self.assertEqual(
            self.token_refs(actual),
            [native.reference("randomPart", "Random Number")]
        )
        self.assertGreaterEqual(len(f"FM11-{native.RANDOM_MIN}"),
                                self.schema["properties"]["requestId"]["minLength"])

    def test_every_choice_has_a_real_list_action_and_visible_prompt(self):
        options = {
            "categoryChoice": native.category_options(),
            "branchCode": self.schema["properties"]["branchCode"]["enum"],
            "accountName": native.ACCOUNT_OPTIONS,
            "flowClass": self.schema["properties"]["flowClass"]["enum"],
        }
        for label, expected in options.items():
            with self.subTest(label=label):
                list_label = label + "List"
                list_action = self.by_label[list_label]
                choose_action = self.by_label[label]
                self.assertEqual(list_action["WFWorkflowActionIdentifier"],
                                 "is.workflow.actions.list")
                self.assertEqual(self.params(list_label)["WFItems"], expected)
                self.assertTrue(all(isinstance(v, str) and v.strip() for v in expected))
                self.assertEqual(choose_action["WFWorkflowActionIdentifier"],
                                 "is.workflow.actions.choosefromlist")
                choose = self.params(label)
                self.assertTrue(choose["WFChooseFromListActionPrompt"].strip())
                self.assertFalse(choose["WFChooseFromListActionSelectMultiple"])
                self.assertEqual(
                    choose["WFInput"], native.attachment(list_label, "List")
                )
                self.assertNotEqual(choose["WFInput"]["WFSerializationType"],
                                    "WFArrayParameterState")
                self.assertLess(self.actions.index(list_action),
                                self.actions.index(choose_action))
        self.assertIn("202 · Papelería y Útiles", options["categoryChoice"])
        self.assertTrue(all(re.match(r"^[0-9]{3} · ", v)
                            for v in options["categoryChoice"]))
        self.assertTrue(all(not v[:3].endswith("00") for v in options["categoryChoice"]))

    def test_category_code_has_explicit_regex_input(self):
        p = self.params("categoryCode")
        self.assertEqual(self.by_label["categoryCode"]["WFWorkflowActionIdentifier"],
                         "is.workflow.actions.text.replace")
        self.assertNotIn("text", p)
        self.assertEqual(p["WFReplaceTextFind"], "^([0-9]{3}).*$")
        self.assertEqual(p["WFReplaceTextReplace"], "$1")
        self.assertTrue(p["WFReplaceTextRegularExpression"])
        self.assertEqual(p["WFInput"],
                         native.attachment("categoryChoice", "Selected Item"))
        for option in native.category_options():
            self.assertEqual(re.sub(p["WFReplaceTextFind"], r"\1", option),
                             option[:3])
        self.assertEqual(set(self.schema["properties"]["categoryCode"]),
                         {"type", "pattern"})

    def test_date_is_formatted_and_amount_is_direct_input(self):
        self.assertEqual(self.params("date")["WFInputType"], "Date")
        self.assertEqual(self.params("date")["WFAskActionDateGranularity"], "Date")
        p = self.params("dateISO")
        self.assertEqual(p["WFDateFormatStyle"], "Custom")
        self.assertEqual(p["WFDateFormat"], "yyyy-MM-dd")
        self.assertEqual(p["WFTimeFormatStyle"], "None")
        self.assertNotIn("WFDateFormatString", p)
        self.assertEqual(self.token_refs(p["WFDate"]),
                         [native.reference("date", "Provided Input")])
        self.assertEqual(self.params("amountPen")["WFInputType"], "Number")
        self.assertNotIn("amountNormalized", str(self.workflow))
        for key in ("name", "amountPen", "date", "description"):
            self.assertTrue(self.params(key)["WFAskActionPrompt"].strip())

    def test_invalid_nonpositive_amount_is_blocked_before_send(self):
        start = self.params("invalidAmountStart")
        end = self.params("invalidAmountEnd")
        self.assertEqual(start["WFControlFlowMode"], 0)
        self.assertEqual(end["WFControlFlowMode"], 2)
        self.assertEqual(start["GroupingIdentifier"], end["GroupingIdentifier"])
        self.assertEqual(start["WFCondition"], 1)
        self.assertEqual(start["WFNumberValue"], "0")
        self.assertEqual(start["WFInput"], {
            "Type": "Variable",
            "Variable": native.attachment("amountPen", "Provided Input"),
        })
        self.assertEqual(
            self.by_label["invalidAmountExit"]["WFWorkflowActionIdentifier"],
            "is.workflow.actions.exit",
        )
        order = list(self.by_label)
        self.assertLess(self.actions.index(self.by_label["invalidAmountStart"]),
                        self.actions.index(self.by_label["invalidAmountExit"]))
        self.assertLess(self.actions.index(self.by_label["invalidAmountEnd"]),
                        self.actions.index(self.by_label["mail"]))

    def test_account_options_match_readonly_dev_snapshot(self):
        self.assertEqual(native.ACCOUNT_OPTIONS, [
            "Caja La Merced", "Caja San Ramón", "Cuenta bancaria principal"
        ])
        self.assertNotIn("hola", native.ACCOUNT_OPTIONS)
        self.assertIn("Selecciona", self.params("accountName")["WFChooseFromListActionPrompt"])

    def test_json_schema_and_dictionary_references_are_complete(self):
        keys = set(self.schema["properties"])
        self.assertEqual(keys, {
            "requestId", "name", "amountPen", "date", "categoryCode",
            "branchCode", "accountName", "flowClass", "description"
        })
        self.assertFalse(self.schema["additionalProperties"])
        self.assertEqual(set(self.schema["required"]), keys - {"description"})
        fields = self.params("payload")["WFItems"]["Value"]["WFDictionaryFieldValueItems"]
        self.assertEqual({x["WFKey"]["Value"]["string"] for x in fields}, keys)
        self.assertTrue(all(x["WFItemType"] == 0 for x in fields))
        mapped = {x["WFKey"]["Value"]["string"]: self.token_refs(x["WFValue"])[0]
                  for x in fields}
        self.assertEqual(mapped["amountPen"], native.reference("amountPen", "Provided Input"))
        self.assertEqual(mapped["date"], native.reference("dateISO", "Formatted Date"))
        self.assertEqual(mapped["categoryCode"], native.reference("categoryCode", "Updated Text"))
        self.assertEqual(mapped["branchCode"], native.reference("branchCode", "Selected Item"))
        self.assertEqual(mapped["flowClass"], native.reference("flowClass", "Selected Item"))
        self.assertEqual(mapped["accountName"], native.reference("accountName", "Selected Item"))
        self.assertNotIn("recipient", mapped)

    def test_alert_shows_each_required_field_before_email(self):
        p = self.params("confirm")
        self.assertTrue(p["WFAlertActionCancelButtonShown"])
        message = p["WFAlertActionMessage"]
        self.assertIn("Concepto:", message["Value"]["string"])
        self.assertIn("PEN:", message["Value"]["string"])
        self.assertIn("Fecha:", message["Value"]["string"])
        self.assertIn("Sede:", message["Value"]["string"])
        self.assertIn("Categoría:", message["Value"]["string"])
        refs = self.token_refs(message)
        for label, output in (
            ("amountPen", "Provided Input"),
            ("dateISO", "Formatted Date"),
            ("branchCode", "Selected Item"),
            ("categoryChoice", "Selected Item"),
            ("flowClass", "Selected Item"),
            ("accountName", "Selected Item"),
        ):
            self.assertIn(native.reference(label, output), refs)

    def test_every_action_reference_points_backward_and_tokens_are_valid(self):
        indices = {a["WFWorkflowActionParameters"]["UUID"]: i
                   for i, a in enumerate(self.actions)}
        self.assertEqual(len(indices), len(self.actions))

        def walk(node, index):
            if isinstance(node, dict):
                if node.get("WFSerializationType") == "WFTextTokenString":
                    self.token_refs(node)
                if node.get("Type") == "ActionOutput":
                    self.assertIn(node["OutputUUID"], indices)
                    self.assertLess(indices[node["OutputUUID"]], index)
                for child in node.values():
                    walk(child, index)
            elif isinstance(node, list):
                for child in node:
                    walk(child, index)

        for i, item in enumerate(self.actions):
            walk(item["WFWorkflowActionParameters"], i)

    def test_mail_is_reviewable_and_json_linked(self):
        self.assertEqual(
            self.token_refs(self.params("json")["WFTextActionText"]),
            [native.reference("payload", "Dictionary")]
        )
        mail = self.params("mail")
        self.assertTrue(mail["WFSendEmailActionShowComposeSheet"])
        self.assertEqual(mail["WFSendEmailActionSubject"], "FM11-DRAFT-V1")
        self.assertEqual(mail["WFSendEmailActionInputAttachments"]["Value"],
                         native.reference("json", "Text"))
        self.assertEqual(mail["WFSendEmailActionToRecipients"]["Value"],
                         native.reference("recipient", "Text"))

    def test_recipient_is_import_parameter_not_data(self):
        self.assertEqual(self.workflow["WFWorkflowImportQuestions"], [{
            "ActionIndex": 0, "Category": "Parameter",
            "ParameterKey": "WFTextActionText",
            "Text": "Correo de recepción del flujo de Power Automate",
        }])

    def test_docs_utf8_and_no_gateway(self):
        guide = (BASE / "README.md").read_text(encoding="utf-8")
        self.assertIn("shortcuts sign", guide)
        self.assertIn("Power Automate", guide)
        self.assertIn("A successful Mail", guide)
        self.assertIn("Power Automate", guide)
        root_guide = (ROOT / "README.md").read_text(encoding="utf-8")
        self.assertIn("docs/images/fm11-shortcut-install.webp", root_guide)
        image = ROOT / "docs/images/fm11-shortcut-install.webp"
        self.assertTrue(image.is_file())
        self.assertTrue(image.read_bytes().startswith(b"RIFF"))
        for p in (BASE / "README.md", ROOT / "README.md", BASE / "build_shortcut.py"):
            self.assertNotIn("\ufffd", p.read_text(encoding="utf-8"))
        for bad in ("gateway.mjs", "web-msal.mjs", "Caddyfile.example",
                    "windows-fm11.ps1", "ios-helper"):
            self.assertFalse((BASE / bad).exists())


if __name__ == "__main__":
    unittest.main()
