"""Build the source-controlled Financial Manager executive PBIR v2.

Rebuilds report-only native visuals deterministically. It never connects to
Dataverse, touches financial records, or hardcodes an environment hostname.
"""
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "power-bi" / "FinancialManager"
REPORT = PROJECT / "FinancialManager.Report"
MODEL = PROJECT / "FinancialManager.SemanticModel"
PAGE_DIR = REPORT / "definition" / "pages"
FACT = MODEL / "definition" / "tables" / "FactPostedExpense.tmdl"
SCHEMA = ("https://developer.microsoft.com/json-schemas/fabric/item/"
          "report/definition/visualContainer/2.9.0/schema.json")
NAVY, INK, MUTED = "#143451", "#152940", "#637489"
BLUE, TEAL, GOLD = "#2878B8", "#129D98", "#E3A948"
LINE, SURFACE, PAGE = "#DCE6F1", "#FFFFFF", "#F3F7FB"

def literal(value):
    if isinstance(value, bool):
        data = str(value).lower()
    elif isinstance(value, (int, float)):
        data = f"{value}D"
    else:
        data = "'" + str(value).replace("'", "''") + "'"
    return {"expr": {"Literal": {"Value": data}}}

def color(value):
    return {"solid": {"color": literal(value)}}

def props(**kwargs):
    return [{"properties": kwargs}]

def field(table, name, *, measure=False, label=None):
    typ = "Measure" if measure else "Column"
    result = {
        "field": {typ: {"Expression": {"SourceRef": {"Entity": table}}, "Property": name}},
        "queryRef": f"{table}.{name}",
        "nativeQueryRef": label or name,
    }
    if label:
        result["displayName"] = label
    return result

def f(name, label=None):
    return field("FactPostedExpense", name, measure=True, label=label)

def c(table, name, label=None):
    return field(table, name, label=label)

