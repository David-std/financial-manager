"""Tests for the F6 ALM no-release drift gate."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from scripts.f6_alm_preflight import REQUIRED_APIS, inspect, SOURCE


class F6ALMPreflightTests(unittest.TestCase):
    def test_current_manifest_is_usable_and_never_claims_freshness_without_apis(self):
        readiness = inspect(SOURCE)
        self.assertTrue(readiness.valid_source, readiness.errors)
        self.assertEqual(readiness.release_ready, len(readiness.missing_apis) == 0)
        self.assertEqual(set(readiness.missing_apis).issubset(REQUIRED_APIS), True)

    def test_missing_manifest_fails_closed(self):
        with tempfile.TemporaryDirectory() as work:
            source = Path(work)
            (source / "Entities").mkdir()
            (source / "SdkMessageProcessingSteps").mkdir()
            result = inspect(source)
            self.assertFalse(result.valid_source)
            self.assertFalse(result.release_ready)

    def test_all_required_apis_are_needed_for_release(self):
        with tempfile.TemporaryDirectory() as work:
            source = Path(work)
            (source / "Other").mkdir()
            (source / "Entities").mkdir()
            (source / "SdkMessageProcessingSteps").mkdir()
            (source / "Other/Solution.xml").write_text(
                "<ImportExportXml><SolutionManifest>"
                "<UniqueName>FinancialManager</UniqueName><Version>0.1.0.0</Version>"
                "<Managed>0</Managed><Publisher><CustomizationPrefix>fmg</CustomizationPrefix>"
                "</Publisher></SolutionManifest></ImportExportXml>", encoding="utf-8"
            )
            api = source / "customapis"
            api.mkdir()
            self.assertFalse(inspect(source).release_ready)
            for name in REQUIRED_APIS:
                (api / name).mkdir()
            self.assertTrue(inspect(source).release_ready)


if __name__ == "__main__":
    unittest.main()
