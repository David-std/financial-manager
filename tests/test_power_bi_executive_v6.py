"""Power BI Executive v6 layout, accessibility-adjacent and data safety checks.

These validate PBIR source, not rendering or keyboard/accessibility acceptance.
"""
from __future__ import annotations
import json
from pathlib import Path
import unittest
from collections import Counter

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT/"power-bi"/"FinancialManager"
PAGES = PROJECT/"FinancialManager.Report"/"definition"/"pages"
MODEL = PROJECT/"FinancialManager.SemanticModel"/"definition"/"tables"/"FactPostedExpense.tmdl"

def read_page(folder):
    meta=json.loads((folder/"page.json").read_text(encoding="utf-8"))
    items={}
    for path in (folder/"visuals").glob("*/visual.json"):
        v=json.loads(path.read_text(encoding="utf-8"))
        items[v["name"]]=v
    return meta,items

class V6Tests(unittest.TestCase):
    def test_canvas_and_major_visual_size(self):
        found=[]
        heights=[]
        gauge=0
        for folder in PAGES.iterdir():
            if not (folder/"page.json").exists():
                continue
            meta,items=read_page(folder)
            self.assertEqual((meta["width"],meta["height"]),(1280,1630))
            counts=Counter()
            for value in items.values():
                kind=value["visual"]["visualType"]
                counts[kind]+=1
                p=value["position"]
                self.assertGreater(p["width"],0)
                self.assertGreater(p["height"],0)
                self.assertGreaterEqual(p["x"],0)
                self.assertGreaterEqual(p["y"],0)
                self.assertLessEqual(p["x"]+p["width"],1280)
                self.assertLessEqual(p["y"]+p["height"],1630)
                if kind in ("tableEx","pivotTable"):
                    self.assertGreaterEqual(p["height"],380,
                                            "Compressed data surface: "+folder.name)
                    heights.append(p["height"])
                    visual=value["visual"]
                    self.assertIn("columnHeaders",visual.get("objects",{}))
                    self.assertIn("grid",visual.get("objects",{}))
                    self.assertIn("values",visual.get("objects",{}))
                if kind=="gauge":
                    gauge+=1
                    roles=value["visual"]["query"]["queryState"]
                    self.assertIn("MaxValue",roles)
                    self.assertIn("Y",roles)
                if kind=="textbox":
                    self.assertNotIn("onClick",value["visual"])
            self.assertEqual(counts["slicer"],5)
            self.assertGreaterEqual(counts["card"],4)
            self.assertGreaterEqual(sum(v for k,v in counts.items()
                if k not in ("slicer","card","textbox")),6)
            found.append((meta["displayName"],tuple(sorted(counts.items()))))
        self.assertEqual(len(found),4)
        self.assertEqual(len(set(v[1] for v in found)),4)
        self.assertGreaterEqual(gauge,3)
        self.assertGreaterEqual(len(heights),4)

    def test_record_status_uses_filtered_fact_not_untrusted_label(self):
        data=MODEL.read_text(encoding="utf-8")
        self.assertIn("measure 'Estado por movimiento'",data)
        self.assertIn("HASONEVALUE ( FactPostedExpense[ExpenseId] )",data)
        self.assertIn("measure 'Estado de registros'",data)
        self.assertIn('IF ( [Payment Count] > 0',data)
        self.assertIn("measure 'Escala porcentual' = 1",data)
        self.assertIn("CommonDataService.Database",data)
        self.assertNotIn("Table.FromRecords(SyntheticRows)",data)
        self.assertNotIn("DEMO-EXP-",data)

    def test_tables_have_business_friendly_headers(self):
        count=0
        for folder in PAGES.iterdir():
            if not (folder/"page.json").exists():
                continue
            _, items=read_page(folder)
            for v in items.values():
                if v["visual"]["visualType"]!="tableEx":
                    continue
                count+=1
                projs=v["visual"]["query"]["queryState"]["Values"]["projections"]
                names=[p.get("displayName","") for p in projs]
                self.assertIn("Referencia",names)
                self.assertIn("Estado",names)
                self.assertIn("Monto PEN",names)
        self.assertEqual(count,2)

    def test_temporal_matrix_has_month_rows(self):
        folders=list(PAGES.glob("*/visuals/*/visual.json"))
        months=[]
        for file in folders:
            v=json.loads(file.read_text(encoding="utf-8"))
            if v["visual"]["visualType"] != "pivotTable":
                continue
            roles=v["visual"]["query"]["queryState"]
            rowrefs=[p["queryRef"] for p in roles.get("Rows",{}).get("projections",[])]
            colrefs=[p["queryRef"] for p in roles.get("Columns",{}).get("projections",[])]
            if "DimDate.YearMonth" in rowrefs:
                self.assertIn("DimCategory.CategoryName",colrefs)
                self.assertGreaterEqual(v["position"]["height"],400)
                months.append(file)
        self.assertEqual(len(months),1)

    def test_no_real_data_written_and_theme_kept(self):
        script=(ROOT/"scripts/build_power_bi_v6.py").read_text(encoding="utf-8")
        self.assertNotIn("service.Create(",script)
        self.assertNotIn("service.Update(",script)
        self.assertNotIn("fmg_ApproveBudget",script)
        demo=(ROOT/"scripts/create_power_bi_demo.py").read_text(encoding="utf-8")
        self.assertIn("tempfile.mkdtemp",demo)
        self.assertIn("NO_DATA_WRITTEN_TO_DATAVERSE",demo)
        theme=PROJECT/"FinancialManager.Report"/"StaticResources"/"RegisteredResources"/"FinancialManager-Executive-v2.json"
        colors=json.loads(theme.read_text(encoding="utf-8"))
        self.assertEqual(colors["background"],"#F6F6F3")

if __name__=="__main__":
    unittest.main()
