"""Financial Manager PBIP v6: information-rich layouts and verified formatting.

Keeps v5's semantic contract, adds truthful status icon measures, a bounded
shared-spending gauge, readable record tables and substantive temporal matrix.
Runs only against the version-controlled report definition. No financial writes.
"""
from __future__ import annotations

import importlib.util
import json
import pathlib
import shutil
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
sp = importlib.util.spec_from_file_location("fm_v5", HERE/"build_power_bi_v5.py")
v5 = importlib.util.module_from_spec(sp)
assert sp.loader
sp.loader.exec_module(v5)
base = v5.b2
PAGES = v5.pages
REPORT = v5.report
FACT = REPORT.parent/"FinancialManager.SemanticModel"/"definition"/"tables"/"FactPostedExpense.tmdl"
W, H = 1280, 1630
INK, MUTED, PAPER = "#303B45", "#6D7B86", "#F6F6F3"
WHITE, MINT, ROSE, LILAC, SKY = "#FFFFFF", "#82A795", "#C79891", "#A59AC3", "#557E9A"
HEADER = "#EAF0F3"
STRIPE = "#F2F6F8"
LINE = "#E5E9EB"

PAGES_CONFIG = [
    ("ac21e26d330743fa8b79", "Resumen de gastos"),
    ("de2b78eaf1b44c2a9485", "Sedes y movimientos"),
    ("f9be472dc34d4e5cab27", "Análisis de categorías"),
    ("f1a74ce8854f434789d2", "Tendencias de pagos"),
]

def move(board,key,x,y,w,h):
    if key not in board.items:
        raise KeyError((board.title,key))
    board.items[key]["position"].update(x=x,y=y,width=w,height=h)

def label(board,key,value,x,y,w,h=26,size=11,tint=INK,weight=False):
    board.text(key,value,x,y,w,h,size,tint,weight="semibold" if weight else None)

def block(board,key,x,y,w,h,tint,rank=1):
    if w<6 or h<6:
        raise ValueError("Tiny decorative textbox")
    v5.block(board,key,x,y,w,h,tint)
    board.items[key]["position"]["z"]=rank

def pill(board,key,title,x,y,w,color):
    # Plain status label, not a clickable button.
    block(board,key+"-back",x,y,w,34,color)
    label(board,key+"-text",title,x+11,y+5,w-20,24,10,INK,True)

def add_icon(board,key,x,y,symbol,description,tint):
    # Icons are informational: no fabricated quality judgment.
    label(board,key+"-glyph",symbol,x,y,29,31,17,tint,True)
    board.items[key+"-glyph"]["position"]["z"]=3500
    # Actual metric titles stay in native Power BI cards.

def consolidate_kpis(board,y,left,width,n):
    # The ribbon is divided by whitespace rather than multicolor strokes.
    if "metric-back" in board.items:
        move(board,"metric-back",left,y,width,114)
    step=width/n
    symbols={
        "Visión general de gastos":["▣","≡","◷","◎","⇄"],
        "Sedes y movimientos":["▣","⌂","◎","%"],
        "Análisis de categorías":["▣","◷","⇄","≡"],
        "Tendencias y estacionalidad":["▣","∑","◷","⇄"],
    }
    for i in range(n):
        move(board,f"kpi-{i}",left+10+step*i,y+17,step-20,92)
        glyph=symbols.get(board.title,["•"]*n)[i]
        add_icon(board,f"metric-{i}",left+17+step*i,y+53,glyph,"Indicador",SKY)
        obj=board.items[f"kpi-{i}"]["visual"].setdefault("visualContainerObjects",{})
        obj["background"]=base.props(show=base.literal(True),
                                        color=base.color(WHITE),
                                        transparency=base.literal(0))
        obj["border"]=base.props(show=base.literal(False))
    label(board,"ribbon-note","PAGOS CONTABILIZADOS  ·  PEN",left+13,y+3,width-26,20,9,MUTED,True)

