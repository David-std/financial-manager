"""Power BI executive-v4 composition, semantic safeguards and offline fixture tests."""
from collections import Counter
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PAGES = ROOT / "power-bi/FinancialManager/FinancialManager.Report/definition/pages"
FACT = ROOT / "power-bi/FinancialManager/FinancialManager.SemanticModel/definition/tables/FactPostedExpense.tmdl"

class ExecutiveV4Tests(unittest.TestCase):
    def test_native_charts_are_different_on_each_page(self):
        signatures = []
        total = 0
        for folder in PAGES.iterdir():
            if not (folder / "page.json").is_file():
                continue
            page = json.loads((folder / "page.json").read_text(encoding="utf-8"))
            self.assertEqual((page["width"], page["height"]), (1280, 1140))
            types = Counter()
            for path in (folder / "visuals").glob("*/visual.json"):
                visual = json.loads(path.read_text(encoding="utf-8"))
                typ = visual["visual"]["visualType"]
                types[typ] += 1
                pos = visual["position"]
                self.assertLessEqual(pos["x"]+pos["width"],1280)
                self.assertLessEqual(pos["y"]+pos["height"],1140)
                self.assertGreaterEqual(pos["x"],0)
                self.assertGreaterEqual(pos["y"],0)
                self.assertNotIn("button",typ.lower())
                total += 1
            self.assertEqual(types["slicer"],5)
            self.assertGreaterEqual(types["card"],4)
            self.assertGreaterEqual(sum(v for k,v in types.items() if k not in ("textbox","slicer","card")),6)
            signatures.append(tuple(sorted(types.items())))
        self.assertEqual(total,83)
        self.assertEqual(len(signatures),4)
        self.assertEqual(len(set(signatures)),4)

    def test_neutral_theme_without_color_edge_kpis(self):
        theme = ROOT / "power-bi/FinancialManager/FinancialManager.Report/StaticResources/RegisteredResources/FinancialManager-Executive-v2.json"
        palette = json.loads(theme.read_text(encoding="utf-8"))
        self.assertEqual(palette["background"],"#F5F6F7")
        self.assertNotIn("#E3A948",palette["dataColors"])
        code=(ROOT/"scripts/build_power_bi_v4.py").read_text(encoding="utf-8")
        self.assertNotIn('nav-rail',code)
        self.assertNotIn('kpi-"+str(i)+"-accent"',code)
        self.assertIn('lineClusteredColumnComboChart',code)
        self.assertIn('Year-over-Year Change', FACT.read_text(encoding="utf-8"))

    def test_time_comparisons_require_selected_year(self):
        fact=FACT.read_text(encoding="utf-8")
        self.assertIn('NOT HASONEVALUE ( DimDate[Year] )', fact)
        self.assertIn('Selecciona un año',fact)
        self.assertIn('VAR LastPaidDate = MAX ( FactPostedExpense[TransactionDate] )',fact)

    def test_synthetic_data_stay_outside_git_and_dataverse(self):
        source=(ROOT/"scripts/create_power_bi_demo.py").read_text(encoding="utf-8")
        self.assertIn("tempfile.mkdtemp",source)
        self.assertIn("SYNTHETIC-",source)
        self.assertIn("NO_DATA_WRITTEN_TO_DATAVERSE",source)
        self.assertNotIn("service.Create(",source)
        self.assertNotIn("service.Update(",source)
        self.assertNotIn("DEMO-EXP-",FACT.read_text(encoding="utf-8"))
        self.assertIn("CommonDataService.Database",FACT.read_text(encoding="utf-8"))

if __name__ == "__main__":
    unittest.main()
