"""Financial Manager v9 mobile UX regression checks (67 mobile visuals)."""
from __future__ import annotations
import hashlib,json,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PAGES=ROOT/"power-bi/FinancialManager/FinancialManager.Report/definition/pages"
EXPECTED={
 "Resumen de gastos":19,
 "Sedes y movimientos":16,
 "Análisis de categorías":15,
 "Tendencias de pagos":17,
}
W=323
def keypath(page,key):
 return page/"visuals"/hashlib.sha1(f"{page.name}:{key}".encode()).hexdigest()[:20]
class PhoneUXTests(unittest.TestCase):
 def test_all_four_pages_and_no_mobile_overlaps(self):
  seen={}
  for page in PAGES.iterdir():
   if not (page/"page.json").exists():continue
   title=json.loads((page/"page.json").read_text(encoding="utf-8"))["displayName"]
   self.assertIn(title,EXPECTED)
   ms=[json.loads(f.read_text(encoding="utf-8")) for f in page.glob("visuals/*/mobile.json")]
   self.assertEqual(len(ms),EXPECTED[title])
   heading=json.loads((keypath(page,"heading")/"mobile.json").read_text(encoding="utf-8"))
   for paragraph in heading["objects"]["general"][0]["properties"]["paragraphs"]:
    for run in paragraph.get("textRuns",[]):
     self.assertEqual(run["textStyle"]["fontSize"],"16pt")
   self.assertEqual(len({m["position"]["tabOrder"] for m in ms}),len(ms))
   primary=json.loads((keypath(page,"kpi-0")/"mobile.json").read_text(encoding="utf-8"))["position"]
   self.assertEqual(primary["width"],W)
   self.assertEqual(primary["x"],0)
   for i in range(5):
    self.assertTrue((keypath(page,f"filter-{i}")/"mobile.json").is_file())
   self.assertEqual(json.loads((keypath(page,"filter-4")/"mobile.json").read_text(encoding="utf-8"))["position"]["width"],W)
   for i,a in enumerate(ms):
    p=a["position"]
    self.assertGreaterEqual(p["y"],0)
    self.assertGreaterEqual(p["height"],73)
    self.assertLessEqual(p["x"]+p["width"],W)
    for b in ms[i+1:]:
     q=b["position"]
     clash=p["x"]<q["x"]+q["width"] and p["x"]+p["width"]>q["x"] and p["y"]<q["y"]+q["height"] and p["y"]+p["height"]>q["y"]
     self.assertFalse(clash,(title,p,q))
   seen[title]=len(ms)
  self.assertEqual(seen,EXPECTED)
 def test_mobile_specific_formatting_is_present(self):
  for page in PAGES.iterdir():
   if not (page/"page.json").exists():continue
   ms=list(page.glob("visuals/*/mobile.json"))
   for f in ms:
    o=json.loads(f.read_text(encoding="utf-8"))
    self.assertIn("visualContainerMobileState",o["$schema"])
    self.assertTrue(o["position"]["tabOrder"]>=0)
    if "visualContainerObjects" in o:
     self.assertIn("general",o["visualContainerObjects"],f.name)
     self.assertIn("altText",o["visualContainerObjects"]["general"][0]["properties"])
    self.assertEqual(json.loads((f.parent/"visual.json").read_text(encoding="utf-8"))["name"],f.parent.name)
 def test_main_reading_order_and_alternative_for_wide_matrices(self):
  for page in PAGES.iterdir():
   if not (page/"page.json").exists():continue
   heading=json.loads((keypath(page,"heading")/"mobile.json").read_text(encoding="utf-8"))["position"]
   kpi=json.loads((keypath(page,"kpi-0")/"mobile.json").read_text(encoding="utf-8"))["position"]
   self.assertLess(heading["y"],kpi["y"])
   # Fit critical charts on portrait; 9+ category charts need additional height.
   for key in ("rank","category-rank","category-breakdown"):
    path=keypath(page,key)/"mobile.json"
    if path.exists():
     pos=json.loads(path.read_text(encoding="utf-8"))["position"]
     self.assertGreaterEqual(pos["height"],335)
   if json.loads((page/"page.json").read_text(encoding="utf-8"))["displayName"]=="Análisis de categorías":
    self.assertFalse((keypath(page,"category-matrix")/"mobile.json").exists())
   if json.loads((page/"page.json").read_text(encoding="utf-8"))["displayName"]=="Tendencias de pagos":
    self.assertFalse((keypath(page,"year-month-grid")/"mobile.json").exists())
 def test_desktop_source_is_not_rebuilt(self):
  code=(ROOT/"scripts/polish_power_bi_mobile_v9.py").read_text(encoding="utf-8")
  for banned in ("service.Create(", "service.Update(", "service.Delete(", "fmg_ApproveBudget", "board.save(", "visual.json\").write_text(", "git push"):
   self.assertNotIn(banned,code)
  self.assertIn('for f in page.glob("visuals/*/mobile.json"):f.unlink()',code)
if __name__=="__main__":unittest.main()
