from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "apps/financial-workspace/src"


class WorkspaceProductDesignTests(unittest.TestCase):
    def test_category_headline_replaced_decorative_overlap(self):
        app = (SRC / "App.tsx").read_text(encoding="utf-8")
        css = (SRC / "styles.css").read_text(encoding="utf-8")
        self.assertIn('hero-compact hero-category', app)
        self.assertIn('hero-category-fact', app)
        self.assertNotIn('className="hero-decoration"', app)
        self.assertIn('.hero-category::after {', css)
        self.assertIn('display: none;', css.split(".hero-category::after {", 1)[1].split("}", 1)[0])

    def test_insights_use_active_filtered_records_not_placeholder_kpis(self):
        app = (SRC / "App.tsx").read_text(encoding="utf-8")
        for marker in (
            'categoryInsights = useMemo',
            'summary.byCategory',
            'filtered.map((item) => item.account)',
            'categoryInsights.months.map',
            'categoryInsights.share.toFixed(1)',
        ):
            self.assertIn(marker, app)
        planner = (SRC / "BudgetPlanner.tsx").read_text(encoding="utf-8")
        for marker in ("plans.flatMap((plan) => plan.lines)", "plannedMonths", "plannedCategories"):
            self.assertIn(marker, planner)
        workflow = (SRC / "WorkflowHub.tsx").read_text(encoding="utf-8")
        self.assertIn("state.events.filter", workflow)
        self.assertIn("items.length - attention.length", workflow)

    def test_provenance_remains_visible_without_old_prototype_badge(self):
        app = (SRC / "App.tsx").read_text(encoding="utf-8")
        self.assertIn("Datos de muestra", app)
        self.assertIn("Sobre los datos de esta vista", app)
        self.assertIn('href="/?mode=live"', app)
        self.assertNotIn("Entorno de ejemplo", app)
        self.assertNotIn('className="hero-decoration"', app)

    def test_live_route_is_opt_in_and_has_real_collection_queries(self):
        entry = (SRC / "main.tsx").read_text(encoding="utf-8")
        self.assertIn('VITE_FM_ENABLE_LIVE_READ === "true"', entry)
        self.assertIn('get("mode") === "live"', entry)
        self.assertIn("<App />", entry)
        live = (SRC / "infrastructure/data/live-read-repository.ts").read_text(encoding="utf-8")
        for entity in (
            "fmg_financialtransactions", "fmg_budgets", "fmg_budgetallocations",
            "fmg_activityevents", "fmg_categories", "fmg_branchs", "fmg_cashaccounts",
        ):
            self.assertIn(entity, live)
        self.assertIn('method: "POST"', live)
        self.assertIn('method: "DELETE"', live)
        self.assertIn('"If-Match": expected.etag', live)
        self.assertNotIn("BypassCustomPluginExecution", live)


if __name__ == "__main__":
    unittest.main()
