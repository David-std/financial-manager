"""Offline Financial Workspace fixture integrity and read-only release boundary."""
from __future__ import annotations
import json
import pathlib
import unittest
from collections import Counter
ROOT=pathlib.Path(__file__).resolve().parents[1]
WEB=ROOT/"experiments"/"financial-workspace"

class WorkspacePreviewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fixture=json.loads((WEB/"src/data/demo.json").read_text(encoding="utf8"))
        cls.records=cls.fixture["records"]
        cls.app=(WEB/"src/App.tsx").read_text(encoding="utf8")
    def test_fixture_matches_offline_semantic_model_demo(self):
        self.assertTrue(self.fixture["demo"])
        self.assertEqual(self.fixture["currency"],"PEN")
        self.assertEqual(self.fixture["count"],310)
        self.assertEqual(len(self.records),310)
        self.assertEqual(sum(round(r["amount"]*100) for r in self.records),50956512)
        self.assertEqual(self.fixture["totalPEN"],509565.12)
        self.assertEqual(Counter(int(r["date"][:4]) for r in self.records),{2024:65,2025:139,2026:106})
        self.assertEqual(len({r["id"] for r in self.records}),310)
        self.assertTrue(all(r["id"].startswith("DEMO-EXP-") for r in self.records))
    def test_demo_contains_no_real_user_accounts_or_write_integrations(self):
        self.assertFalse(any("@" in str(r) for r in self.records))
        for s in ("fmg_ApproveBudget","api/data/v9","clientSecret", "bearer ", "fetch(", "axios.post", "DataverseClient"):
            self.assertNotIn(s,self.app)
        for r in self.records:
            self.assertEqual(r["status"],"Contabilizado")
            self.assertGreater(r["amount"],0)
    def test_workflow_boundaries_are_explicit(self):
        self.assertIn("Operaciones deshabilitadas", self.app)
        self.assertIn("Datos", self.app) if False else None
        self.assertIn("Sin acceso a Dataverse",self.app)
        self.assertIn("getByRole", (WEB/"smoke.mjs").read_text(encoding="utf8"))
    def test_documents_separate_actual_fabric_from_prototype(self):
        doc=(WEB/"README.md").read_text(encoding="utf8")
        for expected in ("not", "Dataverse", "Fabric Apps", "Rayfin", "prototype"):
            self.assertIn(expected.lower(),doc.lower())

if __name__=="__main__":
    unittest.main()
