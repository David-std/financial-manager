"""v8 phone and desktop geometry regression tests."""
from pathlib import Path
import hashlib, json, unittest
ROOT=Path(__file__).resolve().parents[1]
PAGES=ROOT/"power-bi/FinancialManager/FinancialManager.Report/definition/pages"
MAP={
"de2b78eaf1b44c2a9485":{
 "rows":[("branch-month","category-breakdown"),("branch-comparison","accounts")],
 "side":["operational-title","operational-state","sidebar-scope","posted-badge-back"]},
"f9be472dc34d4e5cab27":{
 "rows":[("category-time","flow-mix","decomposition"),("category-matrix","analysis-side")]},
"f1a74ce8854f434789d2":{
 "rows":[("main-trend","by-year"),("monthly-volume","types","scope"),
         ("year-month-grid","trends-breakdown")]}}
def read_visual(folder,key):
 ident=hashlib.sha1((folder.name+":"+key).encode()).hexdigest()[:20]
 return json.loads((folder/"visuals"/ident/"visual.json").read_text(encoding="utf-8"))
class V8LayoutTests(unittest.TestCase):
 def test_four_phone_views_with_correct_selection(self):
  total=0
  for folder in PAGES.iterdir():
   if not (folder/"page.json").exists():continue
   pages=[]
   for m in folder.glob("visuals/*/mobile.json"):
    o=json.loads(m.read_text(encoding="utf-8"))
    self.assertIn("visualContainerMobileState",o["$schema"])
    p=o["position"]
    self.assertGreater(p["width"],0)
    self.assertLessEqual(p["x"]+p["width"],323)
    self.assertGreaterEqual(p["y"],0)
    self.assertLessEqual(p["width"],323)
    for prior in pages:
     hit=p["x"]<prior["x"]+prior["width"] and p["x"]+p["width"]>prior["x"] and p["y"]<prior["y"]+prior["height"] and p["y"]+p["height"]>prior["y"]
     self.assertFalse(hit,(folder.name,m.name,p,prior))
    pages.append(p)
   self.assertGreaterEqual(len(pages),16,folder.name)
   self.assertEqual(len(set(p["tabOrder"] for p in pages)),len(pages))
   self.assertTrue(any(p["width"]==323 and p["height"]>=220 for p in pages))
   total+=len(pages)
  self.assertEqual(total,70)
 def test_grid_alignments(self):
  for id,conf in MAP.items():
   folder=PAGES/id
   for row in conf["rows"]:
    ps=[read_visual(folder,key)["position"] for key in row]
    # Right bottom-most trend row deliberately contains a split vertical stack.
    if "year-month-grid" in row:
     self.assertEqual(ps[0]["y"],1113)
     self.assertEqual(ps[1]["y"]+ps[1]["height"],1546)
    else:
     self.assertEqual(len({p["y"] for p in ps}),1,(id,row,ps))
     self.assertEqual(len({p["y"]+p["height"] for p in ps}),1,(id,row,ps))
   if "side" in conf:
    p=[read_visual(folder,key)["position"] for key in conf["side"]]
    for a,b in zip(p,p[1:]):
     self.assertGreaterEqual(b["y"],a["y"]+a["height"])
 def test_phone_is_independent_from_desktop_and_no_operational_mutations(self):
  source=(ROOT/"scripts/build_power_bi_v8.py").read_text(encoding="utf-8")
  self.assertNotIn("service.Create(",source)
  self.assertNotIn("service.Update(",source)
  self.assertNotIn("fmg_ApproveBudget",source)
  self.assertIn("PHONE_WIDTH=323",source)
  self.assertIn("MOBILE_SCHEMA",source)
if __name__=="__main__":unittest.main()
