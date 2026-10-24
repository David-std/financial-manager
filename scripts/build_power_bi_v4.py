"""Financial Manager Power BI v4: restrained, data-first editorial composition.

The user's reference boards favor large analytical plots, readable matrices and
modest chrome, rather than colored KPI accents, dummy navigation, or decoration.
Native PBIR, existing model, no Dataverse writes. Deterministic visual IDs.
"""
from __future__ import annotations
import hashlib
import importlib.util
import json
import pathlib
import shutil
import tempfile

HERE=pathlib.Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("build_v2",HERE/"build_power_bi_v2.py")
base=importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)

PAGES=base.PAGE_DIR
REPORT=base.REPORT
WIDTH,HEIGHT=1280,1140
BG="#F5F6F7"
WHITE="#FFFFFF"
INK="#233344"
GREY="#526577"
LINE="#E2E6EA"
NAVY="#244765"
SOFT="#7C8C9A"

# No multi-hued card borders or misleading decorative navigation.
class Board(base.Canvas):
    def __init__(self,pid,title,subtitle,code):
        self.pid,self.title=pid,title
        self.folder=PAGES/pid/"visuals"
        self.items={}
        self.text("eyebrow","FINANCIAL MANAGER   /   GASTOS CONFIRMADOS",23,14,980,24,10,NAVY,weight="semibold")
        self.text("report-code","ANÁLISIS  "+code,1070,14,185,24,10,GREY)
        self.text("title",title,22,43,980,42,23,INK,weight="semibold")
        self.text("subtitle",subtitle,23,84,1184,24,10,GREY)
        # A compact, genuine slicer toolbar directly below the report heading.
        self.text("filters-label","Explorar por",22,116,107,30,10,GREY,weight="semibold")
    def build(self,key,visual_type,x,y,w,h,**kwargs):
        # Leave dropdowns fully visible; avoid crowding the KPI row.
        if y >= 184: y += 20
        return super().build(key,visual_type,x,y,w,h,**kwargs)
    def chrome(self,title=None,accent=False,*,no_bg=False):
        # Use neutral, square-edged chart containers; restrained Microsoft-esque UI.
        return {
          "background":base.props(show=base.literal(True),
                                  color=base.color(WHITE),transparency=base.literal(0)),
          "border":base.props(show=base.literal(True),color=base.color(LINE),
                              radius=base.literal(2),width=base.literal(1)),
          "padding":base.props(left=base.literal(11),right=base.literal(11),
                               top=base.literal(5),bottom=base.literal(5)),
          "visualHeader":base.props(show=base.literal(False)),
          "subTitle":base.props(show=base.literal(False)),
          **({"title":base.props(show=base.literal(True),text=base.literal(title),
              fontColor=base.color(INK),fontSize=base.literal(11),
              fontFamily=base.literal("Segoe UI Semibold"),bold=base.literal(True))}
            if title else {})
        }
    def text(self,key,value,x,y,w,h,size=12,color=INK,*,weight=None):
        super().text(key,value,x,y,w,h,size,color,weight=weight)
        v=self.items[key]["visual"]
        v["visualContainerObjects"]= {
            "background":base.props(show=base.literal(False)),
            "border":base.props(show=base.literal(False)),
            "padding":base.props(left=base.literal(0),right=base.literal(0),
                                 top=base.literal(0),bottom=base.literal(0)),
            "visualHeader":base.props(show=base.literal(False)),
            "subTitle":base.props(show=base.literal(False))
        }
    def filter(self,key,label,field,x,w=207):
        self.build(key,"slicer",x,111,w,74,
          query={"Values":[field]},vobjects=self.chrome(label),
          objects={"data":base.props(mode=base.literal("Dropdown")),
                   "header":base.props(show=base.literal(False)),
                   "selection":base.props(singleSelect=base.literal(False))})
    def kpi(self,key,label,value,x,y,w,h=99):
        self.build(key,"card",x,y,w,h,
          query={"Values":[value]},vobjects=self.chrome(label),
          objects={"categoryLabels":base.props(show=base.literal(False))})
    def data(self,key,kind,title,x,y,w,h,roles,*,legend=False):
        formatters={}
        if kind in ("areaChart","stackedAreaChart","lineChart","lineClusteredColumnComboChart",
                     "donutChart","pieChart","ribbonChart","clusteredBarChart",
                     "clusteredColumnChart","stackedColumnChart","hundredPercentStackedColumnChart"):
            formatters["legend"]=base.props(show=base.literal(legend))
        self.build(key,kind,x,y,w,h,query=roles,vobjects=self.chrome(title),
                   objects=formatters or None)
    def plot(self,key,kind,title,x,y,w,h,dim,meas,*,series=None,tooltips=None):
        roles={"Category":[dim],"Y":[meas]}
        if series: roles["Series"]=[series]
        if tooltips: roles["Tooltips"]=tooltips
        self.data(key,kind,title,x,y,w,h,roles,legend=bool(series))
    def matrix(self,key,title,x,y,w,h,rows,measures,columns=None):
        roles={"Rows":rows,"Values":measures}
        if columns: roles["Columns"]=columns
        self.data(key,"pivotTable",title,x,y,w,h,roles)
    def table(self,key,title,x,y,w,h,fields):
        self.data(key,"tableEx",title,x,y,w,h,{"Values":fields})
    def save(self):
        if self.folder.exists(): shutil.rmtree(self.folder)
        self.folder.mkdir(parents=True)
        for key,item in self.items.items():
            vid=hashlib.sha1((self.pid+":"+key).encode()).hexdigest()[:20]
            item["name"]=vid
            sub=self.folder/vid
            sub.mkdir()
            (sub/"visual.json").write_text(
                 json.dumps(item,ensure_ascii=False,indent=2)+"\n",encoding="utf8")
        p=PAGES/self.pid/"page.json"
        dat=json.loads(p.read_text(encoding="utf8"))
        dat["width"]=WIDTH;dat["height"]=HEIGHT
        dat["displayOption"]="FitToWidth"
        dat["objects"]={
          "background":base.props(color=base.color(BG),transparency=base.literal(0)),
          "outspace":base.props(color=base.color(BG),transparency=base.literal(0))
        }
        p.write_text(json.dumps(dat,ensure_ascii=False,indent=2)+"\n",encoding="utf8")
        print(f"PAGE {self.title}: {len(self.items)} visual definitions")

