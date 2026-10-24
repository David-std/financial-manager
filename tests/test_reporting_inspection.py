"""Read-only Dataverse reconciliation contract for the PBIP project."""

from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class ReportingInspectionTests(unittest.TestCase):
    def test_command_is_read_only_and_environment_guarded(self) -> None:
        source = (ROOT / "tools/DataverseDeployment/Program.cs").read_text(encoding="utf-8")
        self.assertIn('mode == "reporting-inspect"', source)
        self.assertIn('mode != "reporting-inspect"', source)
        self.assertIn("ReportingReadiness.Run(client)", source)
        self.assertIn("who.OrganizationId != organizationId", source)

    def test_inspection_agrees_with_posted_pen_import_scope(self) -> None:
        source = (ROOT / "tools/DataverseDeployment/ReportingReadiness.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn('QueryExpression("fmg_financialtransaction")', source)
        self.assertIn('"fmg_status", ConditionOperator.Equal, Posted', source)
        self.assertIn('currency.LinkCriteria.AddCondition(', source)
        self.assertIn('"isocurrencycode", ConditionOperator.Equal, "PEN"', source)
        self.assertIn('REPORTING_READ_ONLY_INSPECTION_COMPLETE', source)
        self.assertNotIn("service.Create(", source)
        self.assertNotIn("service.Update(", source)
        self.assertNotIn("service.Delete(", source)


if __name__ == "__main__":
    unittest.main()