def table_format(v,*,matrix=False):
    obj=v["visual"].setdefault("objects",{})
    obj["columnHeaders"] = base.props(
        fontFamily=base.literal("Segoe UI Semibold"),
        fontSize=base.literal(10),
        fontColor=base.color(INK),
        backColor=base.color(HEADER),
        bold=base.literal(True),
        wordWrap=base.literal(True),
        autoSizeColumnWidth=base.literal(False),
        defaultColumnWidth=base.literal(135))
    obj["grid"] = base.props(
        gridVertical=base.literal(False),
        gridHorizontal=base.literal(True),
        gridHorizontalColor=base.color(LINE),
        rowPadding=base.literal(7),
        textSize=base.literal(10))
    obj["values"] = base.props(
        fontFamily=base.literal("Segoe UI"),
        fontSize=base.literal(10),
        fontColor=base.color(INK),
        backColorPrimary=base.color(WHITE),
        backColorSecondary=base.color(STRIPE),
        wordWrap=base.literal(False))
    if matrix:
        obj["rowHeaders"]=base.props(
            fontSize=base.literal(10),
            fontColor=base.color(INK),
            backColor=base.color(HEADER),
            wordWrap=base.literal(True))

def rename_projections(visual):
    """PBIR displayName has source-safe aliases, not rewritten DAX columns."""
    lookups = {
        "ExpenseReference":"Referencia",
        "TransactionDate":"Fecha de pago",
        "BranchName":"Sede",
        "CashAccountName":"Cuenta origen",
        "CategoryName":"Categoría",
        "ScopeLabel":"Ámbito",
        "FlowClassLabel":"Tipo de gasto",
        "AmountPEN":"Monto PEN",
        "Paid Spending":"Importe PEN",
        "Prior Year Spending":"Año anterior",
        "Year-over-Year Change %":"Variación %",
        "Payment Count":"Pagos",
        "Participación categoría":"Participación",
        "Estado por movimiento":"Estado",
        "YearMonth":"Año / mes",
        "Year":"Año",
        "Month":"Mes",
    }
    for item in visual["visual"].get("query",{}).get("queryState",{}).values():
        for projection in item.get("projections",[]):
            key=projection.get("queryRef","").split(".",1)[-1]
            name=lookups.get(key)
            if name:
                projection["nativeQueryRef"]=name
                projection["displayName"]=name

def enable_table(board,key,*,matrix=False):
    v=board.items[key]
    if v["visual"]["visualType"] not in ("tableEx","pivotTable"):
        raise ValueError("Not a table")
    rename_projections(v)
    table_format(v,matrix=matrix)

def gauge(board,key,title,x,y,w,h,measure):
    board.data(key,"gauge",title,x,y,w,h,{
        "Y":[base.f(measure, "Participación del total")],
        "MaxValue":[base.f("Escala porcentual", "100 %")],
    })
    return board.items[key]

def add_truthful_status(board,key,x,y,w,h):
    board.data(key,"card","Registros en el filtro",x,y,w,h,{
        "Values":[base.f("Estado de registros","Disponibilidad")],
    })

def overview(pid):
    board=v5.overview(pid)
    consolidate_kpis(board,107,20,1240,5)
    # Paneled filters are useful; replace repetitive explanatory footer.
    for k in ("side-foot","side-foot-title","side-foot-note"):
        board.items.pop(k,None)
    move(board,"side-panel",20,236,224,1334)
    label(board,"status-panel-title","LECTURA DEL PERÍODO",33,933,190,24,10,SKY,True)
    add_truthful_status(board,"status-overview",32,962,200,96)
    gauge(board,"shared-gauge","Proporción de gasto compartido",32,1073,200,239,
          "Participación compartida")
    label(board,"gauge-footnote","Del total confirmado, sin meta presupuestaria",36,1320,186,57,10,MUTED)
    pill(board,"posted-badge","✓  Solo contabilizados",32,1402,200,"#E3EEE7")
    move(board,"monthly-combo",260,255,664,353)
    move(board,"category-rank",940,255,320,353)
    move(board,"scope-ring",260,632,266,263)
    move(board,"flows",538,632,470,263)
    move(board,"accounts",1020,632,240,263)
    move(board,"register",260,931,1000,390)
    move(board,"year-total",260,1354,480,224)
    board.data("payment-volume","clusteredColumnChart",
       "Cantidad de pagos por año",756,1354,504,224,{
          "Category":[base.c("DimDate","Year","Año")],
          "Y":[base.f("Payment Count","Número de pagos")]})
    for k,(x,y,w) in {
       "monthly-heading":(269,230,450),
       "compo-heading":(269,608,450),
       "last-heading":(269,906,500),
    }.items():
        move(board,k,x,y,w,24)
    label(board,"year-summary-header","COMPARACIÓN POR EJERCICIO",269,1331,450,24,10,SKY,True)
    enable_table(board,"register")
    v=board.items["register"]["visual"]["query"]["queryState"]["Values"]["projections"]
    v.insert(-1,base.f("Estado por movimiento","Estado"))
    rename_projections(board.items["register"])
    return board

