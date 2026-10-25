import { useMemo, useState } from "react";
import {
  Activity, ArrowDownLeft, ArrowRight, ArrowUpRight, BarChart3, Building2,
  CalendarDays, Check, CheckCheck, ChevronDown, ChevronLeft, ChevronRight, CircleHelp,
  Copy, Download, ExternalLink, FileText, LayoutDashboard, Layers3,
  LockKeyhole, Menu, Moon, ReceiptText, Search, ShieldCheck, SlidersHorizontal,
  Sun, Wallet, X,
} from "lucide-react";
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, ResponsiveContainer,
  Tooltip, XAxis, YAxis,
} from "recharts";
import fixture from "./data/demo.json";
import CategoryExplorer from "./CategoryExplorer";
import LocalDraftWorkbench from "./LocalDraftWorkbench";

type Payment = {
  id: string;
  date: string;
  amount: number;
  category: string;
  categoryCode: string;
  branch: string;
  scope: string;
  account: string;
  flow: string;
  status: string;
};
type Page = "inicio" | "movimientos" | "categorias" | "presupuestos" | "analisis";
const transactions: Payment[] = fixture.records as Payment[];
const NAV = [
  { id: "inicio", label: "Inicio", icon: LayoutDashboard },
  { id: "movimientos", label: "Movimientos", icon: ReceiptText },
  { id: "categorias", label: "Categorías", icon: Layers3 },
  { id: "presupuestos", label: "Presupuestos", icon: Wallet },
  { id: "analisis", label: "Análisis", icon: BarChart3 },
] as const;
const MONTHS = ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];
const MONTH_LONG = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];
const number = new Intl.NumberFormat("es-PE", { maximumFractionDigits: 0 });
const pen = new Intl.NumberFormat("es-PE", { style: "currency", currency: "PEN", minimumFractionDigits: 2 });
const compact = (v: number) => v >= 1000 ? `S/ ${(v / 1000).toLocaleString("es-PE", { maximumFractionDigits: 1 })} mil` : pen.format(v);
const palette = ["#416c77", "#7ca399", "#abc2bb", "#d4c3a5", "#889fa7", "#a7bcae", "#c7b6a5", "#7b9c96", "#cad8d5"];
function dateLabel(input: string) {
  const [y, m, day] = input.split("-").map(Number);
  return new Date(y, m - 1, day).toLocaleDateString("es-PE", { day: "2-digit", month: "short", year: "numeric" });
}
function IconBubble({ icon: Icon, tone = "blue" }: { icon: typeof Activity; tone?: string }) {
  return <span className={`icon-bubble ${tone}`}><Icon aria-hidden="true" size={17} strokeWidth={1.8} /></span>;
}
function ChartHint({ children }: { children: React.ReactNode }) {
  return <span className="chart-hint">{children}</span>;
}
function SectionHead({ eyebrow, title, more }: { eyebrow: string; title: string; more?: React.ReactNode }) {
  return <div className="section-head"><div><span className="eyebrow">{eyebrow}</span><h2>{title}</h2></div>{more}</div>;
}
function TooltipMoney({ active, payload, label }: { active?: boolean; payload?: Array<{ name?: string; value?: number }>; label?: string | number }) {
  if (!active || !payload?.length) return null;
  return <div className="chart-tooltip"><strong>{label}</strong>{payload.map((p, index) => <div key={index}>{p.name}: {compact(Number(p.value ?? 0))}</div>)}</div>;
}
function App() {
  const [page, setPage] = useState<Page>("inicio");
  const [year, setYear] = useState(2026);
  const [month, setMonth] = useState(0);
  const [scope, setScope] = useState("Todos");
  const [category, setCategory] = useState("Todas");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<Payment | null>(null);
  const [dark, setDark] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);
  const [copyMessage, setCopyMessage] = useState("");
  const scopes = ["Todos", ...Array.from(new Set(transactions.map(t => t.scope)))];
  const categories = ["Todas", ...Array.from(new Set(transactions.map(t => t.category))).sort((a, b) => a.localeCompare(b, "es"))];
  const filtered = useMemo(() => transactions.filter(t =>
    Number(t.date.slice(0, 4)) === year &&
    (!month || Number(t.date.slice(5, 7)) === month) &&
    (scope === "Todos" || t.scope === scope) &&
    (category === "Todas" || t.category === category) &&
    (!search || [t.id, t.category, t.account, t.branch].join(" ").toLowerCase().includes(search.toLowerCase()))
  ), [year, month, scope, category, search]);
  const summary = useMemo(() => {
    const total = filtered.reduce((sum, t) => sum + t.amount, 0);
    const shared = filtered.filter(t => t.scope === "Compartido").reduce((sum, t) => sum + t.amount, 0);
    const monthly = Array.from({ length: year === 2026 ? 10 : 12 }, (_, i) => {
      const current = filtered.filter(t => Number(t.date.slice(5, 7)) === i + 1);
      return { name: MONTHS[i], amount: Number(current.reduce((s, t) => s + t.amount, 0).toFixed(2)), count: current.length };
    });
    const byCategory = Array.from(new Set(filtered.map(t => t.category))).map(name => ({
      name, amount: filtered.filter(t => t.category === name).reduce((s, t) => s + t.amount, 0),
      count: filtered.filter(t => t.category === name).length
    })).sort((a, b) => b.amount - a.amount);
    const byFlow = ["Operativo", "Capital", "Impuestos"].map(name => ({
      name, amount: filtered.filter(t => t.flow === name).reduce((s, t) => s + t.amount, 0),
    }));
    return { total, shared, count: filtered.length, monthly, byCategory, byFlow };
  }, [filtered]);
  const latest = useMemo(() => [...filtered].sort((a, b) => b.date.localeCompare(a.date) || b.id.localeCompare(a.id)), [filtered]);
  const visible = latest.slice(0, page === "movimientos" ? 80 : 6);
  const currentPeriod = month ? `${MONTH_LONG[month - 1]} de ${year}` : `el año ${year}`;
  const clear = () => { setMonth(0); setScope("Todos"); setCategory("Todas"); setSearch(""); };
  const navigate = (value: Page) => { setPage(value); setMenuOpen(false); setSelected(null); };
  const copyReference = async (id: string) => {
    try { await navigator.clipboard.writeText(id); setCopyMessage("Referencia copiada"); }
    catch { setCopyMessage("No se pudo copiar; usa el identificador visible."); }
  };
  const exportCSV = () => {
    const columns = ["Referencia", "Fecha", "MontoPEN", "Categoría", "Sede", "Ámbito", "Tipo", "Cuenta"];
    const escape = (v: string | number) => `"${String(v).replaceAll('"', '""')}"`;
    const rows = filtered.map(t => [t.id, t.date, t.amount.toFixed(2), t.category, t.branch, t.scope, t.flow, t.account].map(escape).join(","));
    const blob = new Blob(["\uFEFF" + [columns.join(","), ...rows].join("\r\n")], { type: "text/csv;charset=utf-8" });
    const href = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = href; a.download = `financial-manager-DEMO-${year}${month ? "-" + month : ""}.csv`; a.click();
    URL.revokeObjectURL(href);
  };
  const navContent = <><div className="brand">
    <div className="brand-mark"><Layers3 size={21} strokeWidth={2.1}/></div>
    <div><strong>Financial Manager</strong><span>Workspace <small>• Demo</small></span></div>
  </div><div className="workspace-switch"><Building2 size={18}/><span><b>Espacio financiero</b><small>Entorno de exploración</small></span><ChevronDown size={15}/></div>
    <div className="nav-heading">TU ESPACIO</div>
    <nav aria-label="Navegación principal" className="nav-items">
      {NAV.map(({ id, label, icon: Icon }) => <button key={id} type="button" onClick={() => navigate(id)} aria-current={page === id ? "page" : undefined} className={`nav-item ${page === id ? "active" : ""}`}><Icon size={19} strokeWidth={1.9}/><span>{label}</span>{page === id && <span className="nav-dot" />}</button>)}
    </nav>
    <div className="nav-heading secondary">AYUDA Y SISTEMA</div>
    <button className="nav-item" onClick={() => setHelpOpen(true)}><CircleHelp size={19}/><span>Acerca de esta demo</span></button>
    <div className="sidebar-bottom"><div className="side-tip"><div className="tip-icon"><ShieldCheck size={18}/></div><strong>Una sola fuente de verdad.</strong><p>Los registros reales permanecen en Dataverse. Este espacio no modifica información financiera.</p></div>
      <div className="user-profile"><span className="avatar">FM</span><span><b>Vista de demostración</b><small>Sin sesión empresarial</small></span><span className="profile-circle"/></div>
    </div></>;
  return <div className={`application ${dark ? "dark" : ""}`}>
    {menuOpen && <button className="mobile-overlay" aria-label="Cerrar navegación" onClick={() => setMenuOpen(false)} />}
    <aside className={`sidebar ${menuOpen ? "mobile-open" : ""}`}>{navContent}</aside>
    <div className="app-main">
      <header className="topbar"><div className="breadcrumbs"><button className="hamburger icon-button" onClick={() => setMenuOpen(true)} aria-label="Abrir navegación"><Menu size={20}/></button><span>Workspace</span><span className="crumb-slash">/</span><b>{NAV.find(n => n.id === page)?.label}</b></div>
        <div className="top-actions"><span className="status-dot"/> <span className="system-status">Vista local sin conexión</span><button className="icon-button" title={dark ? "Tema claro" : "Tema oscuro"} aria-label={dark ? "Cambiar a tema claro" : "Cambiar a tema oscuro"} onClick={() => setDark(!dark)}>{dark ? <Sun size={19}/> : <Moon size={19}/>}</button><button className="top-avatar" onClick={() => setHelpOpen(true)} aria-label="Información de la sesión">FM</button></div>
      </header>
      <main className="content">
        <div className="hero"><div className="hero-copy"><span className="hero-overline"><span/> FINANZAS MÁS CLARAS, CADA DÍA</span>
          <h1>{page === "inicio" ? <>Todo en orden,<br/><em>a tu manera.</em></> : page === "movimientos" ? <>Cada movimiento,<br/><em>en contexto.</em></> : page === "presupuestos" ? <>Planifica con<br/><em>responsabilidad.</em></> : page === "categorias" ? <>Cada categoría,<br/><em>en perspectiva.</em></> : <>Los números cuentan<br/><em>una historia.</em></>}</h1>
          <p>{page === "inicio" ? "Una vista tranquila de lo importante: gastos, movimientos y decisiones." : page === "movimientos" ? "Busca, filtra e inspecciona los pagos confirmados sin perderte entre tablas." : page === "presupuestos" ? "Los controles y aprobaciones siguen protegidos en Dataverse." : page === "categorias" ? "Profundiza por categoría y cuenta: importes, frecuencia y movimientos en su contexto." : "Explora patrones y categorías, y continúa el análisis profundo en Power BI."}</p>
        </div><div className="hero-aside"><span className="hero-decoration" aria-hidden="true">✳</span><span className="hero-year">{year}</span><span className="hero-period">GESTIÓN FINANCIERA</span></div></div>

        <div className="period-container"><div className="period-caption"><CalendarDays size={18}/><div><span>PERÍODO DE TRABAJO</span><strong>{year}</strong></div><div className="year-stepper"><button aria-label="Año anterior" onClick={() => { setYear(y => Math.max(2024, y - 1)); setMonth(0); }} disabled={year === 2024}><ChevronLeft size={16}/></button><button aria-label="Año siguiente" onClick={() => { setYear(y => Math.min(2026, y + 1)); setMonth(0); }} disabled={year === 2026}><ChevronRight size={16}/></button></div></div>
          <div className="month-pills" aria-label="Seleccionar mes"><button className={month === 0 ? "selected" : ""} onClick={() => setMonth(0)} aria-pressed={month === 0}>Año</button>{MONTHS.map((name, i) => <button className={month === i + 1 ? "selected" : ""} key={name} onClick={() => setMonth(i + 1)} aria-pressed={month === i + 1}>{name}<small>{i + 1}</small></button>)}</div>
        </div>
        <div className="filterline"><div className="filterline-left"><SlidersHorizontal size={16}/><span>Vista de <b>{currentPeriod}</b></span>
          <label>Ámbito <select aria-label="Filtrar por ámbito" value={scope} onChange={e => setScope(e.target.value)}>{scopes.map(s => <option key={s}>{s}</option>)}</select></label>
          <label>Categoría <select aria-label="Filtrar por categoría" value={category} onChange={e => setCategory(e.target.value)}>{categories.map(c => <option key={c}>{c}</option>)}</select></label>
          {(scope !== "Todos" || category !== "Todas" || month !== 0 || search) && <button className="text-action" onClick={clear}>Restablecer</button>}
          </div><span className="demo-tag"><span/> DATOS SINTÉTICOS</span>
        </div>

        {page === "inicio" && <><section className="overview-metrics" aria-label="Resumen del período">
          <div className="lead-metric"><span className="eyebrow">TOTAL CONFIRMADO · PEN</span><div className="lead-number">{compact(summary.total)}</div><div className="lead-caption"><IconBubble icon={CheckCheck} tone="mint"/><span>{number.format(summary.count)} pagos contabilizados en la selección</span></div></div>
          <div className="support-metrics"><div><IconBubble icon={ReceiptText} tone="blue"/><span>Transacciones</span><b>{number.format(summary.count)}</b><small>Confirmadas</small></div><div><IconBubble icon={Activity} tone="rose"/><span>Ticket promedio</span><b>{compact(summary.count ? summary.total / summary.count : 0)}</b><small>Por movimiento</small></div><div><IconBubble icon={Layers3} tone="lavender"/><span>Gasto compartido</span><b>{compact(summary.shared)}</b><small>{summary.total ? ((summary.shared/summary.total)*100).toFixed(1) : "0"}% del gasto</small></div></div>
        </section>
        <SectionHead eyebrow="TU PULSO FINANCIERO" title="Lo que pasó en este período" more={<button className="inline-link" onClick={() => navigate("analisis")}>Profundizar <ArrowUpRight size={16}/></button>}/>
        <div className="analysis-grid"><article className="surface chart-surface"><div className="surface-heading"><div><h3>Movimiento del gasto</h3><ChartHint>Importes mensuales expresados en PEN</ChartHint></div><span className="mini-label">Evolución {year}</span></div>
          <div className="large-chart" role="img" aria-label="Gráfico del gasto total por mes"><ResponsiveContainer width="100%" height="100%"><AreaChart data={summary.monthly} margin={{ top: 15, right: 6, left: -13, bottom: 2 }}><defs><linearGradient id="spendingArea" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="#91b0b0" stopOpacity=".35"/><stop offset="95%" stopColor="#91b0b0" stopOpacity=".02"/></linearGradient></defs><CartesianGrid vertical={false} stroke="var(--grid)" strokeDasharray="2 5"/><XAxis dataKey="name" axisLine={false} tickLine={false} tick={{ fill: "var(--muted)", fontSize: 11 }} interval={0}/><YAxis axisLine={false} tickLine={false} tick={{ fill: "var(--muted)", fontSize: 11 }} tickFormatter={v => `${Math.round(v/1000)}k`}/><Tooltip content={<TooltipMoney/>}/><Area name="Gasto" type="monotone" dataKey="amount" stroke="#587e83" strokeWidth={2.7} fill="url(#spendingArea)" dot={false} activeDot={{r:5}} isAnimationActive={false}/></AreaChart></ResponsiveContainer></div>
          <div className="chart-bottom"><span><span className="legend-dot teal"/>Gasto mensual</span><span>Fuente: demostración determinística</span></div>
        </article>
        <article className="surface category-surface"><div className="surface-heading"><div><h3>¿Dónde se concentra?</h3><ChartHint>Categorías con mayor gasto</ChartHint></div><IconBubble icon={Layers3} tone="lavender"/></div><div className="category-list">{summary.byCategory.slice(0,5).map((cat,i) => <button className="category-row" key={cat.name} onClick={() => { setCategory(cat.name); navigate("movimientos"); }}><span className="category-square" style={{background:palette[i%palette.length]}}/><span className="category-name">{cat.name}</span><span className="category-money">{compact(cat.amount)}</span><span className="category-progress"><span style={{width:`${summary.total ? 100*cat.amount/summary.total : 0}%`,background:palette[i%palette.length]}}/></span></button>)}</div><button className="subtle-link" onClick={() => navigate("categorias")}>Ver todas las categorías <ArrowRight size={16}/></button></article></div>
        <SectionHead eyebrow="ACTIVIDAD DEL EQUIPO" title="Movimientos a la vista" more={<button className="inline-link" onClick={() => navigate("movimientos")}>Ver historial <ArrowRight size={16}/></button>}/>
        <div className="operational-grid"><TransactionList payments={visible} onSelect={setSelected} selected={selected?.id} compactList/><aside className="insight-panel"><div className="insight-overline">LECTURA RÁPIDA</div><div className="insight-graphic"><span className="insight-ring" style={{background:`conic-gradient(#698c8b ${summary.total ? (summary.shared/summary.total)*100 : 0}%, #e4e9e6 0)`}}><span>{summary.total ? ((summary.shared/summary.total)*100).toFixed(1) : "0"}<small>%</small></span></span><span><strong>Compartido</strong><small>Del importe confirmado</small></span></div><div className="insight-rule"/>{summary.byFlow.map((flow, i) => <div className="flow-row" key={flow.name}><span className="flow-dot" style={{background:palette[i]}}/>{flow.name}<strong>{compact(flow.amount)}</strong></div>)}<p>Los importes reflejan solo pagos ficticios contabilizados; no equivalen a saldo disponible.</p></aside></div>
        </>}
        {page === "movimientos" && <><SectionHead eyebrow="GESTIÓN DIARIA" title="Registro de pagos" more={<span className="demo-tag"><span/> EDICIÓN LOCAL · SOLO PRUEBA</span>}/>
          <LocalDraftWorkbench categories={categories.slice(1)} branches={[...new Set(transactions.map(t=>t.branch).filter(name=>name && name!=="Compartido"))].sort()} accounts={[...new Set(transactions.map(t=>t.account))].sort()}/>
          <div className="toolstrip"><label className="search-field"><Search size={18}/><input aria-label="Buscar movimiento" value={search} onChange={e => setSearch(e.target.value)} placeholder="Buscar referencia, cuenta o categoría..."/></label><button className="outline-button" onClick={exportCSV} disabled={!filtered.length}><Download size={16}/> Exportar demo</button></div>
          <div className="page-summary"><div><span>REGISTROS EN LA SELECCIÓN</span><strong>{number.format(summary.count)}</strong></div><div><span>IMPORTE TOTAL</span><strong>{compact(summary.total)}</strong></div><div><span>ÚLTIMO MOVIMIENTO</span><strong>{latest.length ? dateLabel(latest[0].date) : "Sin datos"}</strong></div></div>
          <TransactionList payments={visible} onSelect={setSelected} selected={selected?.id}/>{visible.length < filtered.length && <p className="table-footnote">Mostrando los 80 movimientos más recientes del filtro. La exportación incluye todos los registros filtrados.</p>}
        </>}
        {page === "categorias" && <>
          <SectionHead eyebrow="EXPLORACIÓN FINANCIERA" title="Comprende el origen del gasto" more={<button className="inline-link" onClick={() => navigate("movimientos")}>Ver registro completo <ArrowRight size={16}/></button>}/>
          <CategoryExplorer records={filtered} onOpenTransactions={name=>{setCategory(name);setSearch("");navigate("movimientos");}}/>
        </>}
        {page === "presupuestos" && <><SectionHead eyebrow="CONTROL PRESUPUESTARIO" title="Planificación y decisiones"/>
          <div className="budget-layout"><article className="surface budget-main"><span className="budget-icon"><ShieldCheck size={28}/></span><span className="eyebrow">ENTORNO DE DEMOSTRACIÓN · SOLO LECTURA</span><h3>Las decisiones sensibles tienen su propio espacio.</h3><p>Los presupuestos, las revisiones y las aprobaciones están protegidos por el servidor de Dataverse. Esta vista visual es una exploración del futuro espacio operativo y no muestra presupuestos simulados como si estuvieran aprobados.</p><div className="gate-grid"><div><Check size={18}/><strong>Integridad del servidor</strong><small>Reglas y validaciones independientes de la interfaz.</small></div><div><LockKeyhole size={18}/><strong>Separación de funciones</strong><small>Sin botones de aprobación ni acceso directo.</small></div><div><FileText size={18}/><strong>Versiones auditables</strong><small>Los cambios reales se revisan en Power Apps.</small></div></div></article>
          <aside className="surface next-panel"><span className="eyebrow">SIGUIENTE INTEGRACIÓN</span><h3>Un centro para decidir, sin perder el control.</h3><p>Cuando exista la conexión de usuario y se comprueben los privilegios, aquí aparecerán planes, estado de revisión y comparativas. Las acciones seguirán pasando por la API segura.</p><div className="readonly-label"><LockKeyhole size={15}/> Operaciones deshabilitadas en este prototipo</div></aside></div>
        </>}
        {page === "analisis" && <><SectionHead eyebrow="EXPLORACIÓN VISUAL" title="Entender antes de decidir" more={<a className="inline-link" target="_blank" rel="noopener noreferrer" href="https://github.com/David-std/financial-manager/tree/develop/power-bi">Proyecto Power BI <ExternalLink size={16}/></a>}/>
          <div className="analysis-summary"><div><span>PERÍODO</span><strong>{currentPeriod}</strong></div><div><span>GASTO CONFIRMADO</span><strong>{compact(summary.total)}</strong></div><div><span>TRANSACCIONES</span><strong>{summary.count}</strong></div></div>
          <div className="analytics-layout"><article className="surface chart-surface"><div className="surface-heading"><div><h3>Composición por mes</h3><ChartHint>Gasto confirmado · soles peruanos</ChartHint></div></div><div className="large-chart"><ResponsiveContainer width="100%" height="100%"><BarChart data={summary.monthly} margin={{top:10,right:3,left:-15,bottom:0}}><CartesianGrid vertical={false} stroke="var(--grid)"/><XAxis dataKey="name" tick={{fill:"var(--muted)",fontSize:11}} axisLine={false} tickLine={false}/><YAxis tickFormatter={v => `${Math.round(v/1000)}k`} tick={{fill:"var(--muted)",fontSize:11}} axisLine={false} tickLine={false}/><Tooltip content={<TooltipMoney/>}/><Bar name="Importe" dataKey="amount" fill="#7898aa" radius={[5,5,0,0]} maxBarSize={42} isAnimationActive={false}/></BarChart></ResponsiveContainer></div></article>
          <article className="surface category-surface"><div className="surface-heading"><div><h3>Clases de gasto</h3><ChartHint>Operativo · capital · impuestos</ChartHint></div></div><div className="type-stack"><div className="stack-track">{summary.byFlow.map((f,i) => <span key={f.name} style={{width:`${summary.total ? f.amount/summary.total*100 : 0}%`,background:palette[i]}}/>)}</div>{summary.byFlow.map((flow,i)=><div className="stack-item" key={flow.name}><span className="flow-dot" style={{background:palette[i]}}/>{flow.name}<strong>{compact(flow.amount)}</strong></div>)}</div><div className="analysis-disclaimer">La clasificación responde a flujos de salida, no a utilidad ni rentabilidad.</div></article></div>
          <article className="surface full-category"><div className="surface-heading"><div><h3>Detalle por categoría</h3><ChartHint>Gasto confirmado y participación en el período seleccionado</ChartHint></div></div>{summary.byCategory.map((c,i)=><div className="detail-category" key={c.name}><span className="category-index">{String(i+1).padStart(2,"0")}</span><span>{c.name}</span><span>{number.format(c.count)} pagos</span><b>{compact(c.amount)}</b><span className="percent">{summary.total ? (100*c.amount/summary.total).toFixed(1) : "0"}%</span></div>)}</article>
        </>}
        <footer className="main-footer"><span><span className="footer-glyph">✳</span> Financial Manager · Exploración de interfaz</span><span>310 movimientos ficticios · Sin acceso a Dataverse</span></footer>
      </main>
    </div>
    {selected && <><button className="drawer-backdrop" aria-label="Cerrar detalle" onClick={() => {setSelected(null);setCopyMessage("");}}/><aside className="detail-drawer" aria-label="Detalle del movimiento"><div className="drawer-title"><span>DETALLE DEL MOVIMIENTO</span><button className="icon-button" aria-label="Cerrar detalle" onClick={() => {setSelected(null);setCopyMessage("");}}><X size={20}/></button></div><div className="drawer-symbol"><ArrowDownLeft size={26}/></div><h2>{selected.id}</h2><p>Pago confirmado · registro de prueba</p><div className="drawer-amount">{pen.format(selected.amount)}</div><span className="drawer-success"><Check size={14}/> Contabilizado</span><div className="drawer-fields">{([["Fecha de pago",dateLabel(selected.date)],["Categoría",selected.category],["Sede",selected.branch],["Ámbito",selected.scope],["Cuenta de origen",selected.account],["Clasificación",selected.flow]] as const).map(([label,value])=><div key={label}><span>{label}</span><strong>{value}</strong></div>)}</div><button className="outline-button full" onClick={() => void copyReference(selected.id)}><Copy size={16}/> Copiar referencia</button>{copyMessage && <p className="copy-status" role="status">{copyMessage}</p>}<div className="drawer-warning"><ShieldCheck size={17}/> Consulta local: no existen acciones de edición o anulación en esta vista.</div></aside></>}
    {helpOpen && <div className="modal-overlay" role="presentation" onMouseDown={e=> {if(e.currentTarget===e.target)setHelpOpen(false);}}><div role="dialog" aria-modal="true" aria-labelledby="help-heading" className="help-modal"><button className="icon-button modal-close" onClick={()=>setHelpOpen(false)} aria-label="Cerrar"><X size={19}/></button><IconBubble icon={ShieldCheck} tone="mint"/><h2 id="help-heading">Vista de exploración, no sistema financiero operativo.</h2><p>Este prototipo funciona sin autenticación de Microsoft ni conexión a Dataverse. Utiliza exclusivamente los mismos 310 movimientos inventados empleados para probar Power BI.</p><p>El ingreso de gastos, las modificaciones y las aprobaciones permanecen en los flujos protegidos de Power Apps y Dataverse. La integración real requerirá autenticación, verificación de permisos y capacidad Fabric si se usa Fabric Apps.</p><button className="primary-button" onClick={()=>setHelpOpen(false)}>Entendido <ArrowRight size={16}/></button></div></div>}
  </div>;
}
function TransactionList({payments,onSelect,selected,compactList=false}:{payments:Payment[];onSelect:(p:Payment)=>void;selected?:string;compactList?:boolean}){
  return <div className={`surface transaction-list ${compactList ? "compact-list" : ""}`}>
    <div className="surface-heading"><div><h3>{compactList ? "Últimos pagos registrados" : "Movimientos confirmados"}</h3><ChartHint>Selecciona cualquier registro para explorar sus detalles</ChartHint></div><span className="mini-label">{number.format(payments.length)} visibles</span></div>
    <div className="table-scroll" role="region" aria-label="Tabla de movimientos, desplazable horizontalmente" tabIndex={0}><table><thead><tr><th>Movimiento</th><th>Categoría</th><th>Fecha</th><th>Importe</th><th><span className="sr-only">Detalle</span></th></tr></thead><tbody>
      {payments.length ? payments.map(t => <tr key={t.id} className={selected===t.id?"row-active":""} onClick={()=>onSelect(t)}><td><button className="row-link" onClick={()=>onSelect(t)}>{t.id}<small>{t.scope==="Compartido"?"Compartido":t.branch}</small></button></td><td><span className="table-category-dot"/>{t.category}</td><td>{dateLabel(t.date)}</td><td className="numeric">{pen.format(t.amount)}</td><td><ArrowUpRight size={16}/></td></tr>) : <tr><td colSpan={5} className="empty-state">No hay movimientos que coincidan con estos filtros. Cambia el período o restablece la selección.</td></tr>}
    </tbody></table></div>
  </div>;
}
export default App;
