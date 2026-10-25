"""Financial Manager v7 — clarity, proportion, accessibility and semantic UX.

Post-v6 polish. Keep the Power BI native visual system and all financial guards.
All visuals use existing Dataverse model fields and filtered measures.
"""
from __future__ import annotations
import importlib.util, json, pathlib, shutil, tempfile

HERE=pathlib.Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("fm_v6",HERE/"build_power_bi_v6.py")
v6=importlib.util.module_from_spec(spec)
assert spec.loader
spec.loader.exec_module(v6)
b=v6.base
PAGES=v6.PAGES
REPORT=v6.REPORT
FACT=v6.FACT
W,H=1280,1630
INK="#303B45"
MUTED="#566775"
BLUE="#557E9A"
GREEN="#82A795"
LINE="#E3E8EB"

def at(board,key,x,y,w,h):
    v6.move(board,key,x,y,w,h)

def remove(board,*keys):
    for key in keys:board.items.pop(key,None)

def rename(board,key,title):
    v=board.items[key]
    co=v["visual"].get("visualContainerObjects",{})
    if "title" in co:
        co["title"]=b.props(
            show=b.literal(True),text=b.literal(title),
            fontSize=b.literal(11),fontColor=b.color(INK),
            fontFamily=b.literal("Segoe UI Semibold"),
            bold=b.literal(True))

def chart(board,key,kind,title,x,y,w,h,query,*,legend=False):
    if key in board.items:
        board.items.pop(key)
    board.data(key,kind,title,x,y,w,h,query,legend=legend)

def compare_table(board,key,title,x,y,w,h,dim,measures):
    chart(board,key,"tableEx",title,x,y,w,h,
          {"Values":[dim]+measures})
    v6.enable_table(board,key)
    # Compact comparisons should fit their host without horizontal scrolling.
    # The primary full-width transactional tables retain their original sizing.
    if w<420:
        visual=board.items[key]["visual"]
        obj=visual.setdefault("objects",{})
        width=max(90,int((w-26)/len([dim]+measures)))
        props=obj["columnHeaders"][0]["properties"]
        props["defaultColumnWidth"]=b.literal(width)
        props["fontSize"]=b.literal(9)
        obj["values"][0]["properties"]["fontSize"]=b.literal(9)
        obj["grid"][0]["properties"]["rowPadding"]=b.literal(6)

def short_months(board,key,*,year=False):
    v=board.items[key]
    query=v["visual"]["query"]["queryState"]
    if "Category" not in query:raise ValueError("No Category role")
    query["Category"]={"projections":[b.c("DimDate","Year" if year else "Month",
                                    "Año" if year else "Mes (1-12)")]}
    objs=v["visual"].setdefault("objects",{})
    objs["categoryAxis"]=b.props(show=b.literal(True),axisType=b.literal("Categorical"),
                                  preferredCategoryWidth=b.literal(38),fontSize=b.literal(10),
                                  labelColor=b.color(MUTED))
def axes(board,key,money=True,labels=False):
    typ=board.items[key]["visual"]["visualType"]
    obj=board.items[key]["visual"].setdefault("objects",{})
    # Correct display units are crucial for accounts and short 2/3-item bars.
    obj["valueAxis"]=b.props(show=b.literal(True),
        labelDisplayUnits=b.literal(1000 if money else 0),
        labelPrecision=b.literal(0 if money else 0),
        fontSize=b.literal(10),labelColor=b.color(MUTED),
        gridlineShow=b.literal(True),gridlineColor=b.color(LINE))
    if labels and typ in ("clusteredBarChart","clusteredColumnChart"):
        obj["labels"]=b.props(show=b.literal(True),
             labelDisplayUnits=b.literal(1000 if money else 0),
             labelPrecision=b.literal(1 if money else 0),
             labelPosition=b.literal("OutsideEnd"),fontSize=b.literal(10),
             color=b.color(INK))
