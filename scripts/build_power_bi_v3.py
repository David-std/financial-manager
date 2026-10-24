"""Design-first Power BI v3 for Financial Manager.

Native PBIR visuals only, deterministic generation, repository-safe.
Run from repo root: python scripts/build_power_bi_v3.py
This never calls Dataverse; no fixture data are committed.
"""
from __future__ import annotations
import importlib.util
import hashlib
import json
import pathlib
import shutil
import tempfile

HERE=pathlib.Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("fmv2",HERE/"build_power_bi_v2.py")
m=importlib.util.module_from_spec(spec)
assert spec.loader
spec.loader.exec_module(m)

ROOT=m.ROOT
PAGES=m.PAGE_DIR
MODEL=m.MODEL
NAVY="#162B48"
NAVY2="#234A75"
TEAL="#14A897"
BLUE="#307BBF"
PURPLE="#665BC5"
ORANGE="#E3A448"
PALE="#EAF3F8"
DARK="#203554"
MUTED="#61758D"
WHITE="#FFFFFF"
BACKGROUND="#F1F5FA"
HEIGHT=944

class ExecutiveCanvas(m.Canvas):
    def __init__(self,pid,title,subtitle,index,accent=BLUE):
        self.pid,self.title,self.accent=pid,title,accent
        self.folder=PAGES/pid/"visuals"
        self.items={}
        self.block("top-main",0,0,1280,91,NAVY,z=10)
        self.block("top-accent",0,0,9,91,accent,z=20)
        self.text("title",title.upper(),28,17,820,39,24,WHITE,weight="semibold")
        self.text("subtitle",subtitle,29,56,885,25,10,"#E1EBF7")
        self.text("brand","FM  /  FINANCIAL MANAGER",984,24,274,40,12,WHITE,weight="semibold")
        self.block("header-rule",20,91,1240,3,accent,z=20)
        self.text("issue",f"REPORTE EJECUTIVO   /   0{index}",27,99,330,23,9,MUTED,weight="semibold")

    def block(self,key,x,y,w,h,fill,z=100):
        if h < 18:
            return  # Avoid invisible 1–3px textboxes and PBIR scroll warnings.
        self.text(key,"",x,y,w,h,8,fill,bg=fill)
        self.items[key]["position"]["z"]=z
    def text(self,key,value,x,y,w,h,size=12,ink=DARK,*,bg=None,weight=None):
        super().text(key,value,x,y,w,h,size,ink,bg=bg,weight=weight)
        if not value:
            self.items[key]["visual"]["objects"]["general"] = m.props(
                paragraphs=[{"textRuns":[{"value":" ","textStyle":{"fontSize":"2pt","color":ink}}],
                             "horizontalTextAlignment":"left"}])
    def caption(self,key,text,x,y,w,h=19,col=MUTED,size=10):
        self.text(key,text,x,y,w,h,size,col)
    def slicer(self,key,label,col,x,y,w=220,h=66):
        self.build(key,"slicer",x,y,w,h,
                   query={"Values":[col]},
                   vobjects=self.chrome(label),
                   objects={"data":m.props(mode=m.literal("Dropdown")),
                            "header":m.props(show=m.literal(False)),
                            "selection":m.props(singleSelect=m.literal(False))})
    def kpi(self,key,label,measure,x,y,w=221,h=115,accent=BLUE,note=None):
        self.block(key+"-accent",x,y,5,h,accent,z=300)
        self.build(key,"card",x+5,y,w-5,h,
                   query={"Values":[measure]},
                   vobjects=self.chrome(label),
                   objects={"categoryLabels":m.props(show=m.literal(False))})
        if note:
            self.caption(key+"-note",note,x+15,y+h-23,w-25,18,size=8)
    def chart(self,key,typ,title,x,y,w,h,dimension,measures,*,series=None,
              tooltips=None):
        state={"Category":[dimension],"Y":measures}
        if series:
            state["Series"]=[series]
        if tooltips:
            state["Tooltips"]=tooltips
        self.build(key,typ,x,y,w,h,
                   query=state,
                   vobjects=self.chrome(title),
                   objects={"legend":m.props(show=m.literal(bool(series or len(measures)>1)))})
    def special(self,key,typ,title,x,y,w,h,roles,legend=False):
        options = {"legend":m.props(show=m.literal(legend))} if typ in {"donutChart","pieChart","lineChart","areaChart","stackedAreaChart","clusteredBarChart","clusteredColumnChart"} else None
        self.build(key,typ,x,y,w,h,query=roles,vobjects=self.chrome(title),
                   objects=options)
    def matrix(self,key,title,x,y,w,h,rows,vals,columns=None):
        roles={"Rows":rows,"Values":vals}
        if columns:
            roles["Columns"]=columns
        self.special(key,"pivotTable",title,x,y,w,h,roles)
    def table(self,key,title,x,y,w,h,*fields):
        self.special(key,"tableEx",title,x,y,w,h,{"Values":list(fields)})
    def section(self,key,title,x,y,w,accent=TEAL):
        self.block(key+"bar",x,y+4,5,23,accent,z=150)
        self.text(key+"title",title,x+13,y,w-13,30,14,NAVY,weight="semibold")
    def rail(self):
        self.block("nav-rail",0,118,207,795,NAVY2,z=10)
        self.text("rail-brand","ANÁLISIS FINANCIERO",14,135,180,42,12,WHITE,weight="semibold")
        self.caption("rail-info","Gastos confirmados · PEN",14,180,178,31,col="#D0E6F6")
        for i,(name,icon) in enumerate([
            ("01  RESUMEN","●"),("02  SEDES","●"),("03  CATEGORÍAS","●"),("04  TENDENCIAS","●")]):
            selected=name.startswith("0"+str({ "Sedes y movimientos":2,
                "Análisis de categorías":3,"Tendencias de pagos":4}.get(self.title,1)))
            self.block("rail-row-"+str(i),14,257+i*63,181,49,
                       self.accent if selected else "#2C5076",z=25)
            self.text("rail-label-"+str(i),name,23,271+i*63,164,24,
                      11,WHITE,weight="semibold" if selected else None)
        self.block("rail-footer",17,799,172,1,"#6483A1")
        self.caption("rail-guard","Sin presupuestos aprobados",18,823,183,28,col="#D9E6F2",size=9)
    def finish(self):
        self.folder.mkdir(parents=True,exist_ok=True)
        for key,item in self.items.items():
            vid=hashlib.sha1((self.pid+":"+key).encode()).hexdigest()[:20]
            item["name"]=vid
            dest=self.folder/vid
            dest.mkdir(parents=True,exist_ok=True)
            (dest/"visual.json").write_text(
                json.dumps(item,ensure_ascii=False,indent=2)+"\n",encoding="utf8")
        info_path=PAGES/self.pid/"page.json"
        info=json.loads(info_path.read_text(encoding="utf8"))
        info["height"]=HEIGHT
        info["displayOption"]="FitToWidth"
        info["objects"]={
          "background":m.props(color=m.color(BACKGROUND),transparency=m.literal(0)),
          "outspace":m.props(color=m.color(BACKGROUND),transparency=m.literal(0))
        }
        info_path.write_text(json.dumps(info,ensure_ascii=False,indent=2)+"\n",
                             encoding="utf8")
        print(f"PAGE {self.title}: {len(self.items)} items")

