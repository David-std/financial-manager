import { useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, ArrowRight, BarChart3, ChevronRight, LayoutGrid, List, Table2 } from "lucide-react";
import "./category-explorer.css";

export type ExplorePayment = {
  id: string; category: string; amount: number; date: string;
  account: string; branch: string; scope: string;
};
export type CategoryGroup = {name: string; amount: number; count: number};
export type Tile = CategoryGroup & {x: number; y: number; w: number; h: number; shade: string};
const shades = ["#c8dfda", "#d4e3e5", "#d6dfec", "#e3dfce", "#d8e8de", "#cbdcd8", "#d8e0e5", "#e6e5d7", "#d0dede", "#e2edea"];
const money = new Intl.NumberFormat("es-PE",{style:"currency",currency:"PEN",minimumFractionDigits:2});
const concise = (amount: number) => amount >= 1000 ? `S/ ${(amount/1000).toLocaleString("es-PE",{maximumFractionDigits:1})} mil` : money.format(amount);

export function categoryGroups(records: ExplorePayment[], groupBy: (r: ExplorePayment) => string): CategoryGroup[] {
  const result=new Map<string,{amount:number;count:number}>();
  for(const record of records){
    const name=groupBy(record)||"Sin clasificar";
    const previous=result.get(name)??{amount:0,count:0};
    result.set(name,{amount:previous.amount+record.amount,count:previous.count+1});
  }
  return [...result].map(([name,values])=>({name,...values}))
    .sort((a,b)=>b.amount-a.amount||a.name.localeCompare(b.name,"es"));
}

/** Partition exact areas by amount. The map is overview; list and table are precise alternatives. */
export function categoryTreemap(groups: CategoryGroup[]): Tile[] {
  const result:Tile[]=[];
  function carve(items:CategoryGroup[],x:number,y:number,w:number,h:number,depth:number){
    if(!items.length)return;
    if(items.length===1){
      const item=items[0];
      result.push({...item,x,y,w,h,shade:shades[groups.findIndex(g=>g.name===item.name)%shades.length]});
      return;
    }
    const total=items.reduce((sum,item)=>sum+item.amount,0);
    if(total<=0)return;
    let split=1;
    let value=items[0].amount;
    while(split<items.length-1&&value/total<.49){
      value+=items[split].amount;split++;
    }
    const ratio=value/total;
    const vertical=depth%2===0?w>=h*.65:w>h*1.35;
    if(vertical){
      carve(items.slice(0,split),x,y,w*ratio,h,depth+1);
      carve(items.slice(split),x+w*ratio,y,w*(1-ratio),h,depth+1);
    }else{
      carve(items.slice(0,split),x,y,w,h*ratio,depth+1);
      carve(items.slice(split),x,y+h*ratio,w,h*(1-ratio),depth+1);
    }
  }
  carve(groups.filter(g=>g.amount>0),0,0,100,100,0);
  return result;
}