def kpi_icons(board):
    # The icon is a genuine visual glyph beside the metric title, not an overlay
    # obstructing the amount. The native KPI still owns its value and semantics.
    for i in range(5):
        k=f"metric-{i}-glyph"
        if k not in board.items:continue
        kpi=board.items[f"kpi-{i}"]["position"]
        at(board,k,kpi["x"]+kpi["width"]-44,kpi["y"]-5,45,61)
        v=board.items[k]["visual"]
        for p in v.get("objects",{}).get("general",[]):
            pass
        # Increase textbox paragraph font by setting the authored run styling.
        raw=v.get("objects",{}).get("general",[{}])[0].get("properties",{})
        paras=raw.get("paragraphs",[{}])
        if paras:
            for run in paras[0].get("textRuns",[]):
                if "textStyle" in run:
                    run["textStyle"]["fontSize"]="20pt"
    # Missing fifth icon on 4-KPI pages is expected.

def fix_status(board,key):
    # Numeric count is accessible and does not get clipped as
    # "✓ Contabili..." in a 200px sidebar.
    v=board.items[key]
    v["visual"]["query"]["queryState"]["Values"]={
        "projections":[b.f("Payment Count","Pagos en la selección")]}
    rename(board,key,"Pagos en la selección")
    v["visual"].setdefault("objects",{})["categoryLabels"]=b.props(show=b.literal(False))
    at(board,key,32,958,200,104)

def matrix_compare(board):
    v=board.items["category-matrix"]
    roles=v["visual"]["query"]["queryState"]["Values"]["projections"]
    for i,p in enumerate(roles):
        if p.get("queryRef","").endswith("Prior Year Spending"):
            roles[i]=b.f("Año previo comparable","Año previo")
            break
    v6.rename_projections(v)

def overview(pid):
    board=v6.overview(pid)
    kpi_icons(board)
    fix_status(board,"status-overview")
    rename(board,"register","Detalle de movimientos · fechas e importes en PEN")
    rename(board,"monthly-combo","Gasto mensual (barras: S/; línea: cantidad de pagos)")
    rename(board,"category-rank","Gasto por categoría · S/ miles")
    axes(board,"category-rank",labels=False)
    # Full labels and exact values replace ambiguous 0.0/0.1/0.2 million ticks.
    compare_table(board,"accounts","Cuentas de origen · importes en PEN",
        1000,635,260,260,
        b.c("DimCashAccount","CashAccountName","Cuenta"),
        [b.f("Paid Spending","S/ pagados")])
    compare_table(board,"scope-ring","Gasto por ámbito · PEN",
        260,635,310,260,
        b.c("FactPostedExpense","ScopeLabel","Ámbito"),
        [b.f("Paid Spending","Importe S/"),
         b.f("Participación por ámbito","% del total")])
    at(board,"flows",585,635,400,260)
    short_months(board,"flows")
    rename(board,"flows","Composición por tipo y mes (años filtrados)")
    # Three flow series have clear columns, not ragged/partially clipped area.
    board.items["flows"]["visual"]["visualType"]="hundredPercentStackedColumnChart"
    board.items["flows"]["visual"]["objects"]["legend"]=b.props(
        show=b.literal(True),position=b.literal("Bottom"),
        fontSize=b.literal(10),labelColor=b.color(MUTED))
    at(board,"register",260,939,1000,394)
    at(board,"year-total",260,1372,487,222)
    at(board,"payment-volume",762,1372,498,222)
    rename(board,"year-total","Gasto por año · S/ miles")
    rename(board,"payment-volume","Número de pagos por año")
    axes(board,"year-total",labels=True)
    axes(board,"payment-volume",money=False,labels=True)
    at(board,"year-summary-header",266,1343,450,26)
    # Sidebar: remove redundant large gauge, show two meaningful quick metrics.
    remove(board,"shared-gauge","gauge-footnote")
    compare_table(board,"sidebar-scope","Ámbito · importe en PEN",
        31,1090,201,218,
        b.c("FactPostedExpense","ScopeLabel","Ámbito"),
        [b.f("Paid Spending","Total S/")])
    at(board,"posted-badge-back",32,1397,200,36)
    at(board,"posted-badge-text",42,1403,180,27)
    return board