f=base.f
c=base.c

def filters(b,arr):
    for i,(label,field) in enumerate(arr):
        b.filter("filter-"+str(i),label,field,145+i*225,216)

def overview(id):
    b=Board(id,"Visión general de gastos",
        "Pagos registrados en PEN · evolución, distribución y registro de movimientos","01")
    filters(b,[
        ("Año",c("DimDate","Year")),
        ("Mes",c("DimDate","YearMonth")),
        ("Sede",c("DimBranch","BranchName")),
        ("Categoría",c("DimCategory","CategoryName")),
        ("Ámbito",c("FactPostedExpense","ScopeLabel"))])
    for i,(title,measure) in enumerate([
       ("Gasto confirmado",f("Pagos confirmados KPI")),
       ("Número de pagos",f("Cantidad de pagos KPI")),
       ("Pago promedio",f("Ticket promedio KPI")),
       ("Gasto compartido",f("Gasto compartido KPI")),
       ("Variación interanual",f("Variación interanual lectura"))]):
        b.kpi("kpi-"+str(i),title,measure,17+i*251,184,238,96)
    b.data("monthly-combo","lineClusteredColumnComboChart",
        "Gasto mensual · volumen y valor",17,296,784,264,{
          "Category":[c("DimDate","YearMonth")],
          "Y":[f("Paid Spending")],
          "Y2":[f("Payment Count")],
          "Tooltips":[f("Average Payment")]})
    b.plot("category-rank","clusteredBarChart",
         "Categorías de mayor gasto",816,296,447,264,
         c("DimCategory","CategoryName"),f("Paid Spending"))
    b.data("scope-ring","donutChart",
        "Gasto por ámbito",17,577,307,249,{
          "Category":[c("FactPostedExpense","ScopeLabel")],
          "Y":[f("Paid Spending")]},legend=True)
    b.plot("flows","stackedAreaChart",
        "Composición mensual por tipo",338,577,469,249,
        c("DimDate","YearMonth"),f("Paid Spending"),
        series=c("FactPostedExpense","FlowClassLabel"))
    b.plot("accounts","clusteredBarChart",
        "Cuentas de origen",821,577,442,249,
        c("DimCashAccount","CashAccountName"),f("Paid Spending"))
    b.table("register","Movimientos recientes · detalle operativo",
         17,844,872,255,[
          c("FactPostedExpense","ExpenseReference"),
          c("FactPostedExpense","TransactionDate"),
          c("DimBranch","BranchName"),
          c("DimCategory","CategoryName"),
          c("FactPostedExpense","FlowClassLabel"),
          c("FactPostedExpense","AmountPEN")])
    b.plot("year-total","clusteredColumnChart",
           "Gasto por ejercicio",904,844,359,255,
           c("DimDate","Year"),f("Paid Spending"))
    return b

