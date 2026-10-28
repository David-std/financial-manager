"""F6 pinned external dependencies do not disappear by deleting XML nodes."""
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from scripts.f6_dependencies import inspect, SOURCE

class DependencyAuditTests(unittest.TestCase):
    def test_checked_in_export_has_exact_components(self):
        report = inspect(SOURCE)
        self.assertTrue(report.valid, report.errors)
        self.assertEqual(len(report.active), 0)
        self.assertEqual(len(report.microsoft), 2)

    def test_missing_source_does_not_pass(self):
        with TemporaryDirectory() as directory:
            self.assertFalse(inspect(Path(directory)).valid)

    def test_unowned_ux_reference_fails_closed(self):
        with TemporaryDirectory() as directory:
            from shutil import copytree
            dest = Path(directory) / "src"
            dest.mkdir()
            (dest / "Other").mkdir()
            (dest / "AppModules" / "financial_manager_bee9f134").mkdir(parents=True)
            import shutil
            shutil.copytree(SOURCE / "uxagentprojects", dest / "uxagentprojects")
            shutil.copy2(SOURCE / "Other/Solution.xml", dest / "Other/Solution.xml")
            shutil.copy2(SOURCE / "AppModules/financial_manager_bee9f134/AppModule.xml",
                         dest / "AppModules/financial_manager_bee9f134/AppModule.xml")
            p = dest / "Other/Solution.xml"
            s = p.read_text(encoding="utf-8")
            self.assertIn("</MissingDependencies>", s)
            malformed = (
                '<MissingDependency><Required type="uxagentproject" '
                'solution="Unknown" id="{1a6e3c3e-fb2e-43b0-b5dd-9071483d1a1f}"/>'
                '<Dependent type="62" schemaName="financial_manager_bee9f134"/>'
                '</MissingDependency>'
            )
            p.write_text(s.replace("</MissingDependencies>", malformed +
                                  "</MissingDependencies>", 1), encoding="utf-8")
            self.assertFalse(inspect(dest).valid)