def branches(pid):
    board=v6.branches(pid)
    kpi_icons(board)
    fix_status(board,"operational-state")
    # Proportion is already a main KPI; remove redundant gauge and cramped chart.
    remove(board,"scope-share-gauge","note-shared","scope-donut")
    compare_table(board,"sidebar-scope","Ámbito · importes",
        31,1084,201,225,b.c("FactPostedExpense","ScopeLabel","Ámbito"),
        [b.f("Paid Spending","Importe S/")])
    at(board,"posted-badge-back",31,1428,202,36)
    at(board,"posted-badge-text",42,1434,180,25)
    at(board,"branch-comparison",259,255,487,258)
    at(board,"accounts",762,255,498,258)
    rename(board,"branch-comparison","Sedes · importe asignado (S/ miles)")
    rename(board,"accounts","Cuenta de origen · importe (S/ miles)")
    axes(board,"branch-comparison",labels=True)
    axes(board,"accounts",labels=True)
    # Monthly bar chart is now the central visual, not a small squeezed
    # 36-month sidecar; category ranking sits alongside as a short comparison.
    at(board,"branch-month",259,572,664,275)
    short_months(board,"branch-month")
    rename(board,"branch-month","Sedes por mes de calendario · S/ (años filtrados)")
    at(board,"category-breakdown",937,572,323,275)
    rename(board,"category-breakdown","Categorías operativas · S/ miles")
    axes(board,"category-breakdown")
    at(board,"monthly-title",266,543,500,26)
    at(board,"transactions",259,900,1001,638)
    rename(board,"transactions","Movimientos por sede, cuenta y categoría · PEN")
    at(board,"records-title",266,872,500,26)
    return board

def categories(pid):
    board=v6.categories(pid)
    kpi_icons(board)
    matrix_compare(board)
    # Replace blank-by-default decomposition with a real first-view metric.
    chart(board,"decomposition","clusteredBarChart",
          "Ticket promedio por categoría · S/ miles",
          829,745,431,282,{
            "Category":[b.c("DimCategory","CategoryName","Categoría")],
            "Y":[b.f("Average Payment","Ticket medio S/")]})
    axes(board,"decomposition")
    rename(board,"rank","Categorías ordenadas por gasto · S/ miles")
    axes(board,"rank")
    rename(board,"tree","Participación de cada categoría en el gasto")
    compare_table(board,"flow-mix","Gasto por tipo · PEN",
        570,745,250,282,
        b.c("FactPostedExpense","FlowClassLabel","Tipo"),
        [b.f("Paid Spending","Monto S/")])
    # Show a concise comparison matrix with an adjacent complementary driver;
    # don't leave a large blank right-hand side of a 1240px matrix.
    at(board,"category-matrix",20,1074,787,469)
    rename(board,"category-matrix","Categoría · gasto, año anterior y peso")
    chart(board,"analysis-side","clusteredBarChart",
       "Cantidad de pagos por categoría",
       825,1074,435,469,{
         "Category":[b.c("DimCategory","CategoryName","Categoría")],
         "Y":[b.f("Payment Count","Pagos")]})
    axes(board,"analysis-side",money=False,labels=True)
    at(board,"matrix-foot",28,1554,1000,30)
    return board

