"""Source-only F6 ALM gates: full source sync, 24 guards and release dependency denial."""
from __future__ import annotations

import tempfile
import unittest
from zipfile import ZipFile
from pathlib import Path

from scripts.f6_alm_preflight import (
    REQUIRED_APIS, REQUIRED_LEGACY, REQUIRED_SHADOW, REQUIRED_PACKAGES,
    SOURCE, inspect,
)


def fixture(source: Path, dependencies: bool = False) -> None:
    for directory in ("Other", "Entities", "SdkMessageProcessingSteps", "customapis", "pluginpackages"):
        (source / directory).mkdir(parents=True, exist_ok=True)
    dep = ("<MissingDependencies><MissingDependency>"
           "<Required type='uxagentproject' solution='Active'/>"
           "</MissingDependency></MissingDependencies>") if dependencies else ""
    (source / "Other/Solution.xml").write_text(
        "<ImportExportXml><SolutionManifest>"
        "<UniqueName>FinancialManager</UniqueName><Version>0.1.0.0</Version>"
        "<Managed>0</Managed><Publisher><CustomizationPrefix>fmg</CustomizationPrefix>"
        "</Publisher>" + dep + "</SolutionManifest></ImportExportXml>", encoding="utf-8"
    )


class F6ALMPreflightTests(unittest.TestCase):
    def test_real_source_contains_all_required_guards_and_packages(self):
        result = inspect(SOURCE)
        self.assertTrue(result.valid_source, result.errors)
        self.assertTrue(result.source_synced, result)
        self.assertEqual(result.missing_apis, ())
        self.assertEqual(result.missing_guards, ())
        self.assertEqual(result.missing_shadows, ())
        self.assertEqual(result.missing_packages, ())
        # The four model-app UX projects are now in source. Two managed
        # Microsoft prerequisites still need verification in a target environment.
        self.assertFalse(result.release_ready)
        self.assertEqual(set(result.missing_dependencies), {"61", "SettingDefinition"})

    def test_missing_manifest_fails_closed(self):
        with tempfile.TemporaryDirectory() as work:
            source = Path(work)
            fixture(source)
            (source / "Other/Solution.xml").unlink()
            result = inspect(source)
            self.assertFalse(result.valid_source)
            self.assertFalse(result.release_ready)

    def test_all_required_components_prove_source_sync_but_not_dependency_approval(self):
        with tempfile.TemporaryDirectory() as work:
            source = Path(work)
            fixture(source, dependencies=True)
            self.assertFalse(inspect(source).source_synced)
            for name in REQUIRED_APIS:
                (source / "customapis" / name).mkdir()
            for index, name in enumerate(sorted(REQUIRED_LEGACY | REQUIRED_SHADOW)):
                rank = "1" if name in REQUIRED_LEGACY else "2"
                (source / "SdkMessageProcessingSteps" / f"step_{index}.xml").write_text(
                    f'<SdkMessageProcessingStep Name="{name}">'
                    f"<Rank>{rank}</Rank><Stage>10</Stage></SdkMessageProcessingStep>",
                    encoding="utf-8",
                )
            for name in REQUIRED_PACKAGES:
                p = source / "pluginpackages" / name
                p.mkdir()
                (p / "pluginpackage.xml").write_text("<pluginpackage />", encoding="utf-8")
                with ZipFile(p / "source.nupkg", "w") as archive:
                    archive.writestr(f"{name}.nuspec", "<package />")
            self.assertTrue(inspect(source).source_synced)
            self.assertFalse(inspect(source).release_ready)
            # MissingDependency is the remaining target import gate.
            fixture(source, dependencies=False)
            self.assertTrue(inspect(source).release_ready)

    def test_legacy_guard_removal_or_rank_change_blocks_source_sync(self):
        with tempfile.TemporaryDirectory() as work:
            source = Path(work)
            fixture(source)
            for name in REQUIRED_APIS:
                (source / "customapis" / name).mkdir()
            for index, name in enumerate(sorted(REQUIRED_LEGACY | REQUIRED_SHADOW)):
                rank = "2" if name in REQUIRED_SHADOW else "1"
                if index == 0:
                    rank = "3"  # One guard's altered execution rank must fail closed.
                (source / "SdkMessageProcessingSteps" / f"step_{index}.xml").write_text(
                    f'<SdkMessageProcessingStep Name="{name}">'
                    f"<Rank>{rank}</Rank><Stage>10</Stage></SdkMessageProcessingStep>",
                    encoding="utf-8",
                )
            self.assertTrue(inspect(source).missing_guards or inspect(source).missing_shadows)
            self.assertFalse(inspect(source).release_ready)


if __name__ == "__main__":
    unittest.main()
