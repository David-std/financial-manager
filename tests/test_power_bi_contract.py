"""Static safety checks for the Power BI starter contract (not an executed PBIP test)."""

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[1] / "power-bi"


class PowerBiStarterTests(unittest.TestCase):
    def test_query_uses_parameterized_dataverse_without_nonposted_expenses(self) -> None:
        source = (ROOT / "posted-expenses.pq").read_text(encoding="utf-8")
        self.assertIn("CommonDataService.Database(", source)
        self.assertIn("DataverseHost", source)
        self.assertIn("WHERE fmg_status = 797280001", source)
        self.assertIn("CreateNavigationProperties = false", source)
        self.assertNotRegex(source, r"(?i)https?://[^\s\"]+crm\d*\.dynamics\.com")
        self.assertNotRegex(source, r"(?i)(?:password|clientsecret|accesstoken)\s*=")

    def test_budget_metrics_are_explicitly_gated(self) -> None:
        contract = (ROOT / "semantic-model.md").read_text(encoding="utf-8")
        self.assertIn("**only after** approval", contract.lower())
        self.assertIn("**not yet tested in a PBIP**", contract)
        self.assertIn("Shared", contract)
        self.assertIn("RLS", contract.upper())


if __name__ == "__main__":
    unittest.main()
