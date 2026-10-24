"""Executive v3 layout and isolated synthetic-data safeguards."""
from collections import Counter
import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
PAGES=ROOT/"power-bi/FinancialManager/FinancialManager.Report/definition/pages"

class ExecutiveV3Tests(unittest.TestCase):
    def test_composition_and_chart_variety(self):
        type_counts=Counter()
        actual=0
        for p in PAGES.iterdir():
            if not (p/"page.json").is_file():
                continue
            page=json.loads((p/"page.json").read_text(encoding="utf-8"))
            self.assertEqual(page["height"],944)
            kinds=Counter()
            for path in (p/"visuals").glob("*/visual.json"):
                raw=json.loads(path.read_text(encoding="utf-8"))
                name=raw["visual"]["visualType"]
                kinds[name]+=1
                type_counts[name]+=1
                actual+=1
                z=raw["position"]
                self.assertGreaterEqual(z["x"],0)
                self.assertGreaterEqual(z["y"],0)
                self.assertLessEqual(z["x"]+z["width"],1280)
                self.assertLessEqual(z["y"]+z["height"],944)
            self.assertGreaterEqual(kinds["slicer"],4)
            self.assertGreaterEqual(kinds["card"],3)
            self.assertGreaterEqual(sum(kinds.values()),30)
        self.assertEqual(actual,153)
        for typ in ("areaChart","clusteredBarChart","donutChart","treemap",
                    "pivotTable","decompositionTreeVisual","clusteredColumnChart",
                    "hundredPercentStackedColumnChart","tableEx"):
            self.assertGreater(type_counts[typ],0,typ)

    def test_synthetic_data_isolated_from_operational_source(self):
        code=(ROOT/"scripts/create_power_bi_demo.py").read_text(encoding="utf-8")
        self.assertIn("tempfile.mkdtemp",code)
        self.assertIn("SYNTHETIC-",code)
        self.assertIn("NO_DATA_WRITTEN_TO_DATAVERSE",code)
        for forbidden in ("service.Create(","service.Update(","service.Delete(",
                          "pac solution import","crm3.dynamics.com"):
            self.assertNotIn(forbidden,code)
        fact=(ROOT/"power-bi/FinancialManager/FinancialManager.SemanticModel/definition/tables/FactPostedExpense.tmdl").read_text(encoding="utf-8")
        self.assertIn("CommonDataService.Database",fact)
        self.assertNotIn("DEMO-EXP-",fact)
        self.assertNotIn("Table.FromRecords(SyntheticRows)",fact)

if __name__=="__main__":
    unittest.main()
