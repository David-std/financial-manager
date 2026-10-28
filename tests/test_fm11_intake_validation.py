"""FM-11 offline intake contract regression tests; no Dataverse writes."""
import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FILE = ROOT / "integrations/power-automate/fm11_validation.py"
spec = importlib.util.spec_from_file_location("fm11_validation", FILE)
intake = importlib.util.module_from_spec(spec)
spec.loader.exec_module(intake)

GOOD = {
    "requestId": "FM11-984397852", "name": "Office supplies",
    "amountPen": "112.00", "date": "2026-10-28", "categoryCode": "101",
    "branchCode": "LM", "accountName": "Caja La Merced",
    "flowClass": "operativo", "description": "Synthetic test",
}
PERMISSIONS = {
    "permitted_branches": {"LM"},
    "permitted_accounts": {"Caja La Merced"},
    "active_accounts": {"Caja La Merced"},
    "active_categories": {"101"},
}


class OfflineIntakeContractTests(unittest.TestCase):
    def validate(self, changes=None, **permission_changes):
        data = {**GOOD, **(changes or {})}
        opts = {**PERMISSIONS, **permission_changes}
        return intake.validate(data, **opts)

    def test_valid_candidate_returns_only_review_data(self):
        self.assertEqual(self.validate()["amountPen"], "112.00")
        self.assertEqual(intake.validate_json(json.dumps(GOOD), **PERMISSIONS), GOOD)

    def test_rejects_invalid_amounts(self):
        for amt in ("0", "-1", "0.00", "1,50", "nan", "1e9", "1.234", "a", ""):
            with self.subTest(amt=amt), self.assertRaises(intake.InvalidRequest):
                self.validate({"amountPen": amt})

    def test_rejects_unknown_or_wrong_branch_account(self):
        for account in ("hola", "Caja San Ramón", "", "Cuenta bancaria principal"):
            with self.subTest(account=account), self.assertRaises(intake.InvalidRequest):
                self.validate({"accountName": account})
        with self.assertRaises(intake.InvalidRequest):
            self.validate(permitted_accounts=set())
        with self.assertRaises(intake.InvalidRequest):
            self.validate(active_accounts=set())

    def test_requires_grant_for_shared_account(self):
        changes = {"accountName": "Cuenta bancaria principal"}
        opts = {**PERMISSIONS,
                "permitted_accounts": {"Cuenta bancaria principal"},
                "active_accounts": {"Cuenta bancaria principal"}}
        with self.assertRaises(intake.InvalidRequest):
            intake.validate({**GOOD, **changes}, **opts)
        self.assertEqual(
            intake.validate({**GOOD, **changes}, **{**opts, "shared_grant": True})
            ["accountName"], "Cuenta bancaria principal"
        )

    def test_rejects_bad_date_and_category(self):
        for date in ("2026-02-29", "2026-13-01", "28/10/2026", ""):
            with self.subTest(date=date), self.assertRaises(intake.InvalidRequest):
                self.validate({"date": date})
        for category in ("100", "001", "abc", "9999"):
            with self.subTest(category=category), self.assertRaises(intake.InvalidRequest):
                self.validate({"categoryCode": category})
        with self.assertRaises(intake.InvalidRequest):
            self.validate(active_categories=set())

    def test_rejects_unpermitted_branch_and_bad_name(self):
        with self.assertRaises(intake.InvalidRequest):
            self.validate({"branchCode": "SR"})
        with self.assertRaises(intake.InvalidRequest):
            self.validate({"name": "abc"})
        with self.assertRaises(intake.InvalidRequest):
            self.validate({"flowClass": "ingreso"})

    def test_rejects_bad_envelope_and_extra_privileges(self):
        with self.assertRaises(intake.InvalidRequest):
            intake.validate_json("not-json", **PERMISSIONS)
        with self.assertRaises(intake.InvalidRequest):
            self.validate({"status": "Posted"})
        with self.assertRaises(intake.InvalidRequest):
            self.validate({"requestId": "3"})
        with self.assertRaises(intake.InvalidRequest):
            self.validate({"amountPen": 1})
        with self.assertRaises(intake.InvalidRequest):
            intake.validate({k: v for k, v in GOOD.items() if k != "accountName"}, **PERMISSIONS)

    def test_blueprint_honestly_describes_no_active_flow(self):
        guide = (ROOT / "integrations/power-automate/README.md").read_text(encoding="utf-8")
        self.assertIn("Mail Intake Quarantine (disabled)", guide)
        self.assertIn("does not create", guide)
        self.assertIn("Outlook V3", guide)
        self.assertIn("unique", guide)
        self.assertIn("review", guide)


if __name__ == "__main__":
    unittest.main()