def branches(pid):
    board=v5.branches(pid)
    consolidate_kpis(board,107,260,1000,4)
    for k in ("side-foot","side-foot-title","side-foot-note"):
        board.items.pop(k,None)
    move(board,"side-panel",20,236,224,1334)
    label(board,"operational-title","CONTROL DE MOVIMIENTOS",33,926,194,26,10,SKY,True)
    add_truthful_status(board,"operational-state",32,960,200,95)
    gauge(board,"scope-share-gauge","Proporción compartida",32,1072,200,245,
          "Participación compartida")
    label(board,"note-shared","Compartido: se presenta separado, sin duplicarse en ninguna sede.",
          36,1330,190,77,9,MUTED)
    pill(board,"posted-badge","✓  Solo confirmados",32,1442,200,"#E3EEE7")
    move(board,"branch-comparison",259,255,620,330)
    move(board,"accounts",895,255,365,330)
    move(board,"scope-donut",259,619,250,266)
    move(board,"branch-month",525,619,450,266)
    move(board,"category-breakdown",991,619,269,266)
    move(board,"transactions",259,919,1001,629)
    move(board,"branch-title",265,229,500,26)
    move(board,"monthly-title",265,594,490,26)
    move(board,"records-title",265,893,480,26)
    enable_table(board,"transactions")
    v=board.items["transactions"]["visual"]["query"]["queryState"]["Values"]["projections"]
    v.insert(-1,base.f("Estado por movimiento","Estado"))
    rename_projections(board.items["transactions"])
    return board

def categories(pid):
    board=v5.categories(pid)
    consolidate_kpis(board,107,20,1240,4)
    # Keep filters integrated with comparative analysis.
    move(board,"filters-back",20,230,1240,115)
    move(board,"filters-title",31,236,300,26)
    for i in range(5):
        move(board,f"filter-{i}",31+i*246,262,232,78)
    move(board,"rank",20,379,585,326)
    move(board,"tree",619,379,641,326)
    move(board,"ranking-label",27,350,420,26)
    move(board,"category-time",20,743,535,288)
    move(board,"flow-mix",570,743,250,288)
    move(board,"decomposition",836,743,424,288)
    move(board,"category-matrix",20,1069,1240,482)
    move(board,"matrix-label",26,1042,510,25)
    enable_table(board,"category-matrix",matrix=True)
    # A current-year variance cannot be equated with missing prior base:
    # keep model's existing comparison measure and the explanatory label.
    label(board,"matrix-foot","Detalle comparativo por categoría · columnas de importe y participación",
          28,1556,900,23,10,MUTED)
    return board

