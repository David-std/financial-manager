"""V5 editorial-layout and safety regression checks."""
from collections import Counter
import json
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
P=ROOT/"power-bi/FinancialManager/FinancialManager.Report/definition/pages"
FACT=ROOT/"power-bi/FinancialManager/FinancialManager.SemanticModel/definition/tables/FactPostedExpense.tmdl"
class V5Tests(unittest.TestCase):
    def test_four_layouts_distinct_and_bounded(self):
        sigs=[]
        total=0
        for p in P.iterdir():
            if not (p/"page.json").exists(): continue
            meta=json.loads((p/"page.json").read_text(encoding="utf8"))
            self.assertEqual((meta["width"],meta["height"]),(1280,1160))
            types=Counter()
            for f in (p/"visuals").glob("*/visual.json"):
                v=json.loads(f.read_text(encoding="utf8"))
                k=v["visual"]["visualType"];types[k]+=1;total+=1
                xy=v["position"]
                self.assertGreaterEqual(xy["x"],0)
                self.assertGreaterEqual(xy["y"],0)
                self.assertLessEqual(xy["x"]+xy["width"],1280)
                self.assertLessEqual(xy["y"]+xy["height"],1160)
                self.assertNotIn("button",k.lower())
            self.assertEqual(types["slicer"],5)
            self.assertGreaterEqual(types["card"],4)
            self.assertGreaterEqual(sum(n for k,n in types.items() if k not in ("textbox","slicer","card")),6)
            sigs.append(tuple(sorted(types.items())))
        self.assertEqual(total,126)
        self.assertEqual(len(sigs),4)
        self.assertEqual(len(set(sigs)),4)
    def test_editorial_theme_and_real_explorations(self):
        theme=ROOT/"power-bi/FinancialManager/FinancialManager.Report/StaticResources/RegisteredResources/FinancialManager-Executive-v2.json"
        data=json.loads(theme.read_text(encoding="utf8"))
        self.assertEqual(data["background"],"#F6F6F3")
        self.assertEqual(len(data["dataColors"]),8)
        src=(ROOT/"scripts/build_power_bi_v5.py").read_text(encoding="utf8")
        self.assertIn('decompositionTreeVisual',src)
        self.assertIn('scope-ring',src)
        self.assertIn('year-month-grid',src)
        self.assertNotIn('nav-rail',src)
        self.assertNotIn('fmg_ApproveBudget',src)
    def test_operational_data_unmodified(self):
        source=FACT.read_text(encoding="utf8")
        self.assertIn("CommonDataService.Database",source)
        self.assertNotIn("DEMO-EXP-",source)
        self.assertNotIn("Table.FromRecords(SyntheticRows)",source)
        demo=(ROOT/"scripts/create_power_bi_demo.py").read_text(encoding="utf8")
        self.assertIn("tempfile.mkdtemp",demo)
        self.assertIn("NO_DATA_WRITTEN_TO_DATAVERSE",demo)
if __name__=="__main__":unittest.main()
