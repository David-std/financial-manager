from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]

class CashGuardContractTests(unittest.TestCase):
    def test_deny_all_tables_and_mutations_no_bypass(self):
        text = (ROOT / "plugins/FinancialManager.CashIntegrity/F5_BlockDirectCashWrites.cs").read_text(encoding="utf-8")
        for table in ("fmg_cashreceipt", "fmg_cashtransfer", "fmg_cashposting",
                      "fmg_accountopening", "fmg_bankstatement", "fmg_bankreconciliation"):
            self.assertIn(table, text)
        for message in ('"Create"', '"Update"', '"Delete"', '"F5G001:'):
            self.assertIn(message, text)
        self.assertNotIn('unsecureConfiguration ==', text)
        self.assertNotIn('SystemAdministrator', text)

    def test_step_registration_covers_six_tables_three_messages(self):
        deploy = (ROOT / "tools/DataverseDeployment/CashGuardDeployment.cs").read_text(encoding="utf-8")
        for text in ("18 - missing", 'OptionSetValue(10)', 'OptionSetValue(0)', '"F5_Block_"',
                     '"sdkmessagefilter"', '"eventhandler"', 'F5_GUARD_DENIED_ALL_SIX_CREATES'):
            self.assertIn(text, deploy)

    def test_package_validator_and_explicit_write_gate(self):
        program = (ROOT / "tools/DataverseDeployment/Program.cs").read_text(encoding="utf-8")
        for mode in ('cash-guard-inspect', 'cash-guard-register', 'cash-guard-steps', 'cash-guard-probe'):
            self.assertIn(mode, program)
        self.assertIn("Mutation requires explicit --apply", program)
        registration = (ROOT / "tools/DataverseDeployment/CashGuardRegistration.cs").read_text(encoding="utf-8")
        self.assertIn("FM_CASH_PACKAGE_PATH", registration)
        self.assertIn("already exists", registration)
        validator = (ROOT / "scripts/check_cash_plugin_package.py").read_text(encoding="utf-8")
        self.assertIn("F5G001", validator)

if __name__ == "__main__":
    unittest.main()
