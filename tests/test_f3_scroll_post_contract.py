from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PAGES = ROOT / "power-platform/model-apps/financial-manager"


class F3OperationalRegressionTests(unittest.TestCase):
    def test_model_driven_pages_have_independent_mobile_scroll(self):
        for filename in ("budgets.tsx", "expenses.tsx", "expense-detail.tsx", "master-data.tsx"):
            source = (PAGES / filename).read_text(encoding="utf-8")
            self.assertIn('height: "100%"', source, filename)
            self.assertIn('overflowY: "auto"', source, filename)
            self.assertIn('overflowX: "hidden"', source, filename)
            self.assertIn('overscrollBehaviorY: "contain"', source, filename)
            self.assertIn('WebkitOverflowScrolling: "touch"', source, filename)

    def test_budget_tables_scroll_horizontally_and_are_keyboard_focusable(self):
        source = (PAGES / "budgets.tsx").read_text(encoding="utf-8")
        self.assertIn('tableContainer: { overflowX: "auto"', source)
        self.assertEqual(source.count('tabIndex={0}'), 2)
        self.assertEqual(source.count('role="region"'), 2)

    def test_post_requires_saved_draft_and_independent_readback(self):
        source = (PAGES / "expense-detail.tsx").read_text(encoding="utf-8")
        post = source.split("async function post(): Promise<void> {", 1)[1].split(
            "async function voidPayment(): Promise<void> {", 1
        )[0]
        self.assertIn("editorFingerprint(inputs)", post)
        self.assertIn("draftFingerprint(current) !== draftFingerprint(data.record)", post)
        self.assertIn("select: [...draftReadColumns]", post)
        self.assertNotIn("await saveDraft()", post)
        self.assertIn('await dataApi.updateRow("fmg_financialtransaction", recordId', post)
        self.assertIn("confirmed.fmg_status !== 797280001", post)
        self.assertIn("transitionInProgress.current", post)

    def test_void_remains_reason_required_and_server_checked(self):
        source = (PAGES / "expense-detail.tsx").read_text(encoding="utf-8")
        section = source.split("async function voidPayment(): Promise<void> {", 1)[1]
        self.assertIn("!inputs.voidReason.trim()", section)
        self.assertIn("fmg_status: 797280002", section)
        self.assertIn("fmg_voidreason: inputs.voidReason.trim()", section)
        self.assertIn("voided.fmg_status !== 797280002", section)
        self.assertNotIn("BypassCustomPluginExecution", section)


if __name__ == "__main__":
    unittest.main()
