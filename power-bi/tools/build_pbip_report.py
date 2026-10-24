"""Author the reviewable PBIR pages in a Microsoft CLI-scaffolded report shell.

Preserves report/page schema versions from the existing official scaffold.
Use the official powerbi-report-author validate before Desktop rendering.
"""
from __future__ import annotations

from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[1] / "FinancialManager"
REPORT = ROOT / "FinancialManager.Report"
DEFINITION = REPORT / "definition"
PAGES = DEFINITION / "pages"
PAGE1 = "ac21e26d330743fa8b79"
PAGE2 = "de2b78eaf1b44c2a9485"
VISUAL_SCHEMA = (
    "https://developer.microsoft.com/json-schemas/fabric/item/report/"
    "definition/visualContainer/2.9.0/schema.json"
)

def save(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8", newline="\n"
    )

def visual_id(name: str) -> str:
    return hashlib.sha256(name.encode("utf-8")).hexdigest()[:20]

def literal(value: str) -> dict:
    return {"expr": {"Literal": {"Value": "'" + value + "'"}}}

def column(table: str, field: str) -> dict:
    return {
        "field": {
            "Column": {
                "Expression": {"SourceRef": {"Entity": table}},
                "Property": field,
            }
        },
        "queryRef": table + "." + field,
        "nativeQueryRef": field,
    }

def measure(table: str, field: str) -> dict:
    return {
        "field": {
            "Measure": {
                "Expression": {"SourceRef": {"Entity": table}},
                "Property": field,
            }
        },
        "queryRef": table + "." + field,
        "nativeQueryRef": field,
    }

def add_visual(
    page: str, name: str, chart: str, x: int, y: int, width: int,
    height: int, roles: dict[str, list[dict]], title: str,
) -> None:
    page_dir = PAGES / page
    visuals = page_dir / "visuals"
    key = visual_id(page + "/" + name)
    z = 1000 + len(list(visuals.iterdir())) * 1000 if visuals.exists() else 1000
    report_visual = {
        "$schema": VISUAL_SCHEMA,
        "name": key,
        "position": {
            "x": x, "y": y, "z": z, "height": height,
            "width": width, "tabOrder": z,
        },
        "visual": {
            "visualType": chart,
            "query": {
                "queryState": {
                    role: {"projections": expressions}
                    for role, expressions in roles.items()
                }
            },
            "visualContainerObjects": {
                "title": [{
                    "properties": {
                        "show": {"expr": {"Literal": {"Value": "true"}}},
                        "text": literal(title),
                    }
                }]
            },
        },
    }
    save(visuals / key / "visual.json", report_visual)

def build() -> None:
    if not (REPORT / "definition.pbir").exists():
        raise ValueError("First scaffold the PBIR shell using the official authoring CLI")
    page1 = PAGES / PAGE1 / "page.json"
    settings = json.loads(page1.read_text(encoding="utf-8"))
    settings["displayName"] = "Resumen de gastos"
    save(page1, settings)
    page2 = dict(settings)
    page2["name"] = PAGE2
    page2["displayName"] = "Sedes y movimientos"
    save(PAGES / PAGE2 / "page.json", page2)
    order = json.loads((PAGES / "pages.json").read_text(encoding="utf-8"))
    order["pageOrder"] = [PAGE1, PAGE2]
    order["activePageName"] = PAGE1
    save(PAGES / "pages.json", order)

    # Overview: three KPIs and analytical comparisons, not placeholder metrics.
    add_visual(PAGE1, "total", "card", 24, 24, 290, 158,
               {"Values": [measure("FactPostedExpense", "Paid Spending")]},
               "Pagos confirmados · PEN")
    add_visual(PAGE1, "count", "card", 326, 24, 290, 158,
               {"Values": [measure("FactPostedExpense", "Payment Count")]},
               "Cantidad de pagos")
    add_visual(PAGE1, "shared", "card", 628, 24, 290, 158,
               {"Values": [measure("FactPostedExpense", "Shared Spending")]},
               "Gastos compartidos")
    add_visual(PAGE1, "year", "slicer", 930, 24, 326, 158,
               {"Values": [column("DimDate", "Year")]},
               "Filtrar por año")
    add_visual(PAGE1, "time", "lineChart", 24, 212, 612, 474,
               {
                   "Category": [column("DimDate", "YearMonth")],
                   "Y": [measure("FactPostedExpense", "Paid Spending")],
               },
               "Evolución mensual del gasto")
    add_visual(PAGE1, "category", "clusteredBarChart", 650, 212, 606, 474,
               {
                   "Category": [column("DimCategory", "CategoryName")],
                   "Y": [measure("FactPostedExpense", "Paid Spending")],
               },
               "Gastos por categoría")

    # Drillable operational review by branch and source account.
    add_visual(PAGE2, "branch", "clusteredBarChart", 24, 24, 606, 300,
               {
                   "Category": [column("DimBranch", "BranchName")],
                   "Y": [measure("FactPostedExpense", "Paid Spending")],
               },
               "Gastos registrados por sede")
    add_visual(PAGE2, "account", "clusteredBarChart", 650, 24, 606, 300,
               {
                   "Category": [column("DimCashAccount", "CashAccountName")],
                   "Y": [measure("FactPostedExpense", "Paid Spending")],
               },
               "Gastos por cuenta de origen")
    add_visual(PAGE2, "transactions", "tableEx", 24, 346, 1232, 340,
               {"Values": [
                   column("FactPostedExpense", "ExpenseReference"),
                   column("FactPostedExpense", "TransactionDate"),
                   column("FactPostedExpense", "FlowClassCode"),
                   column("FactPostedExpense", "ScopeCode"),
                   column("FactPostedExpense", "AmountPEN"),
               ]},
               "Movimientos confirmados")
    print("PBIR_PAGES=2;PBIR_VISUALS=9")
    print("PBIR_PROJECT=" + str(ROOT / "FinancialManager.pbip"))

if __name__ == "__main__":
    build()
