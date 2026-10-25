"""Financial Manager v8 — final desktop grid and four independent phone layouts.

Run python scripts/build_power_bi_v8.py. Rebuilds native PBIR deterministically.
Never touches Dataverse or the budget approval path.
"""
from __future__ import annotations
import hashlib, json
from pathlib import Path
import importlib.util

HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("fm_v7_final",HERE/"build_power_bi_v7.py")
v7=importlib.util.module_from_spec(spec)
assert spec.loader
spec.loader.exec_module(v7)
v6=v7.v6
base=v7.b
PAGE_IDS={value:key for key,value in v6.PAGES_CONFIG}
MOBILE_SCHEMA="https://developer.microsoft.com/json-schemas/fabric/item/report/definition/visualContainerMobileState/2.4.0/schema.json"
PHONE_WIDTH=323
WIDTH,HEIGHT=1280,1630

def rect(board,key,x,y,w,h):
    assert key in board.items,(board.title,key)
    board.items[key]["position"].update(x=x,y=y,width=w,height=h)

def text_color_size(board,key,font):
    item=board.items[key]["visual"]
    try:
        run=item["objects"]["general"][0]["properties"]["paragraphs"][0]["textRuns"][0]
        run.setdefault("textStyle",{})["fontSize"]=f"{font}pt"
    except (KeyError,IndexError,TypeError):pass

def table_narrow(board,key,font=10,padding=6):
    obj=board.items[key]["visual"].setdefault("objects",{})
    if "grid" in obj:
        obj["grid"][0]["properties"]["rowPadding"]=base.literal(padding)
    if "columnHeaders" in obj:
        obj["columnHeaders"][0]["properties"]["fontSize"]=base.literal(font)
    if "values" in obj:
        obj["values"][0]["properties"]["fontSize"]=base.literal(font)

def _new_chart(board,key,kind,label,roles,x,y,w,h,labels=False):
    board.items.pop(key,None)
    board.data(key,kind,label,x,y-20,w,h,roles) # Builder shifts new charts +20; undo here
    rect(board,key,x,y,w,h)
    if labels:v7.axes(board,key,money=(kind!="clusteredColumnChart"),labels=True)

def tune_overview(board):
    # Preserve the accepted desktop first page verbatim; only a phone layout is new.
    return board

def tune_branches(board):
    # Sidebar: section heading no longer touches/underlaps the data card.
    rect(board,"operational-title",33,852,194,26)
    rect(board,"operational-state",32,892,200,118)
    rect(board,"sidebar-scope",32,1034,200,292)
    rect(board,"posted-badge-back",32,1360,200,37)
    rect(board,"posted-badge-text",43,1366,177,26)
    table_narrow(board,"sidebar-scope",font=10,padding=11)
    # Remove horizontal category scrollbar by allotting more vertical space
    # without making an uninformative two-bar chart too tall.
    rect(board,"branch-comparison",259,255,487,258)
    rect(board,"accounts",762,255,498,258)
    rect(board,"branch-month",259,572,664,294)
    rect(board,"category-breakdown",937,572,323,294)
    rect(board,"records-title",266,879,500,26)
    rect(board,"transactions",259,910,1001,632)
    return board

def tune_categories(board):
    # Fix grid offset from Board.build(y>=184): all three panels now share top and base.
    rect(board,"category-time",20,743,535,288)
    # A real three-class bar chart uses more of its panel than a narrow 3-row table.
    _new_chart(board,"flow-mix","clusteredColumnChart",
        "Gasto por tipo · S/ miles",
        {"Category":[base.c("FactPostedExpense","FlowClassLabel","Tipo de gasto")],
         "Y":[base.f("Paid Spending","Importe PEN")]},
        570,743,250,288)
    v7.axes(board,"flow-mix",money=True,labels=True)
    rect(board,"decomposition",829,743,431,288)
    # Bottom matrix and chart match at top AND bottom, with a readable gap after header.
    rect(board,"matrix-label",26,1043,510,25)
    rect(board,"category-matrix",20,1080,787,462)
    rect(board,"analysis-side",825,1080,435,462)
    rect(board,"matrix-foot",28,1551,1000,29)
    return board

def tune_trends(board):
    # Monthly labels are not pressed together at bottom: allow a less dense
    # horizontal series and retain an internal axis breathing margin.
    axis=board.items["main-trend"]["visual"].setdefault("objects",{})
    axis["categoryAxis"]=base.props(
        show=base.literal(True),
        axisType=base.literal("Categorical"),
        preferredCategoryWidth=base.literal(72),
        fontSize=base.literal(9),
        labelColor=base.color(v7.MUTED))
    rect(board,"main-trend",20,257,847,351)
    rect(board,"by-year",883,257,377,351)
    # The three panels share a baseline and top; scope is proportionally small
    # so turn its two values into an easily readable data-labelled chart.
    _new_chart(board,"scope","clusteredBarChart",
        "Gasto por ámbito · S/ miles",
        {"Category":[base.c("FactPostedExpense","ScopeLabel","Ámbito")],
         "Y":[base.f("Paid Spending","Importe PEN")]},
        954,795,306,273)
    v7.axes(board,"scope",money=True,labels=True)
    rect(board,"monthly-volume",20,795,453,273)
    rect(board,"types",488,795,451,273)
    # Matrix and right column form one grid, with a 12-point internal gap.
    rect(board,"year-month-grid",20,1113,935,433)
    rect(board,"flow-total",970,1113,290,204)
    _new_chart(board,"trends-breakdown","clusteredBarChart",
        "Número de pagos por clase",
        {"Category":[base.c("FactPostedExpense","FlowClassLabel","Tipo de gasto")],
         "Y":[base.f("Payment Count","Pagos")]},
        970,1329,290,217)
    v7.axes(board,"trends-breakdown",money=False,labels=True)
    rect(board,"matrix-label",27,1085,430,25)
    rect(board,"heatmap-caveat",30,1554,850,29)
    return board

