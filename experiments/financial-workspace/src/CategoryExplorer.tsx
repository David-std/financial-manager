import { useMemo, useState } from "react";
import { ArrowLeft, ArrowRight, BarChart3, ChevronRight, List, LayoutGrid } from "lucide-react";
import "./category-explorer.css";

export type ExplorePayment = {
  id: string; category: string; amount: number; date: string;
  account: string; branch: string; scope: string;
};

type Tile = { name: string; amount: number; count: number; x: number; y: number; w: number; h: number; shade: string };
type Group = { name: string; amount: number; count: number };
const shades = ["#c8dfda", "#d4e3e5", "#d6dfec", "#e3dfce", "#d8e8de", "#cbdcd8", "#d8e0e5", "#e6e5d7", "#d0dede", "#e2edea"];
const money = new Intl.NumberFormat("es-PE",{style:"currency",currency:"PEN",minimumFractionDigits:2});
const concise = (amount: number) => amount >= 1000 ? `S/ ${(amount/1000).toLocaleString("es-PE",{maximumFractionDigits:1})} mil` : money.format(amount);

export function categoryGroups(records: ExplorePayment[], groupBy: (r: ExplorePayment) => string): Group[] {
  const groups = new Map<string, { amount: number; count: number }>();
  for (const record of records) {
    const name = groupBy(record) || "Sin clasificar";
    const previous = groups.get(name) ?? {amount:0,count:0};
    groups.set(name,{amount:previous.amount+record.amount,count:previous.count+1});
  }
  return [...groups].map(([name,result])=>({name,...result})).sort((a,b)=>b.amount-a.amount || a.name.localeCompare(b.name,"es"));
}