def f(name,label=None):
    return m.f(name,label)
def col(tab,name,label=None):
    return m.c(tab,name,label)

def overview(pid):
    x=ExecutiveCanvas(pid,"Resumen de gastos","Panorama de gasto contabilizado, composición y comportamiento por periodo",1)
    filters=[("Año",col("DimDate","Year","Año")),
             ("Mes",col("DimDate","YearMonth","Mes")),
             ("Sede",col("DimBranch","BranchName","Sede")),
             ("Categoría",col("DimCategory","CategoryName","Categoría")),
             ("Ámbito",col("FactPostedExpense","ScopeLabel","Ámbito"))]
    for i,(label,field) in enumerate(filters):
        x.slicer("filter-"+str(i),label,field,20+i*251,132,237)
    for i,(key,title,measure,color_,note) in enumerate([
      ("spend","TOTAL CONFIRMADO",f("Pagos confirmados KPI"),BLUE,"Montos Posted · PEN"),
      ("count","MOVIMIENTOS",f("Cantidad de pagos KPI"),TEAL,"Pagos contabilizados"),
      ("avg","TICKET PROMEDIO",f("Ticket promedio KPI"),PURPLE,"Promedio por movimiento"),
      ("shared","GASTO COMPARTIDO",f("Gasto compartido KPI"),ORANGE,"Sin asignar a sedes"),
      ("yoy","CAMBIO INTERANUAL",f("Variación interanual lectura"),BLUE,"Respecto al año previo")
    ]):
        x.kpi(key,title,measure,20+i*251,223,237,112,color_,note)
    x.section("hero","EVOLUCIÓN DEL GASTO",20,350,830)
    x.chart("hero-area","areaChart","Gasto mensual confirmado",20,389,806,269,
            col("DimDate","YearMonth","Mes"),
            [f("Paid Spending","Gasto · PEN")])
    x.section("ranking","PRINCIPALES CATEGORÍAS",841,350,405,ORANGE)
    x.chart("ranking-bars","clusteredBarChart","Concentración por categoría",841,389,419,269,
            col("DimCategory","CategoryName","Categoría"),
            [f("Paid Spending","Total")])
    x.section("mix","COMPOSICIÓN",20,675,355,PURPLE)
    x.special("mix-donut","donutChart","Por ámbito",20,710,310,198,
              {"Category":[col("FactPostedExpense","ScopeLabel","Ámbito")],
               "Y":[f("Paid Spending","Total")]},True)
    x.section("flows","CLASES DE GASTO",345,675,360,TEAL)
    x.chart("flows-bars","clusteredBarChart","Operación / capital / impuestos",345,710,341,198,
            col("FactPostedExpense","FlowClassLabel","Tipo"),
            [f("Paid Spending","Total")])
    x.section("recent","MOVIMIENTOS RECIENTES",701,675,555,BLUE)
    x.table("recent-table","Detalle confirmado · PEN",701,710,559,198,
            col("FactPostedExpense","ExpenseReference","Referencia"),
            col("FactPostedExpense","TransactionDate","Fecha"),
            col("DimCategory","CategoryName","Categoría"),
            col("FactPostedExpense","AmountPEN","Monto PEN"))
    return x

