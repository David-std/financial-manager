"""Synthetic workspace contracts: finances, temporary workflow state, no live writes."""
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
        cls.sources = {
            item.name: item.read_text(encoding="utf8")
            for item in (WEB / "src").glob("*.tsx")
        }
        cls.app = cls.sources["App.tsx"]
        cls.store = cls.sources["WorkspaceStore.tsx"]
        cls.explorer = cls.sources["CategoryExplorer.tsx"]
        cls.drafts = cls.sources["LocalDraftWorkbench.tsx"]
        cls.planner = cls.sources["BudgetPlanner.tsx"]
        cls.activity = cls.sources["WorkflowHub.tsx"]

    def test_fixture_matches_financial_reporting_demo(self):
        self.assertTrue(self.fixture["demo"])
        self.assertEqual(self.fixture["currency"], "PEN")
        self.assertEqual(self.fixture["count"], 310)
        self.assertEqual(len(self.records), 310)
        self.assertEqual(sum(round(r["amount"] * 100) for r in self.records), 50956512)
        self.assertEqual(self.fixture["totalPEN"], 509565.12)
        self.assertEqual(Counter(int(r["date"][:4]) for r in self.records),
                         {2024: 65, 2025: 139, 2026: 106})
        self.assertEqual(len({r["id"] for r in self.records}), 310)

    def test_no_production_or_storage_apis_in_frontend(self):
        self.assertFalse(any("@" in str(r) for r in self.records))
        for filename, source in self.sources.items():
            for prohibited in ("fmg_ApproveBudget", "api/data/v9", "clientSecret",
                               "bearer ", "fetch(", "axios.post", "DataverseClient",
                               "localStorage", "sessionStorage"):
                self.assertNotIn(prohibited, source, filename)
        self.assertIn("NO browser storage", self.store)
        self.assertIn("Entorno de ejemplo", self.app)
        for record in self.records:
            self.assertEqual(record["status"], "Contabilizado")
            self.assertGreater(record["amount"], 0)

    def test_session_state_tracks_real_local_edits_only(self):
        self.assertIn("WorkspaceProvider", self.app)
        self.assertIn("createContext", self.store)
        self.assertIn("useReducer(reduce,initial)", self.store)
        self.assertIn("events:[newEvent,...state.events]", self.store)
        self.assertIn('type:"saveExpense"', self.drafts)
        self.assertIn('type:"deleteExpense"', self.drafts)
        self.assertIn('type:"saveBudget"', self.planner)
        self.assertIn('type:"deleteBudget"', self.planner)

    def test_expense_drafts_validate_and_cannot_post(self):
        self.assertIn("cleanAmount(fields.amount)", self.drafts)
        self.assertIn("Introduce un importe positivo", self.drafts)
        self.assertIn("Borradores de movimientos", self.drafts)
        self.assertIn("Guardar borrador", self.drafts)
        self.assertNotIn("fmg_status", self.drafts)
        self.assertNotIn("updateRow", self.drafts)

    def test_budget_monthly_allocations_are_not_approved_versions(self):
        for expected in ("Calendario de asignaciones", "Guardar presupuesto",
                         "No repitas la misma categoría", "year", "scope", "lines"):
            self.assertIn(expected, self.planner)
        self.assertNotIn("ApproveBudget", self.planner)
        self.assertIn("identidad autorizada", self.planner)

    def test_category_explorer_uses_fixture_payments(self):
        self.assertIn("categoryGroups(records", self.explorer)
        self.assertIn("categoryTreemap(groups)", self.explorer)
        self.assertIn("focusRecords", self.explorer)
        self.assertIn("onOpenTransactions", self.explorer)
        self.assertIn('role="listitem"', self.explorer)

    def test_work_queue_derived_from_session_and_has_activity(self):
        self.assertIn("state.expenses.map", self.activity)
        self.assertIn("state.budgets.map", self.activity)
        self.assertIn("state.events", self.activity)
        self.assertIn("Historial de actividad", self.activity)
        self.assertIn("auditoría oficial", self.activity.lower())

    def test_accessibility_and_environment_explained_once(self):
        self.assertIn("aria-label={label}", self.app)
        self.assertIn("environment-indicator", self.app)
        self.assertIn("Sin registros pendientes", self.activity)
        self.assertIn("getByRole", (WEB / "smoke.mjs").read_text(encoding="utf8"))
        doc = (WEB / "README.md").read_text(encoding="utf8").lower()
        for required in ("dataverse", "fabric apps", "preview", "session"):
            self.assertIn(required, doc)


if __name__ == "__main__":
    unittest.main()
