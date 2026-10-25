"""Financial Manager v9: independent, readable phone layouts only.

Run after scripts/build_power_bi_v8.py (or on the existing v8 PBIP).
Changes only mobile.json files. Desktop visual.json, page.json and semantic TMDL
remain byte-for-byte unchanged.
"""
from __future__ import annotations
import hashlib,json
from pathlib import Path
from collections import defaultdict

ROOT=Path(__file__).resolve().parents[1]
PAGES=ROOT/"power-bi/FinancialManager/FinancialManager.Report/definition/pages"
W=323
SCHEMA="https://developer.microsoft.com/json-schemas/fabric/item/report/definition/visualContainerMobileState/2.4.0/schema.json"

def lit(value):
    if isinstance(value,bool): value="true" if value else "false"
    elif isinstance(value,str):value="'"+value.replace("'","''")+"'"
    elif isinstance(value,(int,float)):value=str(value)+"D"
    return {"expr":{"Literal":{"Value":value}}}
def props(**values):return [{"properties":{k:lit(v) for k,v in values.items()}}]

# Keep the functional slicers, KPI values, chart measures and filters from Desktop.
# Use all original financial safeguards; phone state changes layout and formatting only.
PAGES_CONFIG={
 "Resumen de gastos":{
    "filters":["filter-0","filter-1","filter-2","filter-3","filter-4"],
    "kpi":["kpi-0","kpi-1","kpi-2","kpi-3","kpi-4"],
    "charts":[("monthly-combo",310),("category-rank",355),("scope-ring",230),
              ("flows",305),("accounts",255),("year-total",255),
              ("payment-volume",255),("register",410)]},
 "Sedes y movimientos":{
    "filters":["filter-0","filter-1","filter-2","filter-3","filter-4"],
    "kpi":["kpi-0","kpi-1","kpi-2","kpi-3"],
    "charts":[("branch-comparison",258),("accounts",275),("branch-month",300),
              ("category-breakdown",345),("sidebar-scope",230),("transactions",440)]},
 "Análisis de categorías":{
    "filters":["filter-0","filter-1","filter-2","filter-3","filter-4"],
    "kpi":["kpi-0","kpi-3","kpi-1","kpi-2"],
    "charts":[("rank",362),("category-time",300),("flow-mix",262),
              ("decomposition",350),("analysis-side",350)]},
 "Tendencias de pagos":{
    "filters":["filter-0","filter-1","filter-2","filter-3","filter-4"],
    "kpi":["kpi-0","kpi-1","kpi-2","kpi-3"],
    "charts":[("main-trend",324),("by-year",255),("monthly-volume",300),
              ("types",300),("scope",245),("flow-total",250),
              ("trends-breakdown",250)]},
}
TITLES={
 "monthly-combo":"Gasto mensual y número de pagos",
 "category-rank":"Principales categorías · S/ miles",
 "scope-ring":"Gasto compartido y por sede",
 "flows":"Composición mensual por tipo",
 "accounts":"Cuentas de origen · S/ miles",
 "year-total":"Gasto anual · S/ miles",
 "payment-volume":"Pagos por año",
 "register":"Detalle de movimientos",
 "branch-comparison":"Gasto por sede · S/ miles",
 "branch-month":"Sedes por mes · S/ miles",
 "category-breakdown":"Categorías operativas",
 "sidebar-scope":"Importe por ámbito · PEN",
 "transactions":"Registro de movimientos",
 "rank":"Ranking de categorías · S/ miles",
 "category-time":"Evolución por clase de gasto",
 "flow-mix":"Importe por tipo de gasto",
 "decomposition":"Ticket medio por categoría",
 "analysis-side":"Cantidad de pagos por categoría",
 "main-trend":"Tendencia mensual · S/ miles",
 "by-year":"Gasto anual · S/ miles",
 "monthly-volume":"Pagos por mes: importe y frecuencia",
 "types":"Distribución mensual · % por tipo",
 "scope":"Gasto por ámbito · S/ miles",
 "flow-total":"Gasto por clase · S/ miles",
 "trends-breakdown":"Pagos por clase",
}
def name(pid,key):return hashlib.sha1(f"{pid}:{key}".encode("utf-8")).hexdigest()[:20]
def visual(folder,key):
    path=folder/"visuals"/name(folder.name,key)/"visual.json"
    if not path.exists():raise ValueError(f"Missing source {folder.name}:{key}")
    return json.loads(path.read_text(encoding="utf-8"))
def layout(page,config):
    placed={}
    order=0
    def put(key,x,y,w,h):
        nonlocal order
        if key in placed:raise AssertionError(f"Duplicate {key}")
        if x<0 or y<0 or w<100 or h<58 or x+w>W:
            raise ValueError((key,x,y,w,h))
        for prior,p in placed.items():
            if x<p["x"]+p["width"] and x+w>p["x"] and y<p["y"]+p["height"] and y+h>p["y"]:
                raise AssertionError(f"{page.name}:{key} overlaps {prior}")
        placed[key]={"x":x,"y":y,"width":w,"height":h,"z":order,"tabOrder":order}
        order+=1
    y=0
    put("heading",0,y,W,73)
    y+=80
    primary=config["kpi"][0]
    put(primary,0,y,W,100)
    y+=111
    fs=config["filters"]
    for i in (0,2):
        put(fs[i],0,y,158,79)
        put(fs[i+1],165,y,158,79)
        y+=87
    put(fs[4],0,y,W,79)
    y+=90
    # Secondary KPIs need more than a compressed 126-pt card.
    others=config["kpi"][1:]
    for i in range(0,len(others),2):
        row=others[i:i+2]
        if len(row)==2:
            put(row[0],0,y,158,119)
            put(row[1],165,y,158,119)
        else:
            put(row[0],0,y,W,108)
        y+=128 if len(row)==2 else 117
    y+=9
    for key,h in config["charts"]:
        put(key,0,y,W,h)
        y+=h+12
    return placed,y
