"""Source-level guardrails for the Power BI PBIP/PBIR and TMDL project.

These checks are not a substitute for a Desktop import, refresh, visual review or RLS test.
"""
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "power-bi" / "FinancialManager"
REPORT = PROJECT / "FinancialManager.Report"
MODEL = PROJECT / "FinancialManager.SemanticModel"


class PbipContractTests(unittest.TestCase):
    def test_pbip_report_points_to_a_real_semantic_model(self) -> None:
        self.assertTrue((PROJECT / "FinancialManager.pbip").is_file())
        report = json.loads((REPORT / "definition.pbir").read_text(encoding="utf-8"))
        self.assertEqual(
            report["datasetReference"]["byPath"]["path"],
            "../FinancialManager.SemanticModel",
        )
        self.assertTrue((MODEL / "definition.pbism").is_file())
        self.assertTrue((MODEL / "definition" / "database.tmdl").is_file())

    def test_model_is_posted_only_and_has_no_embedded_tenant_or_credentials(self) -> None:
        definition = MODEL / "definition"
        tables = list((definition / "tables").glob("*.tmdl"))
        self.assertEqual(len(tables), 5)
        model = (definition / "model.tmdl").read_text(encoding="utf-8")
        self.assertIn("defaultPowerBIDataSourceVersion: powerBI_V3", model)
        self.assertIn('expression DataverseHost = "SET_DATAVERSE_HOST"', model)
        fact = (definition / "tables" / "FactPostedExpense.tmdl").read_text(
            encoding="utf-8"
        )
        self.assertIn("WHERE expense.fmg_status = 797280001", fact)
        self.assertIn("currency.isocurrencycode = 'PEN'", fact)
        self.assertEqual(fact.count("\n\tmeasure '"), 26)
        self.assertIn("'Pagos confirmados KPI'", fact)
        self.assertIn("'Variación interanual lectura'", fact)
        self.assertIn("column FlowClassLabel", fact)
        self.assertIn("column ScopeLabel", fact)
        for source in definition.rglob("*.tmdl"):
            content = source.read_text(encoding="utf-8")
            self.assertNotIn(".crm3.dynamics.com", content)
            self.assertNotIn("password=", content.lower())
            self.assertNotIn("access_token", content.lower())

    def test_report_pages_are_bounded_and_bind_real_model_fields(self) -> None:
        pages = REPORT / "definition" / "pages"
        listing = json.loads((pages / "pages.json").read_text(encoding="utf-8"))
        self.assertEqual(len(listing["pageOrder"]), 4)
        total_visuals = 0
        for page_id in listing["pageOrder"]:
            page_folder = pages / page_id
            page = json.loads((page_folder / "page.json").read_text(encoding="utf-8"))
            self.assertEqual(page["width"], 1280)
            self.assertEqual(page["height"], 1140)
            for file in (page_folder / "visuals").glob("*/visual.json"):
                visual = json.loads(file.read_text(encoding="utf-8"))
                position = visual["position"]
                self.assertLessEqual(position["x"] + position["width"], 1280)
                self.assertLessEqual(position["y"] + position["height"], 1140)
                if visual["visual"]["visualType"] != "textbox":
                    self.assertTrue(visual["visual"]["query"]["queryState"])
                total_visuals += 1
        self.assertEqual(total_visuals, 83)


if __name__ == "__main__":
    unittest.main()