def trends(pid):
    board=v6.trends(pid)
    kpi_icons(board)
    rename(board,"main-trend","Evolución del gasto · S/ miles por mes")
    rename(board,"by-year","Gasto anual · S/ miles")
    axes(board,"by-year",labels=True)
    rename(board,"monthly-volume","Gasto (barras, S/) y número de pagos (línea)")
    # By month-of-year avoids the 30+ narrow scrolling labels.
    short_months(board,"monthly-volume")
    at(board,"monthly-volume",20,795,453,273)
    short_months(board,"types")
    at(board,"types",488,795,451,273)
    rename(board,"types","Estructura por tipo · % de cada mes (años filtrados)")
    # Replace tiny 225px donut with an actual readable mini-summary.
    compare_table(board,"scope","Gasto por ámbito · PEN",
        954,795,306,273,
        b.c("FactPostedExpense","ScopeLabel","Ámbito"),
        [b.f("Paid Spending","Importe S/"),
         b.f("Participación por ámbito","% del total")])
    at(board,"year-month-grid",20,1113,935,433)
    at(board,"flow-total",970,1113,290,239)
    rename(board,"flow-total","Tipo de gasto · importe en PEN")
    axes(board,"flow-total")
    remove(board,"trends-gauge")
    compare_table(board,"trends-breakdown","Importe por clase de flujo",
       970,1370,290,176,b.c("FactPostedExpense","FlowClassLabel","Tipo"),
       [b.f("Paid Spending","Monto S/")])
    at(board,"heatmap-caveat",30,1555,850,32)
    return board

def add_measures():
    src=FACT.read_text(encoding="utf-8")
    additions=[
       ("Participación por ámbito",
        "DIVIDE ( [Paid Spending], CALCULATE ( [Paid Spending], ALLSELECTED ( FactPostedExpense[ScopeLabel] ) ), 0 )",
        "0.0%"),
       ("Año previo comparable",
        "IF ( HASONEVALUE ( DimDate[Year] ), [Prior Year Spending], BLANK () )",
        "#,##0.00")
    ]
    missing=[(n,e,fmt) for n,e,fmt in additions if "measure '"+n+"'" not in src]
    if missing:
        anchor="\tcolumn FlowClassLabel"
        if src.count(anchor)!=1:raise RuntimeError("TMDL unexpected structure")
        inserts=""
        for name,expr,fmt in missing:
            inserts+=f"\tmeasure '{name}' = {expr}\n\t\tformatString: {fmt}\n\n"
        FACT.write_text(src.replace(anchor,inserts+anchor),encoding="utf-8")
    return len(missing)

def main():
    existing=list(PAGES.glob("*/visuals/*/visual.json"))
    if not 140<=len(existing)<=190:raise RuntimeError(f"Wrong baseline: {len(existing)}")
    backup=pathlib.Path(tempfile.mkdtemp(prefix="FM-PBIR-pre-v7-"))
    shutil.copytree(REPORT/"definition",backup/"definition")
    print("PRIVATE_BACKUP="+str(backup))
    boards=[overview(v6.PAGES_CONFIG[0][0]),branches(v6.PAGES_CONFIG[1][0]),
            categories(v6.PAGES_CONFIG[2][0]),trends(v6.PAGES_CONFIG[3][0])]
    for board in boards:
        board.items.pop("ribbon-note",None)
        for key,hh in (("brand",44),("heading",58),("currency",44)):
            if key in board.items:
                board.items[key]["position"]["height"]=hh
        v6.normalize_background_shapes(board)
        for key,v in board.items.items():
            p=v["position"]
            if p["width"]<=0 or p["height"]<=0 or p["x"]<0 or p["y"]<0 or p["x"]+p["width"]>W or p["y"]+p["height"]>H:
                raise RuntimeError(f"Out of canvas {board.title}/{key}: {p}")
        board.save()
        file=PAGES/board.pid/"page.json"
        meta=json.loads(file.read_text(encoding="utf-8"))
        meta["width"],meta["height"]=W,H
        meta["displayOption"]="FitToWidth"
        meta["objects"]={
            "background":b.props(color=b.color(v6.PAPER),transparency=b.literal(0)),
            "outspace":b.props(color=b.color(v6.PAPER),transparency=b.literal(0))}
        file.write_text(json.dumps(meta,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(f"PAGE={board.title} COUNT={len(board.items)}")
    print("NEW_MEASURES="+str(add_measures()))
    print("VISUALS_TOTAL="+str(sum(len(board.items) for board in boards)))
    print("NO_DATAVERSE_WRITES=True")

if __name__=="__main__":main()
