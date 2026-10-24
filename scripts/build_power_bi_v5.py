"""Executive v5: asymmetrical editorial layouts, measured pastel theme, native BI visuals.
Preserves Dataverse measures and data. Never writes synthetic records to Dataverse.
"""
import importlib.util,json,hashlib,pathlib,tempfile,shutil
here=pathlib.Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("v4",here/"build_power_bi_v4.py")
v4=importlib.util.module_from_spec(spec);spec.loader.exec_module(v4)
b2=v4.base; pages=v4.PAGES; report=v4.REPORT
W,H=1280,1160
INK="#303B45"; MUTED="#737E88"; PAPER="#F6F6F3"; WHITE="#FFFFFF"
SKY="#557E9A"; MINT="#82A795"; ROSE="#C79891"; LILAC="#A59AC3"
SAND="#C2AB86"; LINE="#E4E8E8"
def place(b,key,x,y,w,h):
    b.items[key]["position"].update(x=x,y=y,width=w,height=h)
def block(b,key,x,y,w,h,tint):
    if w < 5 or h < 5:
        return  # One-pixel textboxes create scrollbars; do not simulate borders.
    b.text(key," ",x,y,w,h,3,tint)
    v=b.items[key];v["position"]["z"]=1
    o=v["visual"]["visualContainerObjects"]
    o["background"]=b2.props(show=b2.literal(True),color=b2.color(tint),transparency=b2.literal(0))
    o["border"]=b2.props(show=b2.literal(False))
def tag(b,key,label,x,y,w,shade=INK,sz=11):
    b.text(key,label,x,y,w,23,sz,shade,weight="semibold")
def text(b,key,label,x,y,w,h=25,size=10,tint=MUTED):
    b.text(key,label,x,y,w,h,size,tint)
def shell(b,title,desc,index,hue):
    for k in ("eyebrow","report-code","title","subtitle","filters-label"):b.items.pop(k,None)
    block(b,"paper",0,0,W,H,PAPER);block(b,"header",0,0,W,97,WHITE)
    block(b,"chapter-color",23,19,97,28,hue)
    tag(b,"chapter","SECCIÓN "+index,29,20,86,WHITE,10)
    tag(b,"brand","Financial Manager",139,15,540,INK,14)
    tag(b,"heading",title,22,44,940,INK,21)
    place(b,"heading",22,44,940,36)
    text(b,"desc",desc,22,82,1000,21,9,MUTED)
    tag(b,"currency","PAGOS · PEN",1074,55,175,MUTED,10)
    # keep genuine native filter controls, no fake navigation
    for i in range(5):
        v=b.items["filter-"+str(i)]
        v["visual"]["visualContainerObjects"]["subTitle"]=b2.props(show=b2.literal(False))
def kpis(b,n,x,y,w):
    block(b,"metric-back",x,y,w,107,WHITE)
    step=w/n
    for i in range(n):
        place(b,"kpi-"+str(i),x+5+i*step,y+5,step-11,95)
        obj=b.items["kpi-"+str(i)]["visual"]["visualContainerObjects"]
        obj["background"]=b2.props(show=b2.literal(True),color=b2.color(WHITE),transparency=b2.literal(0))
        obj["border"]=b2.props(show=b2.literal(False))
        if i:block(b,"separator-"+str(i),x+i*step,y+20,1,65,LINE)
def side(b,shade):
    block(b,"side-panel",20,230,224,904,shade)
    tag(b,"side-title","EXPLORAR DATOS",36,247,185,INK,12)
    text(b,"side-description","Filtra el informe por período y segmento",36,281,174,43,9)
    for i in range(5):place(b,"filter-"+str(i),32,343+i*91,200,74)
    block(b,"side-foot",31,965,202,152,WHITE)
    tag(b,"side-foot-title","Criterio del informe",42,982,178,INK,10)
    text(b,"side-foot-note","Solo pagos contabilizados en PEN. Los gastos compartidos no se asignan arbitrariamente a una sede.",42,1016,176,95,9)
def filters_top(b,tint,y=222):
    block(b,"filters-back",20,y,1240,109,tint)
    tag(b,"filters-title","SELECCIÓN DEL ANÁLISIS",31,y+9,352,INK,10)
    for i in range(5):place(b,"filter-"+str(i),31+i*246,y+32,232,74)
def graph(b,key,x,y,w,h):place(b,key,x,y,w,h)
def overview(id):
    b=v4.overview(id);shell(b,"Panorama de gastos","Visión ejecutiva · evolución, categorías y trazabilidad","01",SKY)
    kpis(b,5,20,109,1240);side(b,"#E8EFEC")
    graph(b,"monthly-combo",260,241,663,316);graph(b,"category-rank",939,241,321,316)
    graph(b,"scope-ring",260,583,276,233);graph(b,"flows",547,583,467,233);graph(b,"accounts",1025,583,235,233)
    graph(b,"register",260,839,725,292);graph(b,"year-total",998,839,262,292)
    tag(b,"monthly-heading","EVOLUCIÓN DEL GASTO",265,216,390,SKY,10)
    tag(b,"compo-heading","DISTRIBUCIÓN Y ORIGEN",265,562,430,SKY,10)
    tag(b,"last-heading","MOVIMIENTOS Y EJERCICIOS",265,818,400,SKY,10)
    return b