// Recursive weighted partitions. Rectangular *area* is proportional to amount,
// unlike a uniform grid. Callers must filter out nonpositive amounts.
export function categoryTreemap(groups: Group[]): Tile[] {
  const result:Tile[]=[];
  function carve(items: Group[], x:number, y:number, w:number, h:number, depth:number) {
    if (!items.length) return;
    if (items.length===1) {
      const it=items[0];
      result.push({...it,x,y,w,h,shade:shades[groups.findIndex(g=>g.name===it.name)%shades.length]});
      return;
    }
    const total=items.reduce((s,i)=>s+i.amount,0);
    if(total<=0) return;
    let split=1,sum=items[0].amount;
    while(split<items.length-1 && sum/total<.49) {sum+=items[split].amount;split++;}
    const ratio=sum/total;
    // Alternate split directions, favour the longer axis to avoid skinny
    // slices on narrow phone screens.
    const vertical=(depth%2===0 ? w>=h*.65 : w>h*1.35);
    if(vertical) {
      carve(items.slice(0,split),x,y,w*ratio,h,depth+1);
      carve(items.slice(split),x+w*ratio,y,w*(1-ratio),h,depth+1);
    } else {
      carve(items.slice(0,split),x,y,w,h*ratio,depth+1);
      carve(items.slice(split),x,y+h*ratio,w,h*(1-ratio),depth+1);
    }
  }
  carve(groups.filter(g=>g.amount>0),0,0,100,100,0);
  return result;
}
export default function CategoryExplorer({records,onOpenTransactions,compact=false}:{
  records: ExplorePayment[];
  onOpenTransactions:(category:string)=>void;
  compact?:boolean;
}) {
  const [focus,setFocus]=useState<string|null>(null);
  const [mode,setMode]=useState<"map"|"list">("map");
  const all=useMemo(()=>categoryGroups(records,r=>r.category),[records]);
  const focusRecords=useMemo(()=>focus ? records.filter(r=>r.category===focus):records,[records,focus]);
  const groups=useMemo(()=>categoryGroups(focusRecords,r=>focus ? r.account:r.category),[focusRecords,focus]);
  const tiles=useMemo(()=>categoryTreemap(groups),[groups]);
  const fullTotal=records.reduce((s,r)=>s+r.amount,0);
  const total=groups.reduce((s,r)=>s+r.amount,0);
  const focusedGroup=all.find(g=>g.name===focus);
  return <article className={`surface explorer ${compact?"explorer-compact":""}`} aria-label="Explorador interactivo de gastos por categoría">
    <div className="explorer-header">
      <div>
        <span className="eyebrow">DESGLOSE INTERACTIVO</span>
        <h3>{focus ? "Categoría · "+focus : "Distribución por categorías"}</h3>
        <p>El tamaño de cada rectángulo representa el importe contabilizado. Selecciona uno para explorar.</p>
      </div>
      <div className="explorer-view-switch" role="group" aria-label="Tipo de visualización">
        <button title="Mapa de rectángulos" aria-pressed={mode==="map"} onClick={()=>setMode("map")}><LayoutGrid size={16}/><span>Mapa</span></button>
        <button title="Lista accesible" aria-pressed={mode==="list"} onClick={()=>setMode("list")}><List size={16}/><span>Lista</span></button>
      </div>
    </div>
    {focus && <div className="explorer-breadcrumb"><button onClick={()=>setFocus(null)}><ArrowLeft size={15}/> Todas las categorías</button><ChevronRight size={15}/><strong>{focus}</strong></div>}
    {!groups.length ? <div className="explorer-empty">No hay pagos contabilizados para esta selección. Cambia el período o los filtros.</div> :
      <div className="explorer-layout">
        <div className="explorer-main">
          {mode==="map" && <div className="treemap" role="group" aria-label={focus?"Distribución por cuenta dentro de "+focus:"Distribución del gasto por categoría"}>
            {tiles.map((tile,i)=>{
              const share=total ? 100*tile.amount/total:0;
              const tiny=tile.w<16 || tile.h<18;
              return <button key={tile.name} type="button" className={`treemap-tile ${tiny?"tile-tiny":""}`} style={{
                left:`${tile.x}%`,top:`${tile.y}%`,width:`${tile.w}%`,height:`${tile.h}%`,
                background:tile.shade,
              }} title={`${tile.name}: ${money.format(tile.amount)} (${share.toFixed(1)} %)`}
                aria-label={`${tile.name}: ${money.format(tile.amount)}, ${tile.count} pagos, ${share.toFixed(1)} por ciento`}
                onClick={()=>{if(!focus) setFocus(tile.name);}}
                disabled={Boolean(focus)}
                data-group={tile.name}>
                <span className="treemap-number">{String(i+1).padStart(2,"0")}</span>
                {!tiny && <><strong>{tile.name}</strong><small>{concise(tile.amount)}</small></>}
              </button>;
            })}
          </div>}
          <div className="explorer-legend" role="list" aria-label="Resumen de importes por categoría">
            {groups.map((group,i)=>{
              const share=total?100*group.amount/total:0;
              return <button role="listitem" className="explorer-legend-row" key={group.name}
                onClick={()=>{if(!focus)setFocus(group.name);}}
                disabled={Boolean(focus)}>
                <span className="legend-color" style={{background:shades[i%shades.length]}}/>
                <span className="legend-label">{group.name}</span>
                <span className="legend-count">{group.count} pagos</span>
                <strong>{concise(group.amount)}</strong>
                <span className="legend-share">{share.toFixed(1)} %</span>
                {!focus&&<ChevronRight size={16}/>}
              </button>;
            })}
          </div>
        </div>
        <aside className="explorer-side">
          <span className="eyebrow">{focus?"CATEGORÍA SELECCIONADA":"PERÍODO SELECCIONADO"}</span>
          <strong className="explorer-total">{concise(total)}</strong>
          <span className="explorer-meta">{focusRecords.length} pagos contabilizados</span>
          <div className="explorer-separator"/>
          <div className="explorer-figure"><span>Participación</span><strong>{fullTotal ? ((total/fullTotal)*100).toFixed(1):"0"} %</strong></div>
          <div className="explorer-figure"><span>Ticket medio</span><strong>{concise(focusRecords.length?total/focusRecords.length:0)}</strong></div>
          {focusedGroup && <div className="explorer-figure"><span>En el conjunto</span><strong>{focusedGroup.count} registros</strong></div>}
          {focus ? <>
            <div className="explorer-separator"/>
            <div className="explorer-recent-title">Movimientos de esta categoría</div>
            {[...focusRecords].sort((a,b)=>b.date.localeCompare(a.date)).slice(0,4).map(r=><div className="explorer-record" key={r.id}><span>{r.id}</span><b>{concise(r.amount)}</b></div>)}
            <button className="explorer-action" onClick={()=>onOpenTransactions(focus)}>
              Ver movimientos <ArrowRight size={16}/>
            </button>
          </> : <>
            <div className="explorer-separator"/>
            <p className="explorer-guidance"><BarChart3 size={17}/> Vista financiera de pagos confirmados; no representa compromisos, ingresos ni presupuestos aprobados.</p>

          </>}
        </aside>
      </div>
    }
  </article>;
}
