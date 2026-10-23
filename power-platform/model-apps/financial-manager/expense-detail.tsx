import React, {useEffect, useMemo, useState} from "react";
import {Badge, Button, Card, Field, Input, Spinner, Text, Textarea, makeStyles, tokens} from "@fluentui/react-components";
import {ArrowClockwiseRegular, CheckmarkRegular, SaveRegular, ShieldLockRegular} from "@fluentui/react-icons";
import type {GeneratedComponentProps,fmg_financialtransaction,fmg_category,fmg_cashaccount,fmg_branch,transactioncurrency} from "./RuntimeTypes";

// Power Apps supplies pageInput as a runtime global, not as a React component prop.
declare const pageInput: {recordId?: string; entityName?: string} | undefined;

type Payment=fmg_financialtransaction;
type Data={loading:boolean;record:Payment|null;categories:fmg_category[];accounts:fmg_cashaccount[];branches:fmg_branch[];pen:string;error:string};
type Inputs={date:string;amount:string;flow:string;scope:string;group:string;category:string;branch:string;account:string;description:string;reference:string;voidReason:string};
const useStyles=makeStyles({
 root:{display:"flex",flexDirection:"column",gap:tokens.spacingVerticalL,padding:tokens.spacingHorizontalXXL,backgroundColor:tokens.colorNeutralBackground2,minHeight:"100%",boxSizing:"border-box",color:tokens.colorNeutralForeground1},
 header:{display:"flex",flexWrap:"wrap",alignItems:"center",justifyContent:"space-between",gap:tokens.spacingHorizontalM},
 title:{fontSize:tokens.fontSizeHero700,fontWeight:tokens.fontWeightSemibold},
 description:{color:tokens.colorNeutralForeground3,fontSize:tokens.fontSizeBase300},
 form:{display:"grid",gridTemplateColumns:"repeat(auto-fit, minmax(240px, 1fr))",gap:tokens.spacingHorizontalM},
 panel:{padding:tokens.spacingHorizontalL,backgroundColor:tokens.colorNeutralBackground1},
 toolbar:{display:"flex",flexWrap:"wrap",gap:tokens.spacingHorizontalM,alignItems:"center"},
 note:{color:tokens.colorNeutralForeground3,fontSize:tokens.fontSizeBase200},
 error:{color:tokens.colorPaletteRedForeground1},
 select:{width:"100%",padding:"10px",borderRadius:"5px",border:"1px solid #aaa",backgroundColor:"#fff"},
 wide:{gridColumn:"1/-1"}
});
const initial:Inputs={date:"",amount:"",flow:"",scope:"",group:"",category:"",branch:"",account:"",description:"",reference:"",voidReason:""};
function guid(value:unknown):string {
 if(typeof value!=="string")return "";
 const m=value.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i);
 return m?m[0].toLowerCase():"";
}
function active(value:unknown):boolean{return value===1||value===true;}
function dateInput(value:Date|string|undefined):string{
 if(!value)return "";
 const d=new Date(value);
 return Number.isFinite(d.getTime())?d.toISOString().slice(0,10):"";
}
function buildInputs(row:Payment,categories:fmg_category[]):Inputs{
 const category=guid(row._fmg_category_value);
 const cat=categories.find(c=>c.fmg_categoryid.toLowerCase()===category);
 return {
  date:dateInput(row.fmg_transactiondate),
  amount:row.fmg_amount==null?"":String(row.fmg_amount),
  flow:row.fmg_flowclass==null?"":String(row.fmg_flowclass),
  scope:row.fmg_scope==null?"":String(row.fmg_scope),
  group:guid(cat?._fmg_parentcategory_value),
  category:category,
  branch:guid(row._fmg_branch_value),account:guid(row._fmg_cashaccount_value),
  description:row.fmg_description||"",reference:row.fmg_documentreference||"",voidReason:row.fmg_voidreason||""
 };
}
function statusText(status:number):string{return status===797280001?"Confirmado":status===797280002?"Anulado":"Borrador";}
const ExpenseDetail=(props:GeneratedComponentProps)=>{
 const css=useStyles();
 const {dataApi}=props;
 const pageProps=props as unknown as {pageInput?:{recordId?:string};recordId?:string};
 const recordId=guid(pageProps.pageInput?.recordId || pageProps.recordId || (typeof pageInput!=="undefined" ? pageInput?.recordId : "") || (window as any).pageInput?.recordId);
 const [data,setData]=useState<Data>({loading:!!recordId,record:null,categories:[],accounts:[],branches:[],pen:"",error:""});
 const [inputs,setInputs]=useState<Inputs>(initial);
 const [version,setVersion]=useState(0);
 const [pendingPost,setPendingPost]=useState(false);
 const [pendingVoid,setPendingVoid]=useState(false);
 const [busy,setBusy]=useState(false);
 const [feedback,setFeedback]=useState("");
 const ready=!!dataApi;
 useEffect(()=>{
   if(!recordId||!ready)return;
   let mounted=true;
   setData(d=>({...d,loading:true,error:""}));
   Promise.all([
     dataApi.retrieveRow("fmg_financialtransaction",{id:recordId,select:[
       "fmg_financialtransactionid","fmg_name","fmg_status","fmg_transactiondate","fmg_amount","fmg_flowclass","fmg_scope",
       "_fmg_category_value","_fmg_branch_value","_fmg_cashaccount_value","fmg_description","fmg_documentreference","fmg_voidreason"
     ]}),
     dataApi.queryTable("fmg_category",{select:["fmg_categoryid","fmg_name","fmg_code","fmg_isactive","fmg_isgroup","_fmg_parentcategory_value"],pageSize:100}),
     dataApi.queryTable("fmg_cashaccount",{select:["fmg_cashaccountid","fmg_name","fmg_isactive","_fmg_branch_value"],pageSize:100}),
     dataApi.queryTable("fmg_branch",{select:["fmg_branchid","fmg_name","fmg_isactive"],pageSize:100}),
     dataApi.queryTable("transactioncurrency",{select:["transactioncurrencyid","isocurrencycode"],filter:"isocurrencycode eq 'PEN'",pageSize:5})
   ]).then(([row,cats,accounts,branches,currencies])=>{
     if(!mounted)return;
     const categories=cats.rows;
     const pen=(currencies.rows as transactioncurrency[]).length===1?currencies.rows[0].transactioncurrencyid:"";
     setData({loading:false,record:row,categories,accounts:accounts.rows,branches:branches.rows,pen,error:""});
     setInputs(buildInputs(row,categories));
   }).catch((err:unknown)=>{if(mounted)setData(d=>({...d,loading:false,error:err instanceof Error?err.message:"No se pudo cargar el movimiento."}));});
   return()=>{mounted=false};
 },[recordId,ready,version]);
 const groups=useMemo(()=>data.categories.filter(c=>active(c.fmg_isactive)&&active(c.fmg_isgroup)),[data.categories]);
 const leaves=useMemo(()=>data.categories.filter(c=>active(c.fmg_isactive)&&!active(c.fmg_isgroup)&&c.fmg_code!=="999"&&guid(c._fmg_parentcategory_value)===inputs.group),[data.categories,inputs.group]);
 const accounts=useMemo(()=>data.accounts.filter(c=>{
   if(!active(c.fmg_isactive))return false;
   const assigned=guid(c._fmg_branch_value);
   if(inputs.scope==="797280001")return !assigned;
   if(inputs.scope==="797280000")return !assigned || !inputs.branch || assigned===inputs.branch;
   return true;
 }),[data.accounts,inputs.scope,inputs.branch]);
 function change(field:keyof Inputs,value:string){setInputs(current=>({...current,[field]:value}));setFeedback("");}
 function reload(){setVersion(v=>v+1);setPendingPost(false);setPendingVoid(false);setFeedback("");}
 const draft=data.record?.fmg_status===797280000;
 async function saveDraft():Promise<boolean>{
   if(!recordId||!draft||busy)return false;
   if(inputs.amount && (!Number.isFinite(Number(inputs.amount))||Number(inputs.amount)<=0)) {setFeedback("El importe debe ser positivo.");return false;}
   const payload:Record<string,unknown>={
     fmg_description:inputs.description, fmg_documentreference:inputs.reference
   };
   // Omitted fields retain their previous Dataverse values; cleared draft inputs must be explicit.
   payload.fmg_transactiondate=inputs.date?new Date(inputs.date+"T12:00:00.000Z"):null;
   payload.fmg_amount=inputs.amount?Number(inputs.amount):null;
   payload.fmg_flowclass=inputs.flow?Number(inputs.flow):null;
   payload.fmg_scope=inputs.scope?Number(inputs.scope):null;
   payload["fmg_category@odata.bind"]=inputs.category?"/fmg_category("+inputs.category+")":null;
   payload["fmg_cashaccount@odata.bind"]=inputs.account?"/fmg_cashaccount("+inputs.account+")":null;
   payload["fmg_branch@odata.bind"]=inputs.scope==="797280001"?null:(inputs.branch?"/fmg_branch("+inputs.branch+")":null);
   if(data.pen)payload["transactioncurrencyid@odata.bind"]="/transactioncurrency("+data.pen+")";
   setBusy(true);setFeedback("");
   try{
     await dataApi.updateRow("fmg_financialtransaction",recordId,payload as any);
     setFeedback("Borrador guardado correctamente.");
     return true;
   }catch(err){setFeedback(err instanceof Error?err.message:"No se pudo guardar el borrador.");return false;}
   finally{setBusy(false);}
 }
 async function post():Promise<void>{
   if(!recordId||busy||!draft)return;
   if(!inputs.date||!inputs.amount||!inputs.flow||!inputs.scope||!inputs.category||!inputs.account||
      (inputs.scope==="797280000"&&!inputs.branch)||!data.pen){
      setFeedback("Completa fecha, importe, clasificación, ámbito, categoría, cuenta y sede si corresponde; la moneda PEN debe estar disponible.");return;
   }
   const saved=await saveDraft();
   if(!saved)return;
   setBusy(true);
   try{
     await dataApi.updateRow("fmg_financialtransaction",recordId,{fmg_status:797280001});
     reload();
   }catch(err){setFeedback(err instanceof Error?err.message:"No se pudo confirmar el gasto. Comprueba permisos y datos.");}
   finally{setBusy(false);setPendingPost(false);}
 }
 async function voidPayment():Promise<void>{
   if(!recordId||busy||data.record?.fmg_status!==797280001)return;
   if(!inputs.voidReason.trim()){setFeedback("Es obligatorio indicar el motivo de anulación.");return;}
   setBusy(true);setFeedback("");
   try{
     await dataApi.updateRow("fmg_financialtransaction",recordId,{fmg_status:797280002,fmg_voidreason:inputs.voidReason.trim()});
     reload();
   }catch(err){setFeedback(err instanceof Error?err.message:"No se pudo anular. Comprueba permisos y motivo.");}
   finally{setBusy(false);setPendingVoid(false);}
 }
 if(!recordId)return <main className={css.root}><Text as="h1" className={css.title}>Detalle de gasto</Text><Card>Abre un movimiento desde la lista de Gastos. No se ha recibido un identificador.</Card></main>;
 return <main className={css.root} aria-label="Editar o consultar pago">
   <header className={css.header}><div><Text as="h1" className={css.title}>Detalle de gasto</Text><p className={css.description}>Registro de pagos y remediación de borradores · solo egresos PEN</p></div>
     <Button icon={<ArrowClockwiseRegular/>} onClick={reload} disabled={busy||data.loading}>Actualizar</Button></header>
   {data.loading&&<Spinner label="Cargando movimiento y catálogos..."/>}
   {data.error&&<Text role="alert" className={css.error}>{data.error}</Text>}
   {!data.loading&&!data.error&&data.record&&<>
     <div className={css.header}><Text weight="semibold">{data.record.fmg_name||"Movimiento"}</Text><Badge appearance="outline">{statusText(data.record.fmg_status)}</Badge></div>
     <Card className={css.panel}><div className={css.form}>
       <Field label="Fecha del pago"><Input type="date" disabled={!draft} value={inputs.date} onChange={(_,v)=>change("date",v.value)}/></Field>
       <Field label="Importe (PEN)"><Input type="number" min="0.01" step="0.01" disabled={!draft} value={inputs.amount} onChange={(_,v)=>change("amount",v.value)}/></Field>
       <Field label="Clasificación del pago"><select className={css.select} disabled={!draft} value={inputs.flow} onChange={e=>change("flow",e.target.value)}>
         <option value="">Seleccionar</option><option value="797280000">Pago operativo</option><option value="797280001">Pago de capital</option><option value="797280002">Pago tributario</option><option value="797280003">Otro pago</option>
       </select></Field>
       <Field label="Ámbito"><select className={css.select} disabled={!draft} value={inputs.scope} onChange={e=>{change("scope",e.target.value);change("branch","");change("account","");}}>
         <option value="">Seleccionar</option><option value="797280000">Por sede</option><option value="797280001">Compartido</option>
       </select></Field>
       {inputs.scope==="797280000"&&<Field label="Sede"><select className={css.select} disabled={!draft} value={inputs.branch} onChange={e=>{change("branch",e.target.value);change("account","")}}>
         <option value="">Seleccionar sede</option>{data.branches.filter(b=>active(b.fmg_isactive)).map(b=><option key={b.fmg_branchid} value={b.fmg_branchid}>{b.fmg_name}</option>)}
       </select></Field>}
       <Field label="Grupo de categoría"><select className={css.select} disabled={!draft} value={inputs.group} onChange={e=>{change("group",e.target.value);change("category","")}}>
         <option value="">Seleccionar grupo</option>{groups.map(g=><option key={g.fmg_categoryid} value={g.fmg_categoryid}>{g.fmg_code} · {g.fmg_name}</option>)}
       </select></Field>
       <Field label="Categoría de detalle"><select className={css.select} disabled={!draft} value={inputs.category} onChange={e=>change("category",e.target.value)}>
         <option value="">Seleccionar categoría</option>{leaves.map(c=><option key={c.fmg_categoryid} value={c.fmg_categoryid}>{c.fmg_code} · {c.fmg_name}</option>)}
       </select></Field>
       <Field label="Cuenta de origen"><select className={css.select} disabled={!draft} value={inputs.account} onChange={e=>change("account",e.target.value)}>
         <option value="">Seleccionar cuenta</option>{accounts.map(a=><option key={a.fmg_cashaccountid} value={a.fmg_cashaccountid}>{a.fmg_name}</option>)}
       </select></Field>
       <Field label="Referencia de documento"><Input disabled={!draft} maxLength={100} value={inputs.reference} onChange={(_,v)=>change("reference",v.value)}/></Field>
       <Field label="Descripción" className={css.wide}><Textarea disabled={!draft} value={inputs.description} onChange={(_,v)=>change("description",v.value)} rows={3}/></Field>
     </div></Card>
     <div className={css.toolbar}>
       {draft&&<><Button appearance="secondary" icon={<SaveRegular/>} onClick={()=>{void saveDraft()}} disabled={busy}>Guardar borrador</Button>
         {!pendingPost&&<Button appearance="primary" icon={<CheckmarkRegular/>} onClick={()=>setPendingPost(true)} disabled={busy}>Confirmar pago</Button>}</>}
       {data.record.fmg_status===797280001&&!pendingVoid&&<Button icon={<ShieldLockRegular/>} onClick={()=>setPendingVoid(true)} disabled={busy}>Solicitar anulación</Button>}
     </div>
     {pendingPost&&<Card className={css.panel}><Text weight="semibold">¿Confirmar este pago?</Text>
       <Text size={200}>Al confirmar, los datos financieros quedarán protegidos. Solo usuarios autorizados por Dataverse pueden hacerlo.</Text>
       <div className={css.toolbar}><Button appearance="primary" disabled={busy} onClick={()=>{void post()}}>Sí, confirmar</Button><Button disabled={busy} onClick={()=>setPendingPost(false)}>Cancelar</Button></div></Card>}
     {pendingVoid&&<Card className={css.panel}><Field label="Motivo obligatorio de anulación"><Textarea value={inputs.voidReason} onChange={(_,v)=>change("voidReason",v.value)} rows={3}/></Field>
       <div className={css.toolbar}><Button appearance="primary" disabled={busy||!inputs.voidReason.trim()} onClick={()=>{void voidPayment()}}>Anular movimiento</Button><Button disabled={busy} onClick={()=>setPendingVoid(false)}>Cancelar</Button></div></Card>}
     {feedback&&<Text role="status" className={feedback.toLowerCase().includes("correctamente")?"":css.error}>{feedback}</Text>}
     <Text className={css.note}>La validación final de categoría, cuenta, moneda, ámbito y permisos se ejecuta en el servidor Dataverse. La aplicación nunca omite estas restricciones.</Text>
   </>}
 </main>;
};
export default ExpenseDetail;