def branches(id):
    b=v4.branches(id);shell(b,"Sedes y movimientos","Centro operativo · distribución, cuentas y detalle","02",MINT)
    kpis(b,4,259,109,1001);side(b,"#F0ECE7")
    graph(b,"branch-comparison",259,241,607,298);graph(b,"accounts",880,241,380,298)
    graph(b,"scope-donut",259,563,253,229);graph(b,"branch-month",526,563,453,229)
    graph(b,"category-breakdown",991,563,269,229)
    graph(b,"transactions",259,817,1001,314)
    tag(b,"branch-title","DISTRIBUCIÓN POR SEDE",266,219,430,MINT,10)
    tag(b,"monthly-title","ESTACIONALIDAD Y ÁMBITO",266,542,450,MINT,10)
    tag(b,"records-title","REGISTRO DE MOVIMIENTOS",266,796,440,MINT,10)
    return b
def categories(id):
    b=v4.categories(id);shell(b,"Categorías de gasto","Concentración y análisis de los conceptos financieros","03",LILAC)
    kpis(b,4,20,109,1240);filters_top(b,"#EFECF3",222)
    graph(b,"rank",20,353,584,296);graph(b,"tree",618,353,642,296)
    graph(b,"category-time",20,668,555,225);graph(b,"flow-mix",590,668,304,225)
    b.items.pop("year-comparison")
    b.data("decomposition","decompositionTreeVisual","Explorar factores del gasto",
       908,668,352,225,{"Analyze":[b2.f("Paid Spending")],
       "ExplainBy":[b2.c("DimCategory","CategoryName"),b2.c("DimBranch","BranchName"),
       b2.c("FactPostedExpense","FlowClassLabel"),b2.c("FactPostedExpense","ScopeLabel")]})
    graph(b,"category-matrix",20,924,1240,210)
    tag(b,"ranking-label","RANKING Y CONCENTRACIÓN",25,330,408,LILAC,10)
    tag(b,"matrix-label","DESGLOSE Y COMPARACIONES",25,901,421,LILAC,10)
    return b
def trends(id):
    b=v4.trends(id);shell(b,"Tendencias y estacionalidad","Serie temporal, mezcla de gastos y comparativos","04",ROSE)
    filters_top(b,"#E7EEF3",105)
    graph(b,"main-trend",20,230,871,346);graph(b,"by-year",905,230,355,346)
    kpis(b,4,20,610,1240)
    graph(b,"monthly-volume",20,743,472,227);graph(b,"types",504,743,499,227)
    graph(b,"scope",1015,743,245,227)
    graph(b,"year-month-grid",20,998,837,137);graph(b,"flow-total",869,998,391,137)
    tag(b,"series-label","EVOLUCIÓN MENSUAL",25,204,428,ROSE,10)
    tag(b,"magnitude-label","MÉTRICAS DEL PERÍODO",25,586,400,ROSE,10)
    tag(b,"matrix-label","DISTRIBUCIÓN TEMPORAL",25,977,427,ROSE,10)
    return b
def main():
    ids=["ac21e26d330743fa8b79","de2b78eaf1b44c2a9485","f9be472dc34d4e5cab27","f1a74ce8854f434789d2"]
    n=len(list(pages.glob("*/visuals/*/visual.json")))
    if not 70<=n<=175:raise RuntimeError(f"Unexpected PBIR state {n}")
    backup=pathlib.Path(tempfile.mkdtemp(prefix="FM-PBIR-before-v5-"))
    shutil.copytree(report/"definition",backup/"definition")
    print("BACKUP="+str(backup))
    bs=[overview(ids[0]),branches(ids[1]),categories(ids[2]),trends(ids[3])]
    for b in bs:
        for k,v in b.items.items():
            p=v["position"]
            if p["x"]<0 or p["y"]<0 or p["x"]+p["width"]>W or p["y"]+p["height"]>H:raise ValueError((b.title,k,p))
        b.save()
        page=pages/b.pid/"page.json";data=json.loads(page.read_text(encoding="utf8"))
        data["width"],data["height"]=W,H
        data["displayOption"]="FitToWidth"
        data["objects"]={"background":b2.props(color=b2.color(PAPER),transparency=b2.literal(0)),
                         "outspace":b2.props(color=b2.color(PAPER),transparency=b2.literal(0))}
        page.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf8")
        print("PAGE="+b.title+" ITEMS="+str(len(b.items)))
    theme=report/"StaticResources"/"RegisteredResources"/"FinancialManager-Executive-v2.json"
    theme.write_text(json.dumps({"name":theme.name,
       "dataColors":[SKY,MINT,ROSE,LILAC,SAND,"#7E92A5","#A4ADA5","#C9B5A3"],
       "background":PAPER,"foreground":INK,"tableAccent":SKY,
       "good":MINT,"neutral":SAND,"bad":ROSE},indent=2)+"\n",encoding="utf8")
    print("TOTAL_ELEMENTS="+str(sum(len(b.items) for b in bs)))
if __name__=="__main__":main()
