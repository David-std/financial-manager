"""Validate the exported Financial Manager app and source guardrails.

This check is structural: it does not replace authenticated Power Apps UI tests.
It intentionally does not alter or deploy anything in Dataverse.
"""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UI = Path("power-platform/model-apps/financial-manager")
EXPORT = Path("power-platform/solutions/FinancialManager/src")
APP = EXPORT / "AppModules/financial_manager_bee9f134/AppModule.xml"
SITEMAP = EXPORT / "AppModuleSiteMaps/financial_manager_bee9f134/AppModuleSiteMap.xml"
PAGES = {
    "Gastos": "expenses.tsx",
    "Presupuestos": "budgets.tsx",
    "Datos maestros": "master-data.tsx",
    "Detalle del gasto": "expense-detail.tsx",
}
GUID = re.compile(r"^[a-fA-F0-9]{8}-(?:[a-fA-F0-9]{4}-){3}[a-fA-F0-9]{12}$")
MUTATION = re.compile(
    r"\bdataApi\s*\.\s*(?:createRow|updateRow|deleteRow)\s*\(\s*"
    r"['\"](?:fmg_budget|fmg_budgetallocation|fmg_category|fmg_branch|fmg_cashaccount)['\"]",
    re.MULTILINE,
)


def check(root: Path = ROOT) -> list[str]:
    """Return supported, reproducible structural failures for the exported app."""
    errors: list[str] = []
    app_file, site_file = root / APP, root / SITEMAP

    try:
        app = ET.parse(app_file).getroot()
        sitemap = ET.parse(site_file).getroot()
    except (OSError, ET.ParseError) as exc:
        return [f"Cannot read app and sitemap XML: {exc}"]

    app_name = app.findtext("UniqueName")
    site_name = sitemap.findtext("SiteMapUniqueName")
    if app_name != site_name or app_name != "financial_manager_bee9f134":
        errors.append("AppModule and AppModuleSiteMap unique names disagree")
    if not app.findall(".//AppModuleComponent[@type='62']"):
        errors.append("AppModule does not contain a sitemap component")

    found: dict[str, str] = {}
    used_ids: set[str] = set()
    for area in sitemap.findall(".//SubArea"):
        page_id = area.get("GenPageId", "")
        if not page_id:
            continue  # Native table navigation is permitted without a GenPageId.
        if not GUID.fullmatch(page_id):
            errors.append(f"Invalid generated page ID: {page_id}")
        if page_id.lower() in used_ids:
            errors.append(f"Duplicated generated page ID: {page_id}")
        used_ids.add(page_id.lower())
        titles = [
            t.get("Title", "")
            for t in area.findall("./Titles/Title")
            if t.get("LCID") in ("1033", "3082")
        ]
        title = titles[0] if titles else ""
        if not title or title in found:
            errors.append(f"Missing or duplicate sitemap title: {title!r}")
        else:
            found[title] = page_id

    if set(found) != set(PAGES):
        errors.append(
            f"Unexpected generated navigation: missing={sorted(set(PAGES)-set(found))}; "
            f"extra={sorted(set(found)-set(PAGES))}"
        )

    files: dict[str, str] = {}
    for label, filename in PAGES.items():
        path = root / UI / filename
        try:
            value = path.read_text(encoding="utf-8")
        except OSError:
            errors.append(f"Missing editable page source: {filename}")
            continue
        files[label] = value
        if "export default " not in value:
            errors.append(f"Generated page lacks a default React export: {filename}")

    runtime = root / UI / "RuntimeTypes.ts"
    try:
        types = runtime.read_text(encoding="utf-8")
    except OSError:
        errors.append("Missing Dataverse-generated RuntimeTypes.ts")
        types = ""
    for label, value in (("Draft", 797280000), ("Posted", 797280001), ("Voided", 797280002)):
        if f'"{label}" = {value}' not in types:
            errors.append(f"Wrong or missing transaction Choice: {label}={value}")

    expenses = files.get("Gastos", "")
    if not re.search(
        r'\bdataApi\.createRow\(\s*["\']fmg_financialtransaction["\']\s*,\s*'
        r'\{[^}]*fmg_status\s*:\s*797280000', expenses, re.DOTALL
    ):
        errors.append("The new expense entry must start in Draft")

    detail = files.get("Detalle del gasto", "")
    if not re.search(r'\bdataApi\.updateRow\(\s*["\']fmg_financialtransaction["\']', detail):
        errors.append("The detail editor lacks its Dataverse update route")
    if "fmg_voidreason" not in detail:
        errors.append("The detail editor lacks a void reason field")

    # Budgets are not approvable yet. Catalog changes must not be introduced
    # via an unreviewed client-side write route.
    for label in ("Presupuestos", "Datos maestros"):
        value = files.get(label, "")
        if MUTATION.search(value):
            errors.append(f"{label} contains an unreviewed direct data mutation")

    return errors


if __name__ == "__main__":
    problems = check()
    for problem in problems:
        print(problem, file=sys.stderr)
    if problems:
        raise SystemExit(1)
    print("Financial Manager model-app source and sitemap checks passed.")