def mobile_override(key,typ,w,primary):
    state={"visualContainerObjects":{},"objects":{}}
    vco=state["visualContainerObjects"]
    obj=state["objects"]
    if key!="heading":
        vco["title"]=props(show=True,text=TITLES.get(key,None) or "",titleWrap=True,
                           fontSize=(10 if w==158 else 11),
                           fontColor="#303B45",bold=True)
        if key.startswith("filter-") or key.startswith("kpi-"):
            del vco["title"]
        vco["padding"]=props(left=7,right=7,top=5,bottom=5)
        vco["general"]=props(altText=ALT.get(key,"Visualización de análisis financiero."))
    # Heading inherits the 21pt desktop title without rewriting its text.
    if key.startswith("kpi-"):
        obj["labels"]=props(fontSize=24 if primary else (16 if w==158 else 20))
        vco["title"]=props(fontSize=10 if w==158 else 12,titleWrap=True)
        vco["padding"]=props(left=8,right=8,top=6,bottom=6)
    elif key.startswith("filter-"):
        vco["title"]=props(fontSize=11,titleWrap=False)
    elif typ in ("tableEx","pivotTable"):
        obj["columnHeaders"]=props(fontSize=10,wordWrap=True,defaultColumnWidth=112,autoSizeColumnWidth=False)
        obj["values"]=props(fontSize=10,wordWrap=False)
        obj["grid"]=props(rowPadding=7)
    elif typ in ("clusteredBarChart","lineClusteredColumnComboChart","stackedAreaChart","areaChart",
                 "clusteredColumnChart","hundredPercentStackedColumnChart"):
        obj["categoryAxis"]=props(fontSize=9,labelColor="#566775",preferredCategoryWidth=44)
        obj["valueAxis"]=props(fontSize=9,labelColor="#566775")
        if typ in ("hundredPercentStackedColumnChart","stackedAreaChart"):
            obj["legend"]=props(show=True,position="Bottom",fontSize=9,labelColor="#566775")
    # Use only allowed mobile delta keys and omit empty sets.
    return {k:v for k,v in state.items() if v}
ALT={
 "monthly-combo":"Barras de gasto mensual en soles y línea de número de pagos.",
 "category-rank":"Barras horizontales de gasto por categoría, expresado en miles de soles.",
 "scope-ring":"Tabla con gasto compartido y gasto por sede, en soles y porcentaje.",
 "branch-comparison":"Comparación de gasto asignado entre las sedes.",
 "branch-month":"Comparación mensual de gasto por cada sede filtrada.",
 "decomposition":"Ticket promedio pagado por categoría.",
 "main-trend":"Evolución mensual de los gastos confirmados, en soles.",
 "scope":"Importes confirmados por ámbito en miles de soles.",
 "register":"Tabla detallada. Desliza horizontalmente para explorar columnas adicionales.",
 "transactions":"Registro detallado. Desliza horizontalmente para ver todas las columnas.",
 "types":"Columnas 100% apiladas: porcentaje mensual de capital, impuestos y operativo.",
}
def main():
    seen=0
    for page in PAGES.iterdir():
        if not (page/"page.json").exists():continue
        title=json.loads((page/"page.json").read_text(encoding="utf-8"))["displayName"]
        if title not in PAGES_CONFIG:raise AssertionError("Unexpected page "+title)
        config=PAGES_CONFIG[title]
        positions,ending=layout(page,config)
        for f in page.glob("visuals/*/mobile.json"):f.unlink()
        for key,pos in positions.items():
            p=page/"visuals"/name(page.name,key)/"mobile.json"
            source=visual(page,key)
            typ=source["visual"]["visualType"]
            state={"$schema":SCHEMA,"position":pos}
            state.update(mobile_override(key,typ,pos["width"],key==config["kpi"][0]))
            if key=="heading":
                # A long Spanish heading (e.g. "Tendencias y estacionalidad")
                # must fit in 73 pt with two lines and no internal textbox scroll.
                runs=json.loads(json.dumps(source["visual"]["objects"]["general"]))
                for paragraph in runs[0]["properties"]["paragraphs"]:
                    for run in paragraph.get("textRuns",[]):
                        run.setdefault("textStyle",{})["fontSize"]="16pt"
                state["objects"]={"general":runs}
            p.write_text(json.dumps(state,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(f"PHONE={title}: visuals={len(positions)}, length={ending}, retained source visuals={len(list(page.glob('visuals/*/visual.json')))}")
        seen+=1
    if seen!=4:raise AssertionError(f"Expected four pages, got {seen}")
    print("MOBILE_ONLY_CHANGES=TRUE")
if __name__=="__main__":main()
