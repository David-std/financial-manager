"""Offline financial workspace: synthetic data, UX contracts and safe write boundaries."""
from __future__ import annotations

import json
import pathlib
import unittest
from collections import Counter

ROOT = pathlib.Path(__file__).resolve().parents[1]
WEB = ROOT / "experiments" / "financial-workspace"


class WorkspacePreviewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fixture = json.loads((WEB / "src/data/demo.json").read_text(encoding="utf8"))
        cls.records = cls.fixture["records"]
        cls.app = (WEB / "src/App.tsx").read_text(encoding="utf8")
        cls.explorer = (WEB / "src/CategoryExplorer.tsx").read_text(encoding="utf8")
        cls.drafts = (WEB / "src/LocalDraftWorkbench.tsx").read_text(encoding="utf8")

    def test_fixture_matches_offline_semantic_model_demo(self):
        self.assertTrue(self.fixture["demo"])
        self.assertEqual(self.fixture["currency"], "PEN")
        self.assertEqual(self.fixture["count"], 310)
        self.assertEqual(len(self.records), 310)
        self.assertEqual(sum(round(r["amount"] * 100) for r in self.records), 50956512)
        self.assertEqual(self.fixture["totalPEN"], 509565.12)
        self.assertEqual(Counter(int(r["date"][:4]) for r in self.records),
                         {2024: 65, 2025: 139, 2026: 106})
        self.assertEqual(len({r["id"] for r in self.records}), 310)
        self.assertTrue(all(r["id"].startswith("DEMO-EXP-") for r in self.records))

    def test_demo_contains_no_real_user_accounts_or_write_integrations(self):
        self.assertFalse(any("@" in str(r) for r in self.records))
        for source in (self.app, self.explorer, self.drafts):
            for prohibited in ("fmg_ApproveBudget", "api/data/v9", "clientSecret",
                               "bearer ", "fetch(", "axios.post", "DataverseClient",
                               "localStorage", "sessionStorage"):
                self.assertNotIn(prohibited, source)
        for record in self.records:
            self.assertEqual(record["status"], "Contabilizado")
            self.assertGreater(record["amount"], 0)

    def test_workflow_boundaries_are_explicit(self):
        self.assertIn("Operaciones deshabilitadas", self.app)
        self.assertIn("Sin acceso a Dataverse", self.app)
        self.assertIn("Simulación local", self.drafts)
        self.assertIn("No se creó ningún registro en Dataverse", self.drafts)
        self.assertIn("setDrafts", self.drafts)
        self.assertNotIn("createRow", self.drafts)
        self.assertNotIn("updateRow", self.drafts)
        self.assertIn("getByRole", (WEB / "smoke.mjs").read_text(encoding="utf8"))

    def test_category_explorer_uses_record_values_and_has_drill_down(self):
        self.assertIn("categoryGroups(records", self.explorer)
        self.assertIn("categoryTreemap(groups)", self.explorer)
        self.assertIn("focusRecords", self.explorer)
        self.assertIn("onOpenTransactions", self.explorer)
        self.assertIn('role="listitem"', self.explorer)
        self.assertIn("aria-label", self.explorer)
        self.assertIn('id: "categorias"', self.app)

    def test_local_draft_editing_is_not_a_posting_operation(self):
        self.assertIn("Guardar borrador local", self.drafts)
        self.assertIn("Descartar", self.drafts)
        self.assertIn("editing", self.drafts)
        self.assertIn("newDraft", self.drafts)
        self.assertIn('LOCAL-', self.drafts)
        self.assertIn("no guarda datos en Dataverse", self.drafts)
        self.assertNotIn('fmg_status', self.drafts)

    def test_documents_separate_actual_fabric_from_prototype(self):
        doc = (WEB / "README.md").read_text(encoding="utf8")
        for required in ("not", "Dataverse", "Fabric Apps", "Rayfin",
                         "prototype", "in-memory"):
            self.assertIn(required.lower(), doc.lower())


if __name__ == "__main__":
    unittest.main()