def branches(pid):
    x=ExecutiveCanvas(pid,"Sedes y movimientos","Control operativo por sede, fuente de fondos y ámbito de gasto",2,TEAL)
    x.rail()
    for i,(label,field) in enumerate([
      ("Año",col("DimDate","Year","Año")),
      ("Sede",col("DimBranch","BranchName","Sede")),
      ("Cuenta",col("DimCashAccount","CashAccountName","Cuenta")),
      ("Ámbito",col("FactPostedExpense","ScopeLabel","Ámbito"))]):
        x.slicer("s-"+str(i),label,field,223+i*259,134,243)
    for i,(key,title,measure,clr) in enumerate([
      ("total","TOTAL CONFIRMADO",f("Pagos confirmados KPI"),BLUE),
      ("local","GASTO POR SEDE",f("Gasto por sede KPI"),TEAL),
      ("shared","COMPARTIDO",f("Gasto compartido KPI"),ORANGE),
      ("pct","% COMPARTIDO",f("Participación compartida"),PURPLE)]):
        x.kpi(key,title,measure,223+i*259,231,243,115,clr)
    x.section("branch-header","COMPARACIÓN DE SEDES",223,363,563,TEAL)
    x.chart("branch-bar","clusteredBarChart","Movimientos por sede",223,399,571,251,
            col("DimBranch","BranchName","Sede"),
            [f("Paid Spending","Monto")])
    x.section("sources-head","FUENTES DE PAGO",807,363,441,ORANGE)
    x.chart("accounts","clusteredBarChart","Cuentas internas de origen",807,399,441,251,
            col("DimCashAccount","CashAccountName","Cuenta"),
            [f("Paid Spending","Monto")])
    x.section("breakdown-head","DESCOMPOSICIÓN DE GASTOS",223,669,505,PURPLE)
    x.special("decomposition","decompositionTreeVisual","Explorar factores",
              223,704,514,210,{
               "Analyze":[f("Paid Spending","Gasto")],
               "ExplainBy":[col("DimBranch","BranchName","Sede"),
                            col("DimCategory","CategoryName","Categoría"),
                            col("FactPostedExpense","FlowClassLabel","Tipo")]})
    x.section("detail-head","DETALLE CONFIRMADO",749,669,503,BLUE)
    x.table("branch-details","Registro de movimientos",749,704,499,210,
            col("FactPostedExpense","ExpenseReference","Referencia"),
            col("DimBranch","BranchName","Sede"),
            col("DimCashAccount","CashAccountName","Cuenta"),
            col("FactPostedExpense","AmountPEN","Importe"))
    return x