/** Follows actual tile pixel dimensions, never percentages, to avoid cropped text. */
export function tileContentLevel(width:number,height:number): "full"|"medium"|"code" {
  if(width>=166&&height>=115)return "full";
  if(width>=115&&height>=86)return "medium";
  return "code";
}
export default function CategoryExplorer({records,onOpenTransactions,compact=false}:{
  records:ExplorePayment[];
  onOpenTransactions:(category:string)=>void;
  compact?:boolean;
}){
  const [focus,setFocus]=useState<string|null>(null);
  const [focusAccount,setFocusAccount]=useState<string|null>(null);
  const [mode,setMode]=useState<"map"|"list"|"matrix">("map");
  const mapRef=useRef<HTMLDivElement|null>(null);
  const [dimensions,setDimensions]=useState({width:800,height:350});
  const all=useMemo(()=>categoryGroups(records,r=>r.category),[records]);
  const categoryRecords=useMemo(()=>focus?records.filter(r=>r.category===focus):records,[records,focus]);
  const groups=useMemo(()=>categoryGroups(categoryRecords,r=>focus?r.account:r.category),[categoryRecords,focus]);
  const tiles=useMemo(()=>categoryTreemap(groups),[groups]);
  const visibleRecords=useMemo(()=>focusAccount?categoryRecords.filter(r=>r.account===focusAccount):categoryRecords,[categoryRecords,focusAccount]);
  const fullTotal=records.reduce((s,r)=>s+r.amount,0);
  const total=categoryRecords.reduce((s,r)=>s+r.amount,0);
  const selectedTotal=visibleRecords.reduce((s,r)=>s+r.amount,0);
  const focusLabel=focusAccount??focus;
  useEffect(()=>{
    const el=mapRef.current;
    if(!el||typeof ResizeObserver==="undefined")return;
    const refresh=()=>setDimensions({width:el.clientWidth,height:el.clientHeight});
    refresh();
    const observer=new ResizeObserver(refresh);
    observer.observe(el);
    return ()=>observer.disconnect();
  },[mode,focus]);
  useEffect(()=>{
    if(focus&&!all.some(c=>c.name===focus)){setFocus(null);setFocusAccount(null);}
  },[focus,all]);
  function choose(name:string){
    if(focus)setFocusAccount(name);
    else {setFocus(name);setFocusAccount(null);}
  }
  function reset(){setFocus(null);setFocusAccount(null);}
  return <article className={`surface explorer ${compact?"explorer-compact":""}`} aria-label="Explorador interactivo de gastos por categoría">
    <div className="explorer-header">
      <div><span className="eyebrow">DESGLOSE INTERACTIVO</span>
        <h3>{focus?"Categoría · "+focus:"Distribución por categorías"}</h3>
        <p>La superficie muestra participación en el importe contabilizado. Haz clic para profundizar; usa la tabla para comparar cifras exactas.</p></div>
      <div className="explorer-view-switch" role="group" aria-label="Tipo de visualización">
        <button title="Mapa de rectángulos" aria-pressed={mode==="map"} onClick={()=>setMode("map")}><LayoutGrid size={16}/><span>Mapa</span></button>
        <button title="Lista accesible" aria-pressed={mode==="list"} onClick={()=>setMode("list")}><List size={16}/><span>Lista</span></button>
        <button title="Matriz de categorías" aria-pressed={mode==="matrix"} onClick={()=>setMode("matrix")}><Table2 size={16}/><span>Matriz</span></button>
      </div>
    </div>
    {focus&&<div className="explorer-breadcrumb"><button onClick={reset}><ArrowLeft size={15}/> Categorías</button><ChevronRight size={15}/><strong>{focus}</strong>
      {focusAccount&&<><ChevronRight size={15}/><strong>{focusAccount}</strong></>}</div>}
    {!groups.length?<div className="explorer-empty">No hay pagos contabilizados para esta selección. Cambia el período o los filtros.</div>:
    <div className="explorer-layout">
      <div className="explorer-main">
        {mode==="map"&&<div className="treemap" ref={mapRef} role="group" aria-label={focus?"Distribución por cuenta dentro de "+focus:"Distribución del gasto por categoría"}>
          {tiles.map(tile=>{
            const index=groups.findIndex(g=>g.name===tile.name);
            const share=total?100*tile.amount/total:0;
            const level=tileContentLevel(tile.w*dimensions.width/100,tile.h*dimensions.height/100);
            return <button key={tile.name} type="button"
              className={`treemap-tile treemap-${level} ${focusAccount===tile.name?"treemap-selected":""}`}
              style={{left:`${tile.x}%`,top:`${tile.y}%`,width:`${tile.w}%`,height:`${tile.h}%`,background:tile.shade}}
              title={`${tile.name} · ${money.format(tile.amount)} · ${share.toFixed(1)} % · ${tile.count} pagos`}
              aria-label={`${String(index+1).padStart(2,"0")}: ${tile.name}, ${money.format(tile.amount)}, ${tile.count} pagos, ${share.toFixed(1)} por ciento. Seleccionar`}
              data-group={tile.name} data-level={level} onClick={()=>choose(tile.name)}>
              <span className="treemap-number">{String(index+1).padStart(2,"0")}</span>
              {level!=="code"&&<strong>{tile.name}</strong>}
              {level==="full"&&<small>{concise(tile.amount)} · {share.toFixed(1)} %</small>}
            </button>;
          })}
        </div>}
        {mode==="map"&&<p className="treemap-key">Los números de los rectángulos corresponden al índice de la lista inferior. Los segmentos estrechos muestran solo su código para evitar texto recortado.</p>}
        {mode==="matrix"?<div className="explorer-matrix-wrap"><table className="explorer-matrix">
          <thead><tr><th>Categoría / cuenta</th><th>Pagos</th><th>Importe PEN</th><th>Participación</th><th>Ticket medio PEN</th><th>Detalle</th></tr></thead>
          <tbody>{groups.map(group=><tr key={group.name}><td>{group.name}</td><td>{group.count}</td><td>{money.format(group.amount)}</td><td>{total?(group.amount/total*100).toFixed(1):"0"} %</td><td>{money.format(group.amount/group.count)}</td>
            <td><button onClick={()=>choose(group.name)} aria-label={"Explorar "+group.name}>Explorar <ChevronRight size={14}/></button></td></tr>)}</tbody>
          <tfoot><tr><th>Total</th><th>{categoryRecords.length}</th><th>{money.format(total)}</th><th>100 %</th><th>{money.format(total/categoryRecords.length)}</th><th/></tr></tfoot>
        </table></div>:<div className="explorer-legend" role="list" aria-label="Índice de importes por categoría">
          {groups.map((group,i)=>{const share=total?100*group.amount/total:0;
            return <button role="listitem" className="explorer-legend-row" key={group.name}
              onClick={()=>choose(group.name)} aria-pressed={focusAccount===group.name}>
              <span className="legend-code">{String(i+1).padStart(2,"0")}</span>
              <span className="legend-color" style={{background:shades[i%shades.length]}}/>
              <span className="legend-label">{group.name}</span>
              <span className="legend-count">{group.count} pagos</span>
              <strong>{concise(group.amount)}</strong>
              <span className="legend-share">{share.toFixed(1)} %</span><ChevronRight size={16}/>
            </button>;})}
        </div>}
      </div>
      <aside className="explorer-side" aria-live="polite">
        <span className="eyebrow">{focusLabel?"SEGMENTO SELECCIONADO":"PERÍODO SELECCIONADO"}</span>
        {focusLabel&&<strong className="explorer-side-name">{focusLabel}</strong>}
        <strong className="explorer-total">{concise(focusLabel?selectedTotal:total)}</strong>
        <span className="explorer-meta">{visibleRecords.length} pagos contabilizados</span>
        <div className="explorer-separator"/>
        <div className="explorer-figure"><span>Participación del filtro</span><strong>{fullTotal?((selectedTotal/fullTotal)*100).toFixed(1):"0"} %</strong></div>
        <div className="explorer-figure"><span>Ticket medio</span><strong>{concise(visibleRecords.length?selectedTotal/visibleRecords.length:0)}</strong></div>
        {focus&&<><div className="explorer-separator"/><div className="explorer-recent-title">Movimientos de esta selección</div>
          {[...visibleRecords].sort((a,b)=>b.date.localeCompare(a.date)).slice(0,4).map(r=><div className="explorer-record" key={r.id}><span>{r.id}</span><b>{concise(r.amount)}</b></div>)}
          {focusAccount&&<button className="explorer-back-account" onClick={()=>setFocusAccount(null)}>Ver todas las cuentas</button>}
          <button className="explorer-action" onClick={()=>onOpenTransactions(focus)}>Ver movimientos <ArrowRight size={16}/></button>
        </>}
        {!focus&&<><div className="explorer-separator"/><p className="explorer-guidance"><BarChart3 size={17}/> Importes de pagos confirmados. No representan cobros, caja disponible ni presupuestos aprobados.</p></>}
      </aside>
    </div>}
  </article>;
}