MOBILE_CONFIG={
  "Resumen de gastos":{
    "filters":["filter-0","filter-1","filter-2","filter-3","filter-4"],
    "features":["monthly-combo","category-rank","scope-ring","flows","accounts",
                "year-total","payment-volume","register"],
  },
  "Sedes y movimientos":{
    "filters":["filter-0","filter-1","filter-2","filter-3","filter-4"],
    "features":["branch-comparison","accounts","branch-month",
                "category-breakdown","sidebar-scope","transactions"],
  },
  "Análisis de categorías":{
    "filters":["filter-0","filter-1","filter-2","filter-3","filter-4"],
    "features":["rank","tree","category-time","flow-mix",
                "decomposition","category-matrix","analysis-side"],
  },
  "Tendencias de pagos":{
    "filters":["filter-0","filter-1","filter-2","filter-3","filter-4"],
    "features":["main-trend","by-year","monthly-volume","types",
                "scope","year-month-grid","flow-total","trends-breakdown"],
  },
}
# Standard mobile grid: 323-point visual width, 7-pt gutters.
def phone(board):
    config=MOBILE_CONFIG[dict(v6.PAGES_CONFIG)[board.pid]]
    positions={}
    order=0
    def put(key,x,y,w,h):
        nonlocal order
        assert key in board.items,(board.title,key)
        assert x>=0 and w>0 and x+w<=PHONE_WIDTH,(key,x,w)
        for ok,op in positions.items():
            if x<op["x"]+op["width"] and x+w>op["x"] and y<op["y"]+op["height"] and y+h>op["y"]:
                raise AssertionError(f"Phone overlap: {key} and {ok}")
        positions[key]={"x":x,"y":y,"width":w,"height":h,"z":order,"tabOrder":order}
        order+=1
    y=0
    put("heading",0,y,323,100);y+=108
    # Five dropdown slicers, no decorative sidebar transferred to phone.
    for i,key in enumerate(config["filters"]):
        col=i%2
        if col==0 and i>0:y+=106
        put(key,165*col,y,158,100)
    y+=111
    kpis=[key for key in sorted(board.items) if key.startswith("kpi-")]
    for i,key in enumerate(kpis):
        if i and i%2==0:y+=132
        put(key,165*(i%2),y,158,126)
    y+=141
    for key in config["features"]:
        item=board.items[key]
        typ=item["visual"]["visualType"]
        height=310 if typ in ("tableEx","pivotTable") else (
          260 if typ in ("treemap","stackedAreaChart",
                "lineClusteredColumnComboChart","hundredPercentStackedColumnChart") else 223)
        # Financial history tables require more room, but appear AFTER charts.
        if key in ("register","transactions","category-matrix","year-month-grid"):height=360
        if key=="scope-ring":height=210
        put(key,0,y,323,height);y+=height+10
    out=v6.PAGES/board.pid/"visuals"
    for path in out.glob("*/mobile.json"):path.unlink()
    for key,pos in positions.items():
        identity=hashlib.sha1((board.pid+":"+key).encode()).hexdigest()[:20]
        target=out/identity/"mobile.json"
        if not target.parent.exists():raise AssertionError((key,target))
        # Mobile-specific positions only; desktop titles/fonts are inherited.
        entry={"$schema":MOBILE_SCHEMA,"position":pos}
        target.write_text(json.dumps(entry,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(f"MOBILE_PAGE={board.title}; placed={len(positions)}; height={y}")

def main():
    # Always regenerate from code, not ad hoc patches to an open Desktop model.
    boards=[
        tune_overview(v7.overview(v6.PAGES_CONFIG[0][0])),
        tune_branches(v7.branches(v6.PAGES_CONFIG[1][0])),
        tune_categories(v7.categories(v6.PAGES_CONFIG[2][0])),
        tune_trends(v7.trends(v6.PAGES_CONFIG[3][0])),
    ]
    for board in boards:
        board.items.pop("ribbon-note",None)
        for key,height in (("brand",44),("heading",58),("currency",44)):
            if key in board.items:
                board.items[key]["position"]["height"]=height
        v6.normalize_background_shapes(board)
        for key,item in board.items.items():
            p=item["position"]
            if p["x"]<0 or p["y"]<0 or p["x"]+p["width"]>WIDTH or p["y"]+p["height"]>HEIGHT:
                raise AssertionError((board.title,key,p))
        board.save() # removes stale phone visuals from earlier builds, intentionally
        path=v6.PAGES/board.pid/"page.json"
        page=json.loads(path.read_text(encoding="utf-8"))
        page["width"],page["height"]=WIDTH,HEIGHT
        page["displayOption"]="FitToWidth"
        page["objects"]={
            "background":base.props(color=base.color(v6.PAPER),transparency=base.literal(0)),
            "outspace":base.props(color=base.color(v6.PAPER),transparency=base.literal(0))}
        path.write_text(json.dumps(page,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        phone(board)
    print("FOUR_PHONE_LAYOUTS_READY=True")
    print("FINANCIAL_WRITES=False")
if __name__=="__main__":main()
