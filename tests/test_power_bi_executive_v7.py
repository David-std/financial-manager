"""Financial Manager v7: understandable charts, bounds and financial integrity."""
from __future__ import annotations
import json, unittest
from pathlib import Path
from collections import Counter
ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/"power-bi/FinancialManager"
PAGES=PROJECT/"FinancialManager.Report/definition/pages"
FACT=PROJECT/"FinancialManager.SemanticModel/definition/tables/FactPostedExpense.tmdl"

class V7Quality(unittest.TestCase):
    def test_each_page_has_useful_data_density_without_clipped_geometry(self):
        total=0
        layouts=[]
        tablecount=0
        for folder in PAGES.iterdir():
            if not (folder/"page.json").is_file():continue
            meta=json.loads((folder/"page.json").read_text(encoding="utf-8"))
            self.assertEqual((meta["width"],meta["height"]),(1280,1630))
            types=Counter()
            titles=[]
            for path in (folder/"visuals").glob("*/visual.json"):
                item=json.loads(path.read_text(encoding="utf-8"))
                typ=item["visual"]["visualType"]
                types[typ]+=1;total+=1
                pos=item["position"]
                self.assertGreater(pos["width"],0)
                self.assertGreater(pos["height"],0)
                self.assertGreaterEqual(pos["x"],0)
                self.assertGreaterEqual(pos["y"],0)
                self.assertLessEqual(pos["x"]+pos["width"],1280)
                self.assertLessEqual(pos["y"]+pos["height"],1630)
                if typ in ("tableEx","pivotTable"):
                    tablecount+=1
                    self.assertGreaterEqual(pos["height"],175)
                    if pos["height"]>=350:self.assertGreaterEqual(pos["width"],300)
                if typ not in ("textbox","shape"):
                    self.assertTrue(item["visual"]["query"]["queryState"])
                title=item["visual"].get("visualContainerObjects",{}).get("title",[])
                titles.append(str(title))
            self.assertEqual(types["slicer"],5)
            self.assertGreaterEqual(types["card"],4)
            self.assertGreaterEqual(sum(v for k,v in types.items() if k not in ("textbox","shape","slicer","card")),6)
            layouts.append(tuple(sorted(types.items())))
        self.assertEqual(len(layouts),4)
        self.assertEqual(len(set(layouts)),4)
        self.assertGreaterEqual(tablecount,8)
        self.assertLess(total,175)

    def test_before_year_ambiguity_resolved(self):
        content=FACT.read_text(encoding="utf-8")
        self.assertIn("measure 'Año previo comparable'",content)
        self.assertIn("HASONEVALUE ( DimDate[Year] )",content)
        self.assertIn("measure 'Participación por ámbito'",content)
        self.assertIn("ALLSELECTED ( FactPostedExpense[ScopeLabel] )",content)

    def test_every_page_has_truthful_business_fields_and_no_fake_map(self):
        alljson="".join(p.read_text(encoding="utf-8") for p in PAGES.glob("*/visuals/*/visual.json"))
        self.assertNotIn('"visualType": "donutChart"',alljson)
        self.assertNotIn('"visualType": "decompositionTreeVisual"',alljson)
        self.assertNotIn('"visualType": "map"',alljson)
        self.assertIn('"visualType": "hundredPercentStackedColumnChart"',alljson)
        self.assertIn('"visualType": "pivotTable"',alljson)
        self.assertIn('"visualType": "tableEx"',alljson)
        self.assertNotIn("DEMO-EXP-",FACT.read_text(encoding="utf-8"))
        self.assertIn("CommonDataService.Database",FACT.read_text(encoding="utf-8"))

    def test_generator_stays_off_dataverse(self):
        code=(ROOT/"scripts/build_power_bi_v7.py").read_text(encoding="utf-8")
        for needle in ("service.Create(","service.Update(","service.Delete(","fmg_ApproveBudget","pac solution import"):
            self.assertNotIn(needle,code)
        sample=(ROOT/"scripts/create_power_bi_demo.py").read_text(encoding="utf-8")
        self.assertIn("tempfile.mkdtemp",sample)
        self.assertIn("NO_DATA_WRITTEN_TO_DATAVERSE",sample)
if __name__=="__main__":unittest.main()