def branches(id):
    b=Board(id,"Sedes y movimientos",
        "Comparativo operativo: sedes, cuentas y movimientos contabilizados","02")
    filters(b,[
        ("Año",c("DimDate","Year")),
        ("Sede",c("DimBranch","BranchName")),
        ("Cuenta",c("DimCashAccount","CashAccountName")),
        ("Categoría",c("DimCategory","CategoryName")),
        ("Ámbito",c("FactPostedExpense","ScopeLabel"))])
    for i,(title,measure) in enumerate([
        ("Total pagado",f("Pagos confirmados KPI")),
        ("Asignado a sedes",f("Gasto por sede KPI")),
        ("Compartido",f("Gasto compartido KPI")),
        ("Participación compartida",f("Participación compartida"))]):
        b.kpi("kpi-"+str(i),title,measure,17+i*314,184,296,93)
    b.plot("branch-comparison","clusteredBarChart",
        "Gasto por sede (sin repartir compartidos)",17,292,597,267,
        c("DimBranch","BranchName"),f("Branch Spending"))
    b.plot("accounts","clusteredBarChart",
        "Gasto por cuenta de origen",628,292,635,267,
        c("DimCashAccount","CashAccountName"),f("Paid Spending"))
    b.data("scope-donut","donutChart",
        "Ámbito del gasto",17,577,301,244,{
          "Category":[c("FactPostedExpense","ScopeLabel")],
          "Y":[f("Paid Spending")]},legend=True)
    b.plot("branch-month","clusteredColumnChart",
        "Sedes por mes",333,577,551,244,
        c("DimDate","YearMonth"),f("Branch Spending"),
        series=c("DimBranch","BranchName"))
    b.plot("category-breakdown","clusteredBarChart",
        "Categorías en operaciones",899,577,364,244,
        c("DimCategory","CategoryName"),f("Paid Spending"))
    b.table("transactions","Registro detallado · importes en PEN",
        17,838,1246,261,[
          c("FactPostedExpense","ExpenseReference"),
          c("FactPostedExpense","TransactionDate"),
          c("DimBranch","BranchName"),
          c("DimCashAccount","CashAccountName"),
          c("DimCategory","CategoryName"),
          c("FactPostedExpense","ScopeLabel"),
          c("FactPostedExpense","AmountPEN")])
    return b

def categories(id):
    b=Board(id,"Análisis de categorías",
        "Concentración, cambios entre ejercicios y participación relativa","03")
    filters(b,[
        ("Año",c("DimDate","Year")),
        ("Categoría",c("DimCategory","CategoryName")),
        ("Sede",c("DimBranch","BranchName")),
        ("Tipo",c("FactPostedExpense","FlowClassLabel")),
        ("Ámbito",c("FactPostedExpense","ScopeLabel"))])
    for i,(title,measure) in enumerate([
        ("Total del período",f("Pagos confirmados KPI")),
        ("Año anterior",f("Gasto anterior KPI")),
        ("Cambio frente al anterior",f("Variación interanual lectura")),
        ("Categorías con actividad",f("Categorías con pagos"))]):
        b.kpi("kpi-"+str(i),title,measure,17+i*314,184,296,93)
    b.plot("rank","clusteredBarChart",
        "Ranking de categorías · PEN",17,293,738,275,
        c("DimCategory","CategoryName"),f("Paid Spending"))
    b.data("tree","treemap",
        "Participación de las categorías",769,293,494,275,{
          "Group":[c("DimCategory","CategoryName")],
          "Values":[f("Paid Spending")]})
    b.plot("category-time","stackedAreaChart",
        "Evolución y composición",17,586,567,217,
        c("DimDate","YearMonth"),f("Paid Spending"),
        series=c("FactPostedExpense","FlowClassLabel"))
    b.data("flow-mix","donutChart",
        "Operación, capital e impuestos",599,586,307,217,{
          "Category":[c("FactPostedExpense","FlowClassLabel")],
          "Y":[f("Paid Spending")]},legend=True)
    b.plot("year-comparison","clusteredColumnChart",
        "Total anual",921,586,342,217,
        c("DimDate","Year"),f("Paid Spending"))
    b.matrix("category-matrix",
       "Detalle comparativo · categoría / monto / variación / peso",
       17,820,1246,279,
       [c("DimCategory","CategoryName")],
       [f("Paid Spending"),f("Prior Year Spending"),
        f("Year-over-Year Change %"),f("Participación categoría")])
    return b

