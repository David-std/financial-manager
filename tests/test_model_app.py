"""Static checks for the exported Power Apps model-driven Financial Manager UI.

These prove source/sitemap consistency, not successful interactive Power Apps execution.
"""
from __future__ import annotations

import re
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
PAGES = ROOT / "power-platform" / "model-apps" / "financial-manager"
SOLUTION = ROOT / "power-platform" / "solutions" / "FinancialManager" / "src"
SITEMAP = SOLUTION / "AppModuleSiteMaps" / "financial_manager_bee9f134" / "AppModuleSiteMap.xml"
APP = SOLUTION / "AppModules" / "financial_manager_bee9f134" / "AppModule.xml"

PAGE_IDS = {
    "Gastos": "92083d36-806a-43dd-b289-2fafd9e3afb1",
    "Presupuestos": "1a6e3c3e-fb2e-43b0-b5dd-9071483d1a1f",
    "Datos maestros": "35a771c0-5cdf-4d5e-a295-6f893ffcf3d4",
    "Detalle del gasto": "4a317af5-b6a8-4a82-a22a-6b56447a082f",
}

SOURCES = {
    "Gastos": "expenses.tsx",
    "Presupuestos": "budgets.tsx",
    "Datos maestros": "master-data.tsx",
    "Detalle del gasto": "expense-detail.tsx",
}


class ModelAppContractTests(unittest.TestCase):
    def test_exported_app_has_expected_unique_name(self) -> None:
        self.assertTrue(APP.is_file())
        xml = ET.parse(APP).getroot()
        self.assertEqual(xml.findtext("UniqueName"), "financial_manager_bee9f134")

    def test_four_pages_registered_once_and_labeled(self) -> None:
        self.assertTrue(SITEMAP.is_file())
        xml = ET.parse(SITEMAP).getroot()
        pages: dict[str, str] = {}
        for item in xml.findall(".//SubArea"):
            identifier = item.attrib.get("GenPageId")
            if not identifier:
                continue
            title = item.find("./Titles/Title")
            self.assertIsNotNone(title)
            label = title.attrib["Title"]
            self.assertNotIn(label, pages, f"Duplicate sitemap label: {label}")
            pages[label] = identifier.lower()
        self.assertEqual(pages, PAGE_IDS)

    def test_all_four_typescript_pages_exist(self) -> None:
        for name, file_name in SOURCES.items():
            with self.subTest(page=name):
                path = PAGES / file_name
                self.assertTrue(path.is_file(), f"Missing page source: {path}")
                self.assertIn("export default", path.read_text(encoding="utf-8"))

    def test_expense_list_routes_to_detail_record_context(self) -> None:
        source = (PAGES / "expenses.tsx").read_text(encoding="utf-8")
        self.assertIn(PAGE_IDS["Detalle del gasto"], source)
        self.assertIn('pageType: "generative"', source)
        self.assertIn("recordId: id", source)
        self.assertIn('dataApi.createRow("fmg_financialtransaction"', source)

    def test_detail_accepts_record_context_and_only_updates_transactions(self) -> None:
        source = (PAGES / "expense-detail.tsx").read_text(encoding="utf-8")
        self.assertIn("typeof pageInput", source)
        self.assertIn('dataApi.retrieveRow("fmg_financialtransaction"', source)
        self.assertIn('dataApi.updateRow("fmg_financialtransaction"', source)
        self.assertIn("fmg_voidreason", source)
        self.assertIn("fmg_status:797280001", source)
        self.assertIn("fmg_status:797280002", source)
        self.assertNotIn('dataApi.updateRow("fmg_budget"', source)

    def test_budget_and_catalog_pages_are_read_only(self) -> None:
        for file_name in ("budgets.tsx", "master-data.tsx"):
            with self.subTest(page=file_name):
                source = (PAGES / file_name).read_text(encoding="utf-8")
                self.assertIsNone(re.search(r"dataApi\.(createRow|updateRow|deleteRow)\s*\(", source))

    def test_essential_server_components_remain_versioned(self) -> None:
        packages = SOLUTION / "pluginpackages"
        names = {path.name for path in packages.iterdir() if path.is_dir()}
        self.assertTrue({"fmg_FinancialManagerValidation", "fmg_BudgetIntegrity"}.issubset(names))
        steps = list((SOLUTION / "SdkMessageProcessingSteps").glob("*.xml"))
        self.assertGreaterEqual(len(steps), 12)

    def test_no_account_credentials_or_tenant_endpoint_in_pages(self) -> None:
        patterns = (
            r"(?i)@[a-z0-9.-]+\.onmicrosoft\.com",
            r"(?i)[a-z0-9-]+\.crm\d*\.dynamics\.com",
            r"(?i)client[_-]?secret\s*[:=]",
            r"(?i)(refresh|access)[_-]?token\s*[:=]",
        )
        for file_name in SOURCES.values():
            text = (PAGES / file_name).read_text(encoding="utf-8")
            for pattern in patterns:
                with self.subTest(file=file_name, pattern=pattern):
                    self.assertNotRegex(text, pattern)


if __name__ == "__main__":
    unittest.main()