def trends(pid):
    board=v5.trends(pid)
    move(board,"filters-back",20,108,1240,115)
    move(board,"filters-title",31,112,350,25)
    for i in range(5):
        move(board,f"filter-{i}",31+i*246,140,232,78)
    move(board,"main-trend",20,256,847,362)
    move(board,"by-year",883,256,377,362)
    move(board,"series-label",27,230,480,26)
    consolidate_kpis(board,650,20,1240,4)
    move(board,"magnitude-label",27,625,425,26)
    move(board,"monthly-volume",20,796,510,270)
    move(board,"types",545,796,475,270)
    move(board,"scope",1035,796,225,270)
    move(board,"year-month-grid",20,1113,908,433)
    move(board,"flow-total",945,1113,315,228)
    move(board,"matrix-label",27,1085,430,25)
    # Replace the sparse 3-row calendar matrix with a 36-month x category
    # matrix, based on the genuine date and category dimensions.
    mat=board.items["year-month-grid"]["visual"]["query"]["queryState"]
    mat["Rows"]={"projections":[base.c("DimDate","YearMonth","Año / mes")]}
    mat["Columns"]={"projections":[base.c("DimCategory","CategoryName","Categoría")]}
    mat["Values"]={"projections":[base.f("Paid Spending","Monto PEN")]}
    enable_table(board,"year-month-grid",matrix=True)
    gauge(board,"trends-gauge","Porción del gasto compartido",
          945,1355,315,191,"Participación compartida")
    label(board,"heatmap-caveat","Matriz mensual por categoría (importes en PEN)",30,1555,770,23,10,MUTED)
    return board

def model_updates():
    original=FACT.read_text(encoding="utf-8")
    if "Selecciona un año" in original:
        original=original.replace("Selecciona un año","Elige año")
        FACT.write_text(original,encoding="utf-8")
    added=[
      ("Escala porcentual","1","0.0%"),
      ("Estado de registros",
       'IF ( [Payment Count] > 0, "✓ Contabilizados", "Sin movimientos" )',None),
      ("Estado por movimiento",
       'IF ( HASONEVALUE ( FactPostedExpense[ExpenseId] ), "✓ Contabilizado", BLANK () )',None),
    ]
    if all("measure '"+name+"'" in original for name,_,_ in added):
        return 0
    if any("measure '"+name+"'" in original for name,_,_ in added):
        raise RuntimeError("Partially modified model; inspect before regenerating")
    anchor="\tcolumn FlowClassLabel"
    if original.count(anchor)!=1:
        raise RuntimeError("Unexpected FactPostedExpense model anchor")
    lines=[]
    for name,expr,fmt in added:
        lines.append(f"\tmeasure '{name}' = {expr}\n")
        if fmt:
            lines.append(f"\t\tformatString: {fmt}\n")
        lines.append("\n")
    FACT.write_text(original.replace(anchor,"".join(lines)+anchor),encoding="utf-8")
    return len(added)

def main():
    existing=list(PAGES.glob("*/visuals/*/visual.json"))
    if not 100<=len(existing)<=170:
        raise RuntimeError(f"Unexpected current PBIR state: {len(existing)} visuals")
    backup=pathlib.Path(tempfile.mkdtemp(prefix="FM-before-v6-"))
    shutil.copytree(REPORT/"definition",backup/"definition")
    print("PRIVATE_PBIR_BACKUP="+str(backup))
    boards=[overview(PAGES_CONFIG[0][0]),branches(PAGES_CONFIG[1][0]),
            categories(PAGES_CONFIG[2][0]),trends(PAGES_CONFIG[3][0])]
    for board in boards:
        for key,item in board.items.items():
            q=item["position"]
            if q["x"]<0 or q["y"]<0 or q["width"]<=0 or q["height"]<=0:
                raise ValueError(("Invalid",board.title,key,q))
            if q["x"]+q["width"]>W or q["y"]+q["height"]>H:
                raise ValueError(("Out of canvas",board.title,key,q))
        board.save()
        file=PAGES/board.pid/"page.json"
        page=json.loads(file.read_text(encoding="utf-8"))
        page["width"]=W
        page["height"]=H
        page["displayOption"]="FitToWidth"
        page["objects"]={
          "background":base.props(color=base.color(PAPER),transparency=base.literal(0)),
          "outspace":base.props(color=base.color(PAPER),transparency=base.literal(0))
        }
        file.write_text(json.dumps(page,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print("PAGE="+board.title+" ELEMENTS="+str(len(board.items)))
    measures=model_updates()
    total=sum(len(b.items) for b in boards)
    print("NEW_MEASURES="+str(measures)+" TOTAL_ELEMENTS="+str(total))
    print("FINANCIAL_SERVER_WRITE_COUNT=0")

if __name__=="__main__":
    main()