def trends(id):
    b=Board(id,"Tendencias y estacionalidad",
        "Seguimiento mensual, estructura del gasto y evolución acumulada","04")
    filters(b,[
        ("Año",c("DimDate","Year")),
        ("Mes",c("DimDate","YearMonth")),
        ("Tipo",c("FactPostedExpense","FlowClassLabel")),
        ("Sede",c("DimBranch","BranchName")),
        ("Ámbito",c("FactPostedExpense","ScopeLabel"))])
    for i,(title,measure) in enumerate([
        ("Gasto del período",f("Pagos confirmados KPI")),
        ("Acumulado anual",f("Gasto acumulado YTD")),
        ("Promedio mensual",f("Promedio mensual pagado")),
        ("Comparación interanual",f("Variación interanual lectura"))]):
        b.kpi("kpi-"+str(i),title,measure,17+i*314,184,296,93)
    b.plot("main-trend","areaChart",
        "Evolución mensual del gasto",17,293,860,287,
        c("DimDate","YearMonth"),f("Paid Spending"))
    b.plot("by-year","clusteredColumnChart",
        "Comparación anual",891,293,372,287,
        c("DimDate","Year"),f("Paid Spending"))
    b.data("monthly-volume","lineClusteredColumnComboChart",
       "Pagos por mes · frecuencia e importe",17,597,497,254,{
         "Category":[c("DimDate","YearMonth")],
         "Y":[f("Paid Spending")],
         "Y2":[f("Payment Count")]})
    b.plot("types","hundredPercentStackedColumnChart",
        "Estructura mensual por clasificación",529,597,466,254,
        c("DimDate","YearMonth"),f("Paid Spending"),
        series=c("FactPostedExpense","FlowClassLabel"))
    b.data("scope","donutChart",
       "Ámbito del gasto",1010,597,253,254,{
        "Category":[c("FactPostedExpense","ScopeLabel")],
        "Y":[f("Paid Spending")]},legend=True)
    b.matrix("year-month-grid",
       "Calendario de gasto · año y mes",17,867,798,232,
       [c("DimDate","Year")],
       [f("Paid Spending"),f("Payment Count")],
       columns=[c("DimDate","Month")])
    b.plot("flow-total","clusteredBarChart",
       "Gasto por clase de flujo",831,867,432,232,
       c("FactPostedExpense","FlowClassLabel"),f("Paid Spending"))
    return b

def main():
    ids=["ac21e26d330743fa8b79","de2b78eaf1b44c2a9485",
         "f9be472dc34d4e5cab27","f1a74ce8854f434789d2"]
    existing=list(PAGES.glob("*/visuals/*/visual.json"))
    if len(existing) < 70:
        raise RuntimeError("Unexpected PBIR state; refusing to replace non-v3 report")
    backup=pathlib.Path(tempfile.mkdtemp(prefix="FM-before-report-v4-"))
    shutil.copytree(REPORT/"definition",backup/"definition")
    print("PRIVATE_PRECHANGE_BACKUP="+str(backup))
    result=[overview(ids[0]),branches(ids[1]),categories(ids[2]),trends(ids[3])]
    for b in result:
        for v in b.items.values():
            position=v["position"]
            if position["x"]<0 or position["y"]<0 or position["x"]+position["width"]>WIDTH or position["y"]+position["height"]>HEIGHT:
                raise RuntimeError("Visual falls outside canvas")
        b.save()
    print("TOTAL_NATIVE_ELEMENTS="+str(sum(len(b.items) for b in result)))

if __name__=="__main__":
    main()