def categories(pid):
    x=ExecutiveCanvas(pid,"Análisis de categorías","Ranking, composición del gasto y variaciones de los conceptos",3,PURPLE)
    x.rail()
    for i,(label,field) in enumerate([
      ("Año",col("DimDate","Year","Año")),
      ("Categoría",col("DimCategory","CategoryName","Categoría")),
      ("Sede",col("DimBranch","BranchName","Sede")),
      ("Tipo",col("FactPostedExpense","FlowClassLabel","Tipo"))]):
        x.slicer("s-"+str(i),label,field,223+i*259,132,243)
    for i,(key,title,measure,clr) in enumerate([
      ("now","PERIODO ACTUAL",f("Pagos confirmados KPI"),BLUE),
      ("past","PERIODO PREVIO",f("Gasto anterior KPI"),TEAL),
      ("yoy","VARIACIÓN INTERANUAL",f("Variación interanual lectura"),PURPLE),
      ("active","CATEGORÍAS ACTIVAS",f("Categorías con pagos"),ORANGE)]):
        x.kpi(key,title,measure,223+i*259,227,243,109,clr)
    x.section("treemap-header","MAPA DE CONCENTRACIÓN",223,350,596,PURPLE)
    x.special("category-treemap","treemap","Distribución de categorías",223,388,610,289,{
      "Group":[col("DimCategory","CategoryName","Categoría")],
      "Values":[f("Paid Spending","Total PEN")]})
    x.section("rank-head","RANKING DE GASTOS",846,350,397,BLUE)
    x.chart("rank","clusteredBarChart","Categorías con mayor gasto",846,388,402,289,
            col("DimCategory","CategoryName","Categoría"),
            [f("Paid Spending","Total")])
    x.section("matrix-head","MATRIZ COMPARATIVA",223,692,1020,TEAL)
    x.matrix("matrix","Actual · año anterior · variación",223,730,1025,185,
             [col("DimCategory","CategoryName","Categoría")],
             [f("Paid Spending","Actual PEN"),
              f("Prior Year Spending","Anterior PEN"),
              f("Year-over-Year Change %","Variación"),
              f("Participación categoría","Participación")])
    return x

def trends(pid):
    x=ExecutiveCanvas(pid,"Tendencias de pagos","Estacionalidad, evolución acumulada y composición mensual",4,ORANGE)
    for i,(label,field) in enumerate([
      ("Año",col("DimDate","Year","Año")),
      ("Mes",col("DimDate","YearMonth","Mes")),
      ("Tipo",col("FactPostedExpense","FlowClassLabel","Tipo")),
      ("Sede",col("DimBranch","BranchName","Sede")),
      ("Ámbito",col("FactPostedExpense","ScopeLabel","Ámbito"))]):
        x.slicer("s-"+str(i),label,field,20+i*251,130,237)
    x.section("hero-head","SEGUIMIENTO MENSUAL",20,218,800,ORANGE)
    x.chart("trend-area","areaChart","Gasto registrado por mes",20,255,792,328,
            col("DimDate","YearMonth","Mes"),
            [f("Paid Spending","Gasto PEN")])
    x.section("right-head","INDICADORES DE TENDENCIA",832,218,411,TEAL)
    for i,(key,title,measure,color_) in enumerate([
      ("ytd","ACUMULADO ANUAL",f("Gasto acumulado YTD"),BLUE),
      ("avg","PROMEDIO MENSUAL",f("Promedio mensual pagado"),PURPLE),
      ("yoy","COMPARACIÓN ANUAL",f("Variación interanual lectura"),ORANGE)]):
        x.kpi(key,title,measure,832,255+i*109,420,101,color_)
    x.section("monthly-head","COMPARATIVO ANUAL",20,606,608,BLUE)
    x.chart("year-column","clusteredColumnChart","Gasto por año",20,641,600,272,
            col("DimDate","Year","Año"),
            [f("Paid Spending","Total PEN")])
    x.section("class-head","MEZCLA DE PAGOS",638,606,355,PURPLE)
    x.special("flow-donut","donutChart","Por clasificación",638,641,351,272,{
        "Category":[col("FactPostedExpense","FlowClassLabel","Clase")],
        "Y":[f("Paid Spending","Total PEN")]},True)
    x.section("progress-head","DISTRIBUCIÓN",1004,606,245,TEAL)
    x.chart("class-column","hundredPercentStackedColumnChart","Proporción mensual",
            1004,641,245,272,col("DimDate","YearMonth","Mes"),
            [f("Paid Spending","Total")],
            series=col("FactPostedExpense","ScopeLabel","Ámbito"))
    return x

def main():
    p=PAGES
    ids={
      "overview":"ac21e26d330743fa8b79",
      "branches":"de2b78eaf1b44c2a9485",
      "categories":"f9be472dc34d4e5cab27",
      "trends":"f1a74ce8854f434789d2"
    }
    originals=[f.parent.name for f in p.glob("*/visuals/*/visual.json")]
    assert 50<=len(originals)<=160, f"Unexpected existing count {len(originals)}"
    backup=pathlib.Path(tempfile.mkdtemp(prefix="FM-before-executive-v3-"))
    shutil.copytree(m.REPORT/"definition",backup/"definition")
    print("PRIVATE_BACKUP="+str(backup))
    builders=[overview(ids["overview"]),branches(ids["branches"]),
              categories(ids["categories"]),trends(ids["trends"])]
    for page in builders:
        shutil.rmtree(page.folder)
        page.finish()
    total=sum(len(q.items) for q in builders)
    print("EXECUTIVE_V3_VISUAL_ELEMENTS="+str(total))
    return total

if __name__=="__main__":
    main()