class Canvas:
    def __init__(self, pid, title, subtitle):
        self.pid, self.title = pid, title
        self.folder = PAGE_DIR / pid / "visuals"
        self.items = {}
        self.text("header-title", title.upper(), 24, 18, 915, 42,
                  23, "#FFFFFF", bg=NAVY, weight="semibold")
        self.text("header-subtitle", subtitle, 24, 58, 915, 30,
                  10, "#DBEAF6", bg=NAVY)
        self.text("header-brand", "FINANCIAL MANAGER  /  GASTOS · PEN", 949, 18,
                  307, 70, 12, "#FFFFFF", bg=NAVY, weight="semibold")
    def write(self):
        self.folder.mkdir(parents=True, exist_ok=True)
        for key, visual in self.items.items():
            vid = hashlib.sha1((self.pid + ":" + key).encode("utf-8")).hexdigest()[:20]
            visual["name"] = vid
            out = self.folder / vid
            out.mkdir(parents=True, exist_ok=True)
            (out / "visual.json").write_text(
                json.dumps(visual, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        names = set(self.items)
        print(f"PAGE {self.title}: {len(names)} visuals")
    def build(self, key, visual_type, x, y, w, h, *, title=None, query=None,
              vobjects=None, objects=None, z=2000):
        vid = hashlib.sha1((self.pid + ":" + key).encode("utf-8")).hexdigest()[:20]
        v = {"visualType": visual_type}
        if query:
            v["query"] = {"queryState": {
                group: {"projections": projections} for group, projections in query.items()}}
        if objects:
            v["objects"] = objects
        if vobjects:
            v["visualContainerObjects"] = vobjects
        self.items[key] = {
            "$schema": SCHEMA, "name": vid,
            "position": {"x": x, "y": y, "z": z, "height": h, "width": w,
                         "tabOrder": len(self.items) + 1},
            "visual": v,
        }
        return self.items[key]
    def chrome(self, title=None, accent=False, *, no_bg=False):
        style = {
            "background": props(show=literal(not no_bg),
                                color=color(SURFACE), transparency=literal(0)),
            "border": props(show=literal(not no_bg),
                            color=color(LINE), radius=literal(10),
                            width=literal(1)),
            "padding": props(left=literal(10), right=literal(10),
                             top=literal(6), bottom=literal(6)),
            "visualHeader": props(show=literal(False)),
        }
        style["subTitle"] = props(show=literal(False))
        if title is not None:
            style["title"] = props(
                show=literal(True), text=literal(title),
                fontColor=color(NAVY), fontSize=literal(12),
                fontFamily=literal("Segoe UI Semibold"),
                bold=literal(True), alignment=literal("left"))
        return style
    def text(self, key, value, x, y, w, h, size=14, ink=INK, *, bg=None, weight=None):
        style = {"fontFamily": "Segoe UI Semibold" if weight else "Segoe UI",
                 "fontSize": f"{size}pt", "color": ink}
        if weight:
            style["fontWeight"] = "bold"
        vb = self.chrome(no_bg=not bg)
        if bg:
            vb["background"] = props(show=literal(True), color=color(bg),
                                     transparency=literal(0))
            vb["border"] = props(show=literal(False))
        else:
            vb["border"] = props(show=literal(False))
        vb["padding"] = props(top=literal(0), bottom=literal(0),
                              left=literal(8), right=literal(6))
        self.build(key, "textbox", x, y, w, h, z=300, objects={
            "general": props(paragraphs=[{
                "textRuns": [{"value": value, "textStyle": style}],
                "horizontalTextAlignment": "left",
            }])}, vobjects=vb)
    def slicer(self, key, title, column, x, w=290):
        self.build(key, "slicer", x, 102, w, 82, title=title,
                   query={"Values": [column]}, vobjects=self.chrome(title),
                   objects={
                       "data": props(mode=literal("Dropdown")),
                       "header": props(show=literal(False)),
                       "selection": props(singleSelect=literal(False)),
                   })
    def card(self, key, title, measure, x, y=202, w=290, h=119):
        self.build(key, "card", x, y, w, h, title=title,
                   query={"Values": [measure]}, vobjects=self.chrome(title),
                   objects={
                       "categoryLabels": props(show=literal(False)),
                   })
    def chart(self, key, typ, title, x, y, w, h, categories, measures, *,
              extra=None):
        qs = {"Category": [categories], "Y": measures}
        if extra:
            qs.update(extra)
        self.build(key, typ, x, y, w, h, title=title, query=qs,
                   vobjects=self.chrome(title), objects={
                       "legend": props(show=literal(len(measures) > 1))
                   })
    def table(self, key, title, x, y, w, h, *fields):
        self.build(key, "tableEx", x, y, w, h, title=title,
                   query={"Values": list(fields)}, vobjects=self.chrome(title))

def build_model():
    text = FACT.read_text(encoding="utf-8")
    sentinel = "\tcolumn ExpenseId"
    if "measure 'Pagos confirmados KPI'" in text:
        print("MODEL measures already present")
        return
    assert text.count(sentinel) == 1
    additions = [
        ("Pagos confirmados KPI", "COALESCE ( [Paid Spending], 0 )", '"S/ "#,##0.00'),
        ("Cantidad de pagos KPI", "COALESCE ( [Payment Count], 0 )", "#,##0"),
        ("Ticket promedio KPI", "COALESCE ( [Average Payment], 0 )", '"S/ "#,##0.00'),
        ("Gasto compartido KPI", "COALESCE ( [Shared Spending], 0 )", '"S/ "#,##0.00'),
        ("Gasto por sede KPI", "COALESCE ( [Branch Spending], 0 )", '"S/ "#,##0.00'),
        ("Gasto anterior KPI", 'IF ( ISBLANK ( [Prior Year Spending] ), "Sin base anterior", FORMAT ( [Prior Year Spending], "S/ #,##0.00" ) )', None),
        ("Participación compartida", "COALESCE ( DIVIDE ( [Shared Spending], [Paid Spending], 0 ), 0 )", "0.0%"),
        ("Participación categoría", "COALESCE ( DIVIDE ( [Paid Spending], CALCULATE ( [Paid Spending], ALLSELECTED ( DimCategory ) ), 0 ), 0 )", "0.0%"),
        ("Categorías con pagos", "COALESCE ( DISTINCTCOUNT ( FactPostedExpense[CategoryId] ), 0 )", "#,##0"),
        ("Gasto acumulado YTD", "COALESCE ( TOTALYTD ( [Paid Spending], DimDate[Date] ), 0 )", '"S/ "#,##0.00'),
        ("Meses con pagos", "COALESCE ( COUNTROWS ( FILTER ( VALUES ( DimDate[YearMonth] ), NOT ISBLANK ( [Paid Spending] ) ) ), 0 )", "#,##0"),
        ("Promedio mensual pagado", "COALESCE ( DIVIDE ( [Paid Spending], [Meses con pagos], 0 ), 0 )", '"S/ "#,##0.00'),
        ("Variación interanual lectura", 'IF ( ISBLANK ( [Prior Year Spending] ), "Sin base anterior", FORMAT ( [Year-over-Year Change %], "0.0%;-0.0%;0.0%" ) )', None),
        ("Estado del informe", 'IF ( [Payment Count] = 0 || ISBLANK ( [Payment Count] ), "Sin pagos confirmados en PEN para los filtros actuales", "Pagos contabilizados · fuente Dataverse" )', None),
    ]
    values = ""
    for name, formula, fmt in additions:
        values += f"\tmeasure '{name}' = {formula}\n"
        if fmt:
            values += f"\t\tformatString: {fmt}\n"
        values += "\n"
    text = text.replace(sentinel, values + sentinel)
    FACT.write_text(text, encoding="utf-8")
    print(f"MODEL new measures: {len(additions)}")

def localize_fact():
    text = FACT.read_text(encoding="utf-8")
    if "column FlowClassLabel" in text:
        print("MODEL labels already present")
        return
    anchor = "\tcolumn ExpenseId"
    assert text.count(anchor) == 1
    cols = (
        "\tcolumn FlowClassLabel\n"
        "\t\tdataType: string\n\t\tsourceColumn: FlowClassLabel\n"
        "\t\tsummarizeBy: none\n\n"
        "\tcolumn ScopeLabel\n"
        "\t\tdataType: string\n\t\tsourceColumn: ScopeLabel\n"
        "\t\tsummarizeBy: none\n\n"
    )
    text = text.replace(anchor, cols + anchor)
    old = "            in\n\t\t\t    Typed"
    # Exact M formatting in the original committed table: spaces in the SQL
    # query, tab for TMDL partition indentation.
    alternative = "            in\n                Typed"
    actual = "            in\n\t\t\t    Typed"
    if actual not in text:
        import re
        hits = list(re.finditer(r"(?m)^(\s*)in\n(\s*)Typed\s*$", text))
        if len(hits) != 1:
            raise ValueError("Unable to locate unambiguous M final in Typed expression")
        m = hits[0]
        indentation = m.group(1)
        outputindent = m.group(2)
        replacement = (
            f"            , FlowLabels = Table.AddColumn(Typed, \"FlowClassLabel\", "
            "each if [FlowClassCode] = 797280000 then \"Operativo\" "
            "else if [FlowClassCode] = 797280001 then \"Capital\" "
            "else if [FlowClassCode] = 797280002 then \"Impuestos\" "
            "else \"Otros\", type text)\n"
            f"            , ScopeLabels = Table.AddColumn(FlowLabels, \"ScopeLabel\", "
            "each if [ScopeCode] = 797280001 then \"Compartido\" "
            "else \"Por sede\", type text)\n"
            f"{indentation}in\n{outputindent}ScopeLabels"
        )
        text = text[:m.start()] + replacement + text[m.end():]
    else:
        raise RuntimeError("Unexpected TMDL indentation; inspect query before changing")
    # Power Query M let declarations use a trailing comma on the preceding step.
    text = text.replace(
        '    , FlowLabels = Table.AddColumn',
        '    , FlowLabels = Table.AddColumn'
    )
    FACT.write_text(text, encoding="utf-8")
    print("MODEL category/scope labels added")

def build_pages():
    pages = {
        "overview": ("ac21e26d330743fa8b79",
            "Resumen de gastos", "Indicadores, evolución y composición del gasto confirmado"),
        "branches": ("de2b78eaf1b44c2a9485",
            "Sedes y movimientos", "Comparación por ubicación, cuenta y movimientos"),
        "categories": ("f9be472dc34d4e5cab27",
            "Análisis de categorías", "Concentración, aportes y comparación interanual"),
        "trends": ("f1a74ce8854f434789d2",
            "Tendencias de pagos", "Evolución temporal y comportamiento de los pagos"),
    }
    result = {}
    for key, (pid, title, subtitle) in pages.items():
        page = Canvas(pid, title, subtitle)
        result[key] = page

    O = result["overview"]
    for key, title, bind, x in (
        ("year", "Año", c("DimDate", "Year", "Año"), 24),
        ("branch", "Sede", c("DimBranch", "BranchName", "Sede"), 338),
        ("category", "Categoría", c("DimCategory", "CategoryName", "Categoría"), 652),
        ("scope", "Ámbito", c("FactPostedExpense", "ScopeLabel", "Ámbito"), 966),
    ):
        O.slicer(key, title, bind, x)
    for key, title, measure, x in (
        ("paid", "Pagos confirmados · PEN", f("Pagos confirmados KPI", "Total pagado"), 24),
        ("count", "Cantidad de pagos", f("Cantidad de pagos KPI", "Pagos"), 338),
        ("shared", "Gastos compartidos", f("Gasto compartido KPI", "Compartidos"), 652),
        ("average", "Ticket promedio", f("Ticket promedio KPI", "Promedio por pago"), 966),
    ):
        O.card(key, title, measure, x)
    O.chart("monthly", "lineChart", "Evolución mensual · PEN", 24, 337, 754, 250,
            c("DimDate", "YearMonth", "Mes"), [f("Paid Spending", "Total pagado")])
    O.chart("categories", "clusteredBarChart", "Distribución por categoría",
            792, 337, 464, 250, c("DimCategory", "CategoryName", "Categoría"),
            [f("Paid Spending", "Total pagado")])
    O.card("status", "Estado de los datos", f("Estado del informe", "Disponibilidad"),
           24, 603, 358, 90)
    O.table("last", "Movimientos confirmados · detalle", 395, 603, 861, 90,
            c("FactPostedExpense", "ExpenseReference", "Referencia"),
            c("FactPostedExpense", "TransactionDate", "Fecha"),
            c("DimCategory", "CategoryName", "Categoría"),
            c("FactPostedExpense", "AmountPEN", "Monto · PEN"))

    B = result["branches"]
    for key, title, bind, x in (
        ("year", "Año", c("DimDate", "Year", "Año"), 24),
        ("branch", "Sede", c("DimBranch", "BranchName", "Sede"), 338),
        ("account", "Cuenta de origen", c("DimCashAccount", "CashAccountName", "Cuenta"), 652),
        ("scope", "Ámbito", c("FactPostedExpense", "ScopeLabel", "Ámbito"), 966),
    ):
        B.slicer(key, title, bind, x)
    for key, title, measure, x in (
        ("paid", "Total confirmado · PEN", f("Pagos confirmados KPI", "Total pagado"), 24),
        ("branch", "Gastos por sede · PEN", f("Gasto por sede KPI", "Por sede"), 338),
        ("shared", "Gastos compartidos · PEN", f("Gasto compartido KPI", "Compartidos"), 652),
        ("share", "Proporción compartida", f("Participación compartida", "Participación"), 966),
    ):
        B.card(key, title, measure, x)
    B.chart("branches", "clusteredBarChart", "Gasto por sede (sin duplicar compartidos)",
            24, 337, 610, 228, c("DimBranch", "BranchName", "Sede"),
            [f("Paid Spending", "Total pagado")])
    B.chart("accounts", "clusteredBarChart", "Gasto por cuenta de origen",
            648, 337, 608, 228, c("DimCashAccount", "CashAccountName", "Cuenta"),
            [f("Paid Spending", "Total pagado")])
    B.table("transactions", "Detalle de movimientos confirmados", 24, 581, 1232, 115,
            c("FactPostedExpense", "ExpenseReference", "Referencia"),
            c("FactPostedExpense", "TransactionDate", "Fecha"),
            c("DimBranch", "BranchName", "Sede"),
            c("DimCashAccount", "CashAccountName", "Cuenta"),
            c("FactPostedExpense", "ScopeLabel", "Ámbito"),
            c("DimCategory", "CategoryName", "Categoría"),
            c("FactPostedExpense", "AmountPEN", "Monto · PEN"))

    C = result["categories"]
    for key, title, bind, x in (
        ("year", "Año", c("DimDate", "Year", "Año"), 24),
        ("category", "Categoría", c("DimCategory", "CategoryName", "Categoría"), 338),
        ("branch", "Sede", c("DimBranch", "BranchName", "Sede"), 652),
        ("flow", "Tipo de gasto", c("FactPostedExpense", "FlowClassLabel", "Tipo"), 966),
    ):
        C.slicer(key, title, bind, x)
    for key, title, measure, x in (
        ("paid", "Gasto confirmado · PEN", f("Pagos confirmados KPI", "Total pagado"), 24),
        ("prior", "Periodo comparable anterior", f("Gasto anterior KPI", "Año anterior"), 338),
        ("yoy", "Variación interanual", f("Variación interanual lectura", "Comparación"), 652),
        ("active", "Categorías con pagos", f("Categorías con pagos", "Categorías"), 966),
    ):
        C.card(key, title, measure, x)
    C.table("comparison", "Comparativo de gastos por categoría", 24, 337, 796, 359,
            c("DimCategory", "CategoryCode", "Código"),
            c("DimCategory", "CategoryName", "Categoría"),
            f("Paid Spending", "Gasto actual · PEN"),
            f("Prior Year Spending", "Gasto previo · PEN"),
            f("Year-over-Year Change %", "Variación interanual"),
            f("Participación categoría", "Participación"))
    C.chart("concentration", "clusteredBarChart", "Concentración del gasto",
            834, 337, 422, 359, c("DimCategory", "CategoryName", "Categoría"),
            [f("Paid Spending", "Total pagado")])

    T = result["trends"]
    for key, title, bind, x in (
        ("year", "Año", c("DimDate", "Year", "Año"), 24),
        ("month", "Mes de análisis", c("DimDate", "YearMonth", "Mes"), 338),
        ("flow", "Tipo de gasto", c("FactPostedExpense", "FlowClassLabel", "Tipo"), 652),
        ("scope", "Ámbito", c("FactPostedExpense", "ScopeLabel", "Ámbito"), 966),
    ):
        T.slicer(key, title, bind, x)
    for key, title, measure, x in (
        ("paid", "Pagos confirmados · PEN", f("Pagos confirmados KPI", "Total pagado"), 24),
        ("change", "Variación interanual", f("Variación interanual lectura", "Comparación"), 338),
        ("ytd", "Acumulado anual · PEN", f("Gasto acumulado YTD", "Acumulado"), 652),
        ("monthly", "Promedio mensual activo", f("Promedio mensual pagado", "Promedio"), 966),
    ):
        T.card(key, title, measure, x)
    T.chart("monthlytrend", "lineChart", "Pagos a lo largo del tiempo · PEN",
            24, 337, 754, 240, c("DimDate", "YearMonth", "Mes"),
            [f("Paid Spending", "Total pagado")])
    T.chart("annual", "clusteredColumnChart", "Gasto anual comparado",
            792, 337, 464, 240, c("DimDate", "Year", "Año"),
            [f("Paid Spending", "Total pagado")])
    T.chart("flow", "clusteredBarChart", "Composición por tipo de gasto",
            24, 591, 610, 105, c("FactPostedExpense", "FlowClassLabel", "Tipo"),
            [f("Paid Spending", "Total pagado")])
    T.card("context", "Estado del análisis", f("Estado del informe", "Disponibilidad"),
           648, 591, 608, 105)
    return result

def main():
    import shutil
    import tempfile
    import os
    from pathlib import Path
    # Source protection: never silently erase user-authored or unexpected visuals.
    existing = {f.parent.name for f in PAGE_DIR.glob("*/visuals/*/visual.json")}
    original = {
        "330179b73b2b6d70d53f","3341c0d3ef06938ce2a4","60a41df5fc3ba9427262",
        "716a2b54e797c6818b41","85a3a34ac44955aceac5","f64a3ba1587b14599526",
        "9d81ea41b0824eab951a","ab3495e76167ebae4834","db0dd3bf7ddbd4c4ae02",
        "4d8d1c80cf6a1003a483","cb1cc961dc6e662121fe","de09a3b950c5d7e2270d",
        "dfcc7cdcb54d59b94109","28b1708808353a96699b","d191a0369ab8d99c1e36",
        "d56d2c7b334e1428916f",
    }
    canvases = build_pages()
    new_ids = {
        hashlib.sha1((page.pid+":"+key).encode("utf-8")).hexdigest()[:20]
        for page in canvases.values() for key in page.items
    }
    if not existing <= original | new_ids:
        raise RuntimeError("Report includes unknown manual visuals; refusing destructive rebuild")
    backup = Path(tempfile.mkdtemp(prefix="FM-PBIR-before-v2-"))
    shutil.copytree(REPORT / "definition", backup / "definition")
    print("PRIVATE_REPORT_BACKUP=" + str(backup))
    build_model()
    localize_fact()
    for page in canvases.values():
        p = PAGE_DIR / page.pid / "visuals"
        if p.exists():
            shutil.rmtree(p)
        page.write()
        page_json = PAGE_DIR / page.pid / "page.json"
        data = json.loads(page_json.read_text(encoding="utf-8"))
        data["objects"] = {
            "background": props(color=color(PAGE), transparency=literal(0)),
            "outspace": props(color=color(PAGE), transparency=literal(0)),
        }
        page_json.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    # Keep existing Microsoft base theme; register a project-specific palette.
    custom = "FinancialManager-Executive-v2.json"
    theme_dir = REPORT / "StaticResources" / "RegisteredResources"
    theme_dir.mkdir(exist_ok=True)
    palette = {
        "name": custom,
        "dataColors": [BLUE,TEAL,GOLD,"#5375C7","#8F77C2","#5D9C84","#C67E70"],
        "background": PAGE, "foreground": INK, "tableAccent": BLUE,
        "good": TEAL, "neutral": GOLD, "bad": "#CE5A62",
    }
    (theme_dir / custom).write_text(
        json.dumps(palette,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
    report_file = REPORT / "definition" / "report.json"
    report = json.loads(report_file.read_text(encoding="utf-8"))
    report["themeCollection"]["customTheme"] = {
        "name": custom,
        "reportVersionAtImport": report["themeCollection"]["baseTheme"]["reportVersionAtImport"],
        "type": "RegisteredResources",
    }
    report["resourcePackages"] = [
        p for p in report["resourcePackages"] if p.get("name") != "RegisteredResources"]
    report["resourcePackages"].append({
        "name": "RegisteredResources", "type": "RegisteredResources",
        "items": [{"name": custom,"path":custom,"type":"CustomTheme"}],
    })
    report_file.write_text(json.dumps(report,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
    print("TOTAL_VISUALS=" + str(sum(len(p.items) for p in canvases.values())))

if __name__ == "__main__":
    main()
