"""Safety and configuration contracts for the FM-11 Entra browser handoff."""
from pathlib import Path
import importlib.util
import unittest

ROOT=Path(__file__).resolve().parents[1]
HTML=ROOT/"integrations/power-automate/fm11_shortcut_draft.html"
INSTALLER=ROOT/"tools/DataverseDeployment/Fm11ShortcutWebResource.cs"
REG=ROOT/"integrations/ios-shortcuts/register_spa_redirect.py"
SP=importlib.util.spec_from_file_location("fm11_spa_registration",REG)
assert SP and SP.loader
module=importlib.util.module_from_spec(SP)
SP.loader.exec_module(module)

class BrowserHandoffTests(unittest.TestCase):
    def test_redirect_registration_preserves_existing_uris(self):
        current=["https://other.example.org/auth"]
        result=module.merge_redirects(current)
        self.assertEqual(current,["https://other.example.org/auth"])
        self.assertEqual(result,current+[module.CALLBACK])
        self.assertEqual(module.merge_redirects(result),result)
        with self.assertRaises(module.RegistrationError):
            module.merge_redirects(["http://untrusted.example.org"])

    def test_browser_uses_pkce_s256_and_no_persisted_token(self):
        s=HTML.read_text("utf-8")
        for required in ("crypto.subtle.digest(\"SHA-256\"",
                         "code_challenge_method:\"S256\"",
                         "crypto.getRandomValues",
                         "sessionStorage.removeItem(storage)",
                         "location.origin!==origin",
                         'params.get("state")!==pending.state',
                         'history.replaceState(null,"",redirect)',
                         'new TextDecoder("utf-8",{fatal:true})'):
            self.assertIn(required,s)
        self.assertNotIn("localStorage",s)
        self.assertNotIn("client_secret",s)
        self.assertNotIn("code_challenge_method:\"plain\"",s)

    def test_delegated_user_creates_only_draft(self):
        s=HTML.read_text("utf-8")
        for required in ("fmg_mailintakes","fmg_CreateDraftFromMailIntake",
                         "fmg_reviewstate:\"Unverified\"",
                         "fmg_validationstate:\"FormatChecked\"",
                         "ExpectedRowVersion:String(row.versionnumber)",
                         "Authorization\":\"Bearer \"+bearer"):
            self.assertIn(required,s)
        self.assertNotIn("fmg_PostFinancialTransaction",s)

    def test_webresource_deployer_is_solution_bound(self):
        s=INSTALLER.read_text("utf-8")
        self.assertIn('private const string Solution = "FinancialManager"',s)
        self.assertIn('private const string Name = "fmg_shortcut_draft.html"',s)
        self.assertIn("PublishXmlRequest",s)
        self.assertIn("FM11_SHORTCUT_BROWSER_CONTENT_MATCH=true",s)
        self.assertNotIn("fmg_financialtransaction",s)

if __name__=="__main__":
    unittest.main()
