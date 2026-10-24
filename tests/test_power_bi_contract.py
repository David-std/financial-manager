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
        self.assertIn("WHERE expense.fmg_status = 797280001", source)
        self.assertIn("CreateNavigationProperties = false", source)
        self.assertNotRegex(source, r"(?i)https?://[^\s\"]+crm\d*\.dynamics\.com")
        self.assertNotRegex(source, r"(?i)(?:password|clientsecret|accesstoken)\s*=")

    def test_fact_filters_expenses_to_pen_and_never_embeds_tenant(self) -> None:
        source = (ROOT / "posted-expenses.pq").read_text(encoding="utf-8")
        self.assertIn("INNER JOIN transactioncurrency AS currency", source)
        self.assertIn("currency.isocurrencycode = 'PEN'", source)
        self.assertIn("expense.transactioncurrencyid", source)
        self.assertNotIn("fmg_status = 797280000", source)

    def test_each_dimension_is_a_separate_parameterized_m_query(self) -> None:
        expected = ("DimCategory", "DimBranch", "DimCashAccount", "DimDate")
        for name in expected:
            with self.subTest(dimension=name):
                source = (ROOT / "queries" / (name + ".pq")).read_text(
                    encoding="utf-8-sig"
                )
                self.assertIn("\nlet\n", source)
                self.assertIn("\nin\n", source)
                self.assertNotRegex(
                    source, r"(?i)https?://[^\s\"]+crm\d*\.dynamics\.com"
                )
                if name != "DimDate":
                    self.assertIn("DataverseHost", source)

    def test_dax_avoids_unsupported_budget_and_income_measures(self) -> None:
        source = (ROOT / "measures.dax").read_text(encoding="utf-8")
        self.assertIn("Paid Spending =", source)
        self.assertIn("Shared Spending =", source)
        self.assertIn("ISBLANK ( Previous )", source)
        self.assertNotRegex(source, r"(?m)^(?:Approved Budget|Profit|Revenue)\s*=")

    def test_budget_metrics_are_explicitly_gated(self) -> None:
        contract = (ROOT / "semantic-model.md").read_text(encoding="utf-8")
        self.assertIn("**only after** approval", contract.lower())
        self.assertIn("**not yet tested in a PBIP**", contract)
        self.assertIn("Shared", contract)
        self.assertIn("RLS", contract.upper())


if __name__ == "__main__":
    unittest.main()
