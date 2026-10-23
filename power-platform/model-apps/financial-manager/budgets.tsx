import React, { useEffect, useMemo, useState } from "react";
import { Badge, Button, Card, CardHeader, Input, Spinner, Text, makeStyles, tokens } from "@fluentui/react-components";
import { ArrowClockwiseRegular, OpenRegular, MoneyRegular, CalendarRegular, ShieldLockRegular } from "@fluentui/react-icons";
import type { GeneratedComponentProps, fmg_budget, fmg_budgetallocation } from "./RuntimeTypes";

type B = fmg_budget;
type A = fmg_budgetallocation;
type State = { loading: boolean; budgets: B[]; allocations: A[]; error: string; limited: boolean };
const cache = "__financialManagerBudgetPage2026";
const useStyles = makeStyles({
  root: { display: "flex", flexDirection: "column", gap: tokens.spacingVerticalL, padding: tokens.spacingHorizontalXXL, backgroundColor: tokens.colorNeutralBackground2, color: tokens.colorNeutralForeground1, minHeight: "100%", boxSizing: "border-box" },
  heading: { fontSize: tokens.fontSizeHero700, fontWeight: tokens.fontWeightSemibold },
  top: { display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: tokens.spacingHorizontalM },
  description: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase300 },
  cards: { display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: tokens.spacingHorizontalM },
  big: { fontSize: tokens.fontSizeHero600, fontWeight: tokens.fontWeightSemibold },
  toolbar: { display: "flex", gap: tokens.spacingHorizontalM, flexWrap: "wrap", alignItems: "center" },
  panel: { backgroundColor: tokens.colorNeutralBackground1, padding: tokens.spacingHorizontalM, borderRadius: tokens.borderRadiusMedium },
  tableContainer: { overflowX: "auto", backgroundColor: tokens.colorNeutralBackground1 },
  table: { borderCollapse: "collapse", minWidth: "710px", width: "100%" },
  cell: { padding: tokens.spacingHorizontalM, borderBottom: "1px solid " + tokens.colorNeutralStroke2, textAlign: "left" },
  th: { fontWeight: tokens.fontWeightSemibold, backgroundColor: tokens.colorNeutralBackground3 },
  info: { padding: tokens.spacingHorizontalM, backgroundColor: tokens.colorNeutralBackground1 },
  error: { color: tokens.colorPaletteRedForeground1 }
});
function idFromLookup(value?: string): string {
  if (!value) return "";
  const valueMatch = value.match(/[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}/i);
  return valueMatch ? valueMatch[0].toLowerCase() : "";
}
function displayAmount(value: number): string {
  return "S/ " + value.toLocaleString("es-PE", {minimumFractionDigits:2,maximumFractionDigits:2});
}
function displayStatus(value: number): string {
  const map: Record<number,string> = {797280400:"Borrador",797280401:"Presentado",797280402:"Aprobado",797280403:"Rechazado",797280404:"Sustituido"};
  return map[value] || "Sin estado";
}
function navigateBudget(id: string): void {
  const xrm=(window as any).Xrm;
  if (xrm?.Navigation?.navigateTo) void xrm.Navigation.navigateTo({pageType:"entityrecord",entityName:"fmg_budget",entityId:id});
}
const BudgetPage = ({dataApi}:GeneratedComponentProps) => {
  const classes=useStyles();
  const [state,setState]=useState<State>(() => (window as any)[cache] ?? {loading:true,budgets:[],allocations:[],error:"",limited:false});
  const [reload,setReload]=useState(0);
  const [year,setYear]=useState("all");
  const [search,setSearch]=useState("");
  const [filter,setFilter]=useState("all");
  const ready=!!dataApi;
  useEffect(() => {
    if (!ready) return;
    let active=true;
    const shared=window as any;
    if (reload===0 && shared[cache]) {setState(shared[cache]);return;}
    setState(s=>({...s,loading:true,error:""}));
    const key=cache+"_inflight";
    if(!shared[key]) shared[key]=Promise.all([
      dataApi.queryTable("fmg_budget",{select:["fmg_budgetid","fmg_name","fmg_year","fmg_version","fmg_status","fmg_scope","fmg_effectivefrommonth","_fmg_branch_value"],orderBy:"fmg_year desc",pageSize:200}),
      dataApi.queryTable("fmg_budgetallocation",{select:["fmg_budgetallocationid","_fmg_budget_value","fmg_month","fmg_amount","_fmg_category_value"],pageSize:1000})
    ]).then(([b,a])=>{
      const next:State={loading:false,budgets:b.rows,allocations:a.rows,error:"",limited:b.hasMoreRows||a.hasMoreRows};
      shared[cache]=next;return next;
    }).catch((err:unknown)=>{
      const next:State={loading:false,budgets:[],allocations:[],limited:false,error:err instanceof Error?err.message:"No se pudieron cargar los presupuestos."};
      shared[cache]=next;return next;
    }).finally(()=>{shared[key]=null});
    (shared[key] as Promise<State>).then(result=>{if(active)setState(result)});
    return ()=>{active=false};
  },[ready,reload]);
  const counts=useMemo(()=>{
    const sums=new Map<string,number>();
    for(const row of state.allocations) {
      const id=idFromLookup(row._fmg_budget_value as unknown as string);
      sums.set(id,(sums.get(id)||0)+(Number(row.fmg_amount)||0));
    }
    return sums;
  },[state.allocations]);
  const visible=useMemo(()=>state.budgets.filter(row=>{
    return (year==="all"||String(row.fmg_year)===year) &&
           (filter==="all"||String(row.fmg_status)===filter) &&
           (!search.trim()||String(row.fmg_name||"").toLowerCase().includes(search.trim().toLowerCase()));
  }),[state.budgets,year,filter,search]);
  const years=Array.from(new Set(state.budgets.map(row=>String(row.fmg_year)))).sort().reverse();
  const submitted=state.budgets.filter(row=>row.fmg_status===797280401).length;
  function refresh():void{(window as any)[cache]=null;(window as any)[cache+"_inflight"]=null;setReload(v=>v+1)}
  return <main className={classes.root} aria-label="Gestión de presupuestos">
    <header className={classes.top}><div><Text as="h1" className={classes.heading}>Presupuestos</Text><p className={classes.description}>Planificación anual y asignaciones por categoría y mes · PEN</p></div>
      <Button icon={<ArrowClockwiseRegular/>} onClick={refresh} disabled={state.loading}>Actualizar</Button></header>
    <section className={classes.cards} aria-label="Resumen de presupuestos">
      <Card><CardHeader image={<CalendarRegular/>} header={<Text weight="semibold">Versiones visibles</Text>}/><Text className={classes.big}>{state.budgets.length}</Text></Card>
      <Card><CardHeader image={<MoneyRegular/>} header={<Text weight="semibold">Asignaciones consultadas</Text>}/><Text className={classes.big}>{state.allocations.length}</Text></Card>
      <Card><CardHeader image={<ShieldLockRegular/>} header={<Text weight="semibold">Presentados</Text>}/><Text className={classes.big}>{submitted}</Text></Card>
    </section>
    <Card className={classes.info}><Text weight="semibold">Aprobación temporalmente deshabilitada</Text><Text size={200}>Los presupuestos se pueden preparar y presentar según permisos. La transición a Aprobado está bloqueada por Dataverse hasta completar la separación de funciones y el control de versiones concurrentes. No hay todavía presupuesto aprobado válido para calcular diferencias frente a ejecución.</Text></Card>
    <section className={classes.panel}><div className={classes.toolbar}>
      <Input aria-label="Buscar presupuesto por nombre" placeholder="Buscar presupuesto" value={search} onChange={(_,data)=>setSearch(data.value)}/>
      <label htmlFor="fmBudgetYear">Año </label><select id="fmBudgetYear" value={year} onChange={e=>setYear(e.target.value)}><option value="all">Todos</option>{years.map(y=><option key={y} value={y}>{y}</option>)}</select>
      <label htmlFor="fmBudgetStatus">Estado </label><select id="fmBudgetStatus" value={filter} onChange={e=>setFilter(e.target.value)}>
        <option value="all">Todos</option><option value="797280400">Borrador</option><option value="797280401">Presentado</option><option value="797280402">Aprobado</option><option value="797280403">Rechazado</option><option value="797280404">Sustituido</option>
      </select></div></section>
    {state.loading&&<Spinner label="Cargando presupuesto y asignaciones..."/>}
    {state.error&&<Text role="alert" className={classes.error}>{state.error}</Text>}
    {!state.loading&&!state.error&&visible.length===0&&<div className={classes.info}><Text>No hay presupuestos para los filtros seleccionados.</Text></div>}
    {!state.loading&&!state.error&&visible.length>0&&<div className={classes.tableContainer}><table className={classes.table}>
      <thead><tr>{["Presupuesto","Año","Ámbito","Versión","Desde mes","Estado","Asignado (consultado)","Detalle"].map(t=><th key={t} className={classes.cell+" "+classes.th}>{t}</th>)}</tr></thead>
      <tbody>{visible.map(row=><tr key={row.fmg_budgetid}>
        <td className={classes.cell}>{row.fmg_name}</td><td className={classes.cell}>{row.fmg_year}</td>
        <td className={classes.cell}>{row.fmg_scope===797280301?"Compartido":"Por sede"}</td>
        <td className={classes.cell}>{row.fmg_version}</td><td className={classes.cell}>{row.fmg_effectivefrommonth}</td>
        <td className={classes.cell}><Badge appearance="outline">{displayStatus(row.fmg_status)}</Badge></td>
        <td className={classes.cell}>{displayAmount(counts.get(String(row.fmg_budgetid).toLowerCase())||0)}</td>
        <td className={classes.cell}><Button appearance="subtle" icon={<OpenRegular/>} onClick={()=>navigateBudget(row.fmg_budgetid)}>Abrir</Button></td>
      </tr>)}</tbody></table></div>}
    {state.limited&&<Text size={200}>Listado parcial por paginación. Use la vista nativa para consultar el conjunto completo de registros.</Text>}
  </main>;
};
export default BudgetPage;
