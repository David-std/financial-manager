"""FM-11 browser handoff shortcut contract; device execution is separate."""
import importlib.util
import plistlib
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DIR=ROOT/"integrations/ios-shortcuts"
SPEC=importlib.util.spec_from_file_location("fm11_browser_shortcut",DIR/"build_authenticated_shortcut.py")
assert SPEC and SPEC.loader
generator=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(generator)

class BrowserShortcutTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.document=generator.build()
        cls.actions=cls.document["WFWorkflowActions"]
        cls.by_uid={a["WFWorkflowActionParameters"]["UUID"]:a for a in cls.actions}

    def action(self,label):
        return self.by_uid[generator.native.uid(label)]

    def test_existing_mail_shortcut_untouched(self):
        existing=plistlib.loads((DIR/"Registrar-gasto-v2.unsigned.shortcut").read_bytes())
        self.assertEqual(existing["WFWorkflowName"],"Registrar gasto")
        self.assertEqual(sum(a["WFWorkflowActionIdentifier"]=="is.workflow.actions.sendemail"
                             for a in existing["WFWorkflowActions"]),1)

    def test_native_forms_and_browser_handoff(self):
        self.assertEqual(self.document["WFWorkflowName"],"Registrar gasto (Microsoft)")
        self.assertEqual(len(self.document["WFWorkflowImportQuestions"]),0)
        for label in ("name","amountPen","date","dateISO","categoryChoice",
                      "categoryCode","branchCode","accountName","flowClass",
                      "description","payload","json","safariPayload","safariEntry",
                      "safariLogin"):
            self.assertIn(generator.native.uid(label),self.by_uid)
        self.assertEqual(sum(a["WFWorkflowActionIdentifier"]=="is.workflow.actions.sendemail"
                             for a in self.actions),0)
        self.assertEqual(sum(a["WFWorkflowActionIdentifier"]=="is.workflow.actions.downloadurl"
                             for a in self.actions),0)
        encoded=self.action("safariPayload")["WFWorkflowActionParameters"]
        self.assertEqual(encoded["WFEncodeMode"],"Encode")
        self.assertEqual(encoded["WFBase64LineBreakMode"],"None")
        self.assertEqual(encoded["WFInput"]["Value"]["OutputUUID"],
                         generator.native.uid("json"))
        url_action=self.action("safariEntry")
        self.assertEqual(url_action["WFWorkflowActionIdentifier"],
                         "is.workflow.actions.url")
        url=url_action["WFWorkflowActionParameters"]["WFURLActionURL"]
        self.assertTrue(url["Value"]["string"].startswith(generator.CALLBACK+"#draft="))
        self.assertEqual(len(url["Value"]["attachmentsByRange"]), 1)
        self.assertEqual(self.action("safariLogin")["WFWorkflowActionParameters"]["WFInput"]["Value"],
                         generator.native.reference("safariEntry","URL"))
        self.assertEqual(self.actions[-1]["WFWorkflowActionIdentifier"],
                         "is.workflow.actions.openurl")

    def test_binary_matches_source(self):
        actual=plistlib.loads((DIR/"Registrar-gasto-entra.unsigned.shortcut").read_bytes())
        self.assertEqual(actual,self.document)

if __name__=="__main__":
    unittest.main()
