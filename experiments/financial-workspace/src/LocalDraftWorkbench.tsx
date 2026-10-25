import { useState } from "react";
import { ArrowLeft, Check, Clock3, FilePenLine, PencilLine, Plus, ShieldAlert, Trash2, X } from "lucide-react";
import "./local-draft-workbench.css";

type LocalDraft = {
  id: string;
  amount: string;
  date: string;
  category: string;
  scope: "Por sede" | "Compartido";
  branch: string;
  account: string;
  flow: string;
  description: string;
  modifiedAt: string;
};
const blank = ():Omit<LocalDraft,"id"|"modifiedAt"> => ({
  amount:"",date:"",category:"",scope:"Por sede",branch:"",
  account:"",flow:"Operativo",description:"",
});
type Fields = ReturnType<typeof blank>;
type FormError = Partial<Record<keyof Fields,string>>;
export default function LocalDraftWorkbench({categories,branches,accounts}:{
  categories:string[];branches:string[];accounts:string[];
}) {
  const [drafts,setDrafts]=useState<LocalDraft[]>([]);
  const [editing,setEditing]=useState<string|null>(null);
  const [fields,setFields]=useState<Fields>(blank());
  const [errors,setErrors]=useState<FormError>({});
  const [notice,setNotice]=useState("");
  const [nextId,setNextId]=useState(1);
  const update = <K extends keyof Fields>(field:K,value:Fields[K]) =>{
    setFields(old=>({...old,[field]:value,...(field==="scope"?{branch:""}:{})}));
    setErrors(old=>({...old,[field]:undefined}));
    setNotice("");
  };
  const open=(id?:string)=>{
    const source=drafts.find(d=>d.id===id);
    setFields(source?{
      amount:source.amount,date:source.date,category:source.category,
      scope:source.scope,branch:source.branch,account:source.account,
      flow:source.flow,description:source.description,
    }:blank());
    setEditing(id ?? "new");
    setErrors({});
    setNotice("");
  };
  const save=()=>{
    const invalid:FormError={};
    const raw=fields.amount.trim().replace(",",".");
    if(fields.amount && (!/^[0-9]+(?:[.,][0-9]{1,2})?$/.test(fields.amount.trim()) || Number(raw)<=0)) invalid.amount="Introduce un importe positivo en PEN, con hasta dos decimales.";
    if(fields.description.length>500)invalid.description="Máximo 500 caracteres.";
    if(fields.date && !/^\d{4}-\d{2}-\d{2}$/.test(fields.date))invalid.date="La fecha no es válida.";
    setErrors(invalid);
    if(Object.keys(invalid).length)return;
    const now=new Date().toISOString();
    const id=editing==="new"?`LOCAL-${String(nextId).padStart(4,"0")}`:editing;
    if(!id)return;
    const newDraft:LocalDraft={...fields,amount:fields.amount?Number(raw).toFixed(2):"",id,modifiedAt:now};
    setDrafts(old=>[newDraft,...old.filter(d=>d.id!==id)]);
    if(editing==="new")setNextId(n=>n+1);
    setNotice(`${id} guardado solo en memoria de esta página. No se creó ningún registro en Dataverse.`);
    setEditing(null);
  };
  const discard=(id:string)=>{
    setDrafts(old=>old.filter(d=>d.id!==id));
    setNotice(`Borrador local ${id} descartado. No afecta a registros financieros.`);
  };
  return <section className="draft-workbench" aria-label="Borradores de prueba sin persistencia">
    <div className="draft-workbench-header">
      <div><div className="eyebrow">FLUJO OPERATIVO · VISTA EXPERIMENTAL</div>
        <h3><FilePenLine size={21}/> Bandeja de borradores</h3>
        <p>Ensaya la captura y edición. Los borradores existen solo en memoria mientras esta vista está abierta; no son pagos contabilizados.</p>
      </div>
      {!editing && <button className="draft-create" onClick={()=>open()}><Plus size={16}/> Nuevo borrador <span>demo</span></button>}
    </div>
    <div className="draft-safety"><ShieldAlert size={17}/><span><strong>Simulación local:</strong> crear, editar o descartar aquí no guarda datos en Dataverse ni concede permisos. No se admite contabilizar o aprobar.</span></div>
    {notice && <div className="draft-notice" role="status"><Check size={17}/>{notice}</div>}
    {editing ? <div className="draft-editor">
      <div className="draft-editor-top"><div><span className="eyebrow">CAPTURA LOCAL</span><h4>{editing==="new"?"Nuevo borrador de prueba":`Editar ${editing}`}</h4></div>
        <button className="draft-back" onClick={()=>{setEditing(null);setNotice("Cambios no guardados descartados.");}}><X size={16}/> Cancelar</button></div>
      <div className="draft-form-grid">
        <label>Importe (PEN)<input inputMode="decimal" value={fields.amount} placeholder="0,00" onChange={e=>update("amount",e.target.value)} aria-invalid={Boolean(errors.amount)}/>{errors.amount&&<small role="alert">{errors.amount}</small>}</label>
        <label>Fecha estimada<input type="date" value={fields.date} onChange={e=>update("date",e.target.value)} aria-invalid={Boolean(errors.date)}/>{errors.date&&<small role="alert">{errors.date}</small>}</label>
        <label>Categoría<select value={fields.category} onChange={e=>update("category",e.target.value)}><option value="">Seleccionar (opcional para Draft)</option>{categories.map(c=><option key={c}>{c}</option>)}</select></label>
        <label>Clasificación<select value={fields.flow} onChange={e=>update("flow",e.target.value)}>{["Operativo","Capital","Impuestos"].map(s=><option key={s}>{s}</option>)}</select></label>
        <label>Ámbito<select value={fields.scope} onChange={e=>update("scope",e.target.value as Fields["scope"])}><option>Por sede</option><option>Compartido</option></select></label>
        {fields.scope==="Por sede" && <label>Sede<select value={fields.branch} onChange={e=>update("branch",e.target.value)}><option value="">Seleccionar sede</option>{branches.map(s=><option key={s}>{s}</option>)}</select></label>}
        <label className="draft-account">Cuenta de origen<select value={fields.account} onChange={e=>update("account",e.target.value)}><option value="">Seleccionar cuenta</option>{accounts.map(s=><option key={s}>{s}</option>)}</select></label>
        <label className="draft-description">Descripción<textarea rows={3} maxLength={520} placeholder="Explica el movimiento (opcional en borrador)" value={fields.description} onChange={e=>update("description",e.target.value)} aria-invalid={Boolean(errors.description)}/>{errors.description&&<small role="alert">{errors.description}</small>}</label>
      </div>
      <div className="draft-form-actions"><span><Clock3 size={15}/> Los campos incompletos pueden conservarse como Draft; contabilizar exige validación de servidor.</span><button onClick={save} className="draft-create"><Check size={17}/> Guardar borrador local</button></div>
    </div> : drafts.length ? <div className="draft-items" role="list">{drafts.map(d=><article key={d.id} role="listitem" className="draft-item"><div className="draft-item-summary"><strong>{d.id}</strong><span>{d.category||"Categoría pendiente"} · {d.scope}</span><small><Clock3 size={13}/> Editado localmente · {new Date(d.modifiedAt).toLocaleTimeString("es-PE",{hour:"2-digit",minute:"2-digit"})}</small></div><div className="draft-item-right"><b>{d.amount?`S/ ${Number(d.amount).toLocaleString("es-PE",{minimumFractionDigits:2})}`:"Sin importe"}</b><span>Draft · solo demo</span><div><button onClick={()=>open(d.id)}><PencilLine size={14}/> Editar</button><button onClick={()=>discard(d.id)}><Trash2 size={14}/> Descartar</button></div></div></article>)}</div> : <div className="draft-empty"><FilePenLine size={24}/><div><strong>No hay borradores locales.</strong><span>Prueba el flujo «Nuevo borrador» sin modificar la contabilidad real.</span></div><button onClick={()=>open()}><ArrowLeft size={15}/> Empezar</button></div>}
  </section>;
}
