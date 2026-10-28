import { useEffect, useMemo, useState } from "react";
import {
  Activity,
  ArrowDownLeft,
  ArrowRight,
  ArrowUpRight,
  BarChart3,
  Building2,
  CalendarDays,
  Check,
  CheckCheck,
  ChevronLeft,
  ChevronRight,
  CircleHelp,
  Database,
  Copy,
  Download,
  LayoutDashboard,
  Layers3,
  Menu,
  Moon,
  ReceiptText,
  Search,
  ShieldCheck,
  SlidersHorizontal,
  Sun,
  Wallet,
  X,
} from "lucide-react";
import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { demoExpenses } from "./infrastructure/data/financial-repository";
import CategoryExplorer from "./CategoryExplorer";
import AnalyticsWorkbench from "./AnalyticsWorkbench";
import LocalDraftWorkbench from "./LocalDraftWorkbench";
import BudgetPlanner from "./BudgetPlanner";
import WorkflowHub from "./WorkflowHub";
import { WorkspaceProvider, useWorkspace } from "./WorkspaceStore";
import { ClipboardList, History } from "lucide-react";

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
type Page =
  | "inicio"
  | "bandeja"
  | "movimientos"
  | "categorias"
  | "presupuestos"
  | "analisis"
  | "auditoria";
const transactions: Payment[] = [...demoExpenses.listPosted()];
const DATA_MONTH_LIMIT = new Map(
  [2024, 2025, 2026].map((y) => [
    y,
    Math.max(
      0,
      ...transactions
        .filter((t) => Number(t.date.slice(0, 4)) === y)
        .map((t) => Number(t.date.slice(5, 7))),
    ),
  ]),
);
const NAV = [
  { id: "inicio", label: "Inicio", icon: LayoutDashboard },
  { id: "bandeja", label: "Mi trabajo", icon: ClipboardList },
  { id: "movimientos", label: "Movimientos", icon: ReceiptText },
  { id: "presupuestos", label: "Presupuestos", icon: Wallet },
  { id: "categorias", label: "Categorías", icon: Layers3 },
  { id: "analisis", label: "Análisis", icon: BarChart3 },
  { id: "auditoria", label: "Actividad", icon: History },
] as const;
const MONTHS = ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];
const MONTH_LONG = [
  "enero",
  "febrero",
  "marzo",
  "abril",
  "mayo",
  "junio",
  "julio",
  "agosto",
  "septiembre",
  "octubre",
  "noviembre",
  "diciembre",
];
const number = new Intl.NumberFormat("es-PE", { maximumFractionDigits: 0 });
const pen = new Intl.NumberFormat("es-PE", {
  style: "currency",
  currency: "PEN",
  minimumFractionDigits: 2,
});
const compact = (v: number) =>
  v >= 1000
    ? `S/ ${(v / 1000).toLocaleString("es-PE", { maximumFractionDigits: 1 })} mil`
    : pen.format(v);
const palette = [
  "#416c77",
  "#7ca399",
  "#abc2bb",
  "#d4c3a5",
  "#889fa7",
  "#a7bcae",
  "#c7b6a5",
  "#7b9c96",
  "#cad8d5",
];
function dateLabel(input: string) {
  const [y, m, day] = input.split("-").map(Number);
  return new Date(y, m - 1, day).toLocaleDateString("es-PE", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  });
}
function IconBubble({ icon: Icon, tone = "blue" }: { icon: typeof Activity; tone?: string }) {
  return (
    <span className={`icon-bubble ${tone}`}>
      <Icon aria-hidden="true" size={17} strokeWidth={1.8} />
    </span>
  );
}
function ChartHint({ children }: { children: React.ReactNode }) {
  return <span className="chart-hint">{children}</span>;
}
function SectionHead({
  eyebrow,
  title,
  more,
}: {
  eyebrow: string;
  title: string;
  more?: React.ReactNode;
}) {
  return (
    <div className="section-head">
      <div>
        <span className="eyebrow">{eyebrow}</span>
        <h2>{title}</h2>
      </div>
      {more}
    </div>
  );
}
function TooltipMoney({
  active,
  payload,
  label,
}: {
  active?: boolean;
  payload?: Array<{ name?: string; value?: number }>;
  label?: string | number;
}) {
  if (!active || !payload?.length) return null;
  return (
    <div className="chart-tooltip">
      <strong>{label}</strong>
      {payload.map((p, index) => (
        <div key={index}>
          {p.name}: {compact(Number(p.value ?? 0))}
        </div>
      ))}
    </div>
  );
}
function App() {
  const { state: work } = useWorkspace();
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
  useEffect(() => {
    window.scrollTo({ top: 0, behavior: "auto" });
  }, [page]);
  const scopes = ["Todos", ...Array.from(new Set(transactions.map((t) => t.scope)))];
  const categories = [
    "Todas",
    ...Array.from(new Set(transactions.map((t) => t.category))).sort((a, b) =>
      a.localeCompare(b, "es"),
    ),
  ];
  const filtered = useMemo(
    () =>
      transactions.filter(
        (t) =>
          Number(t.date.slice(0, 4)) === year &&
          (!month || Number(t.date.slice(5, 7)) === month) &&
          (scope === "Todos" || t.scope === scope) &&
          (category === "Todas" || t.category === category) &&
          (!search ||
            [t.id, t.category, t.account, t.branch]
              .join(" ")
              .toLowerCase()
              .includes(search.toLowerCase())),
      ),
    [year, month, scope, category, search],
  );
  const summary = useMemo(() => {
    const total = filtered.reduce((sum, t) => sum + t.amount, 0);
    const shared = filtered
      .filter((t) => t.scope === "Compartido")
      .reduce((sum, t) => sum + t.amount, 0);
    const monthly = month
      ? Array.from({ length: Math.ceil(new Date(year, month, 0).getDate() / 7) }, (_, i) => {
          const current = filtered.filter((t) => {
            const day = Number(t.date.slice(8, 10));
            return day >= i * 7 + 1 && day <= (i + 1) * 7;
          });
          return {
            name: `Sem ${i + 1}`,
            amount: Number(current.reduce((sum, t) => sum + t.amount, 0).toFixed(2)),
            count: current.length,
          };
        })
      : Array.from({ length: DATA_MONTH_LIMIT.get(year) ?? 12 }, (_, i) => {
          const current = filtered.filter((t) => Number(t.date.slice(5, 7)) === i + 1);
          return {
            name: MONTHS[i],
            amount: Number(current.reduce((sum, t) => sum + t.amount, 0).toFixed(2)),
            count: current.length,
          };
        });
    const byCategory = Array.from(new Set(filtered.map((t) => t.category)))
      .map((name) => ({
        name,
        amount: filtered.filter((t) => t.category === name).reduce((s, t) => s + t.amount, 0),
        count: filtered.filter((t) => t.category === name).length,
      }))
      .sort((a, b) => b.amount - a.amount);
    const byFlow = ["Operativo", "Capital", "Impuestos"].map((name) => ({
      name,
      amount: filtered.filter((t) => t.flow === name).reduce((s, t) => s + t.amount, 0),
    }));
    return { total, shared, count: filtered.length, monthly, byCategory, byFlow };
  }, [filtered]);
  const homeContext = useMemo(() => {
    const previousYear = month === 1 || !month ? year - 1 : year;
    const previousMonth = month === 1 ? 12 : month ? month - 1 : 0;
    const throughMonth = DATA_MONTH_LIMIT.get(year) ?? 12;
    const baseline = transactions.filter(
      (t) =>
        Number(t.date.slice(0, 4)) === previousYear &&
        (previousMonth
          ? Number(t.date.slice(5, 7)) === previousMonth
          : Number(t.date.slice(5, 7)) <= throughMonth) &&
        (scope === "Todos" || t.scope === scope) &&
        (category === "Todas" || t.category === category) &&
        (!search ||
          [t.id, t.category, t.account, t.branch]
            .join(" ")
            .toLowerCase()
            .includes(search.toLowerCase())),
    );
    const prior = baseline.reduce((sum, t) => sum + t.amount, 0);
    const branchTotals = new Map<string, { amount: number; count: number }>();
    for (const t of filtered) {
      const name = t.scope === "Compartido" ? "Compartido" : t.branch || "Sin sede";
      const before = branchTotals.get(name) ?? { amount: 0, count: 0 };
      branchTotals.set(name, { amount: before.amount + t.amount, count: before.count + 1 });
    }
    const branches = [...branchTotals]
      .map(([name, values]) => ({ name, ...values }))
      .sort((a, b) => b.amount - a.amount || a.name.localeCompare(b.name, "es"));
    return {
      prior,
      delta: prior > 0 ? ((summary.total - prior) / prior) * 100 : null,
      priorLabel: previousMonth
        ? `${MONTHS[previousMonth - 1]} de ${previousYear}`
        : `${previousYear}, hasta ${MONTHS[Math.max(throughMonth, 1) - 1]}`,
      branches,
      pending: work.expenses.length + work.budgets.length,
      categories: summary.byCategory.length,
    };
  }, [
    filtered,
    year,
    month,
    scope,
    category,
    search,
    summary.total,
    summary.byCategory,
    work.expenses.length,
    work.budgets.length,
  ]);
  const categoryInsights = useMemo(() => {
    const total = summary.total;
    const ordered = summary.byCategory;
    const top = ordered[0];
    const accounts = new Set(filtered.map((item) => item.account).filter(Boolean)).size;
    const months = Array.from({ length: 12 }, (_, index) =>
      filtered
        .filter((item) => Number(item.date.slice(5, 7)) === index + 1)
        .reduce((sum, item) => sum + item.amount, 0),
    );
    const maxMonth = Math.max(...months, 0);
    const peakIndex = months.indexOf(maxMonth);
    return {
      categories: ordered.length,
      accounts,
      top,
      share: total && top ? (top.amount / total) * 100 : 0,
      average: filtered.length ? total / filtered.length : 0,
      months,
      maxMonth,
      peakLabel: maxMonth > 0 ? MONTHS[peakIndex] : "Sin movimientos",
    };
  }, [filtered, summary]);
  const latest = useMemo(
    () => [...filtered].sort((a, b) => b.date.localeCompare(a.date) || b.id.localeCompare(a.id)),
    [filtered],
  );
  const visible = latest.slice(0, page === "movimientos" ? 80 : 6);
  const currentPeriod = month ? `${MONTH_LONG[month - 1]} de ${year}` : `el año ${year}`;
  const clear = () => {
    setMonth(0);
    setScope("Todos");
    setCategory("Todas");
    setSearch("");
  };
  const navigate = (value: Page) => {
    setPage(value);
    setMenuOpen(false);
    setSelected(null);
  };
  const copyReference = async (id: string) => {
    try {
      await navigator.clipboard.writeText(id);
      setCopyMessage("Referencia copiada");
    } catch {
      setCopyMessage("No se pudo copiar; usa el identificador visible.");
    }
  };
  const exportCSV = () => {
    const columns = [
      "Referencia",
      "Fecha",
      "MontoPEN",
      "Categoría",
      "Sede",
      "Ámbito",
      "Tipo",
      "Cuenta",
    ];
    const escape = (v: string | number) => `"${String(v).replaceAll('"', '""')}"`;
    const rows = filtered.map((t) =>
      [t.id, t.date, t.amount.toFixed(2), t.category, t.branch, t.scope, t.flow, t.account]
        .map(escape)
        .join(","),
    );
    const blob = new Blob(["\uFEFF" + [columns.join(","), ...rows].join("\r\n")], {
      type: "text/csv;charset=utf-8",
    });
    const href = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = href;
    a.download = `financial-manager-movimientos-${year}${month ? "-" + month : ""}.csv`;
    a.click();
    URL.revokeObjectURL(href);
  };
  const navContent = (
    <>
      <div className="brand">
        <div className="brand-mark">
          <Layers3 size={21} strokeWidth={2.1} />
        </div>
        <div>
          <strong>Financial Manager</strong>
          <span>Workspace</span>
        </div>
      </div>
      <div className="workspace-switch">
        <Building2 size={18} />
        <span>
          <b>Espacio financiero</b>
          <small>Gestión de operaciones</small>
        </span>
      </div>
      <div className="nav-heading">MI TRABAJO</div>
      <nav aria-label="Navegación principal" className="nav-items">
        {NAV.slice(0, 4).map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            type="button"
            title={label}
            aria-label={label}
            onClick={() => navigate(id)}
            aria-current={page === id ? "page" : undefined}
            className={`nav-item ${page === id ? "active" : ""}`}
          >
            <Icon size={19} strokeWidth={1.9} />
            <span>{label}</span>
            {page === id && <span className="nav-dot" />}
          </button>
        ))}
        <div className="nav-heading secondary">EXPLORAR Y CONTROL</div>
        {NAV.slice(4).map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            type="button"
            title={label}
            aria-label={label}
            onClick={() => navigate(id)}
            aria-current={page === id ? "page" : undefined}
            className={`nav-item ${page === id ? "active" : ""}`}
          >
            <Icon size={19} strokeWidth={1.9} />
            <span>{label}</span>
            {page === id && <span className="nav-dot" />}
          </button>
        ))}
      </nav>
      <div className="nav-heading secondary">SISTEMA</div>
      <button className="nav-item" onClick={() => setHelpOpen(true)}>
        <CircleHelp size={19} />
        <span>Acerca del entorno</span>
      </button>
      <div className="sidebar-bottom">
        <div className="user-profile">
          <span className="avatar">FM</span>
          <span>
            <b>Espacio financiero</b>
            <small>Sesión en este navegador</small>
          </span>
          <span className="profile-circle" />
        </div>
      </div>
    </>
  );
  return (
    <div className={`application ${dark ? "dark" : ""}`}>
      {menuOpen && (
        <button
          className="mobile-overlay"
          aria-label="Cerrar navegación"
          onClick={() => setMenuOpen(false)}
        />
      )}
      <aside className={`sidebar ${menuOpen ? "mobile-open" : ""}`}>{navContent}</aside>
      <div className="app-main">
        <header className="topbar">
          <div className="breadcrumbs">
            <button
              className="hamburger icon-button"
              onClick={() => setMenuOpen(true)}
              aria-label="Abrir navegación"
            >
              <Menu size={20} />
            </button>
            <span>Workspace</span>
            <span className="crumb-slash">/</span>
            <b>{NAV.find((n) => n.id === page)?.label}</b>
          </div>
          <div className="top-actions">
            {import.meta.env.VITE_FM_ENABLE_LIVE_READ === "true" && (
              <a
                className="workspace-live-link"
                href="/?mode=live"
                aria-label="Consultar registros en Dataverse"
              >
                <Database size={15} aria-hidden="true" />
                <span className="workspace-live-label">Consultar Dataverse</span>
              </a>
            )}
            <button
              className="workspace-source-info"
              onClick={() => setHelpOpen(true)}
              title="Información sobre la procedencia de los datos"
            >
              Datos de muestra
            </button>
            <button
              className="icon-button"
              title={dark ? "Tema claro" : "Tema oscuro"}
              aria-label={dark ? "Cambiar a tema claro" : "Cambiar a tema oscuro"}
              onClick={() => setDark(!dark)}
            >
              {dark ? <Sun size={19} /> : <Moon size={19} />}
            </button>
            <button
              className="top-avatar"
              onClick={() => setHelpOpen(true)}
              aria-label="Información de la sesión"
            >
              FM
            </button>
          </div>
        </header>
        <main className="content">
          {(page === "inicio" || page === "categorias") && (
            <div className={`hero ${page === "categorias" ? "hero-compact hero-category" : ""}`}>
              <div className="hero-copy">
                <span className="hero-overline">
                  {page === "inicio" ? "CONTROL FINANCIERO" : "EXPLORADOR DE CATEGORÍAS"}
                </span>
                <h1>
                  {page === "inicio" ? (
                    <>
                      Todo en orden,
                      <br />
                      <em>a tu manera.</em>
                    </>
                  ) : (
                    <>
                      El gasto,
                      <br />
                      <em>bajo la lupa.</em>
                    </>
                  )}
                </h1>
                <p>
                  {page === "inicio"
                    ? "Una vista tranquila de lo importante: gastos, movimientos y decisiones."
                    : "Localiza dónde se concentra el presupuesto y abre los movimientos que explican cada cifra."}
                </p>
              </div>
              <div className="hero-aside">
                {page === "categorias" ? (
                  <div className="hero-category-fact" aria-label="Resumen del período">
                    <span>Ejercicio {year}</span>
                    <strong>{categoryInsights.categories}</strong>
                    <small>categorías con movimientos</small>
                  </div>
                ) : (
                  <>
                    <span className="hero-year">{year}</span>
                    <span className="hero-period">GESTIÓN FINANCIERA</span>
                  </>
                )}
              </div>
            </div>
          )}

          {(["inicio", "movimientos", "categorias", "analisis"] as Page[]).includes(page) && (
            <>
              <div className="period-container">
                <div className="period-caption">
                  <CalendarDays size={18} />
                  <div>
                    <span>PERÍODO DE TRABAJO</span>
                    <strong>{year}</strong>
                  </div>
                  <div className="year-stepper">
                    <button
                      aria-label="Año anterior"
                      onClick={() => {
                        setYear((y) => Math.max(2024, y - 1));
                        setMonth(0);
                      }}
                      disabled={year === 2024}
                    >
                      <ChevronLeft size={16} />
                    </button>
                    <button
                      aria-label="Año siguiente"
                      onClick={() => {
                        setYear((y) => Math.min(2026, y + 1));
                        setMonth(0);
                      }}
                      disabled={year === 2026}
                    >
                      <ChevronRight size={16} />
                    </button>
                  </div>
                </div>
                <div className="month-pills" aria-label="Seleccionar mes">
                  <button
                    className={month === 0 ? "selected" : ""}
                    onClick={() => setMonth(0)}
                    aria-pressed={month === 0}
                  >
                    Año
                  </button>
                  {MONTHS.map((name, i) => (
                    <button
                      className={month === i + 1 ? "selected" : ""}
                      key={name}
                      disabled={i + 1 > (DATA_MONTH_LIMIT.get(year) ?? 12)}
                      title={
                        i + 1 > (DATA_MONTH_LIMIT.get(year) ?? 12)
                          ? "Mes aún no disponible"
                          : undefined
                      }
                      onClick={() => setMonth(i + 1)}
                      aria-pressed={month === i + 1}
                    >
                      {name}
                      <small>{i + 1}</small>
                    </button>
                  ))}
                </div>
              </div>
              <div className="filterline">
                <div className="filterline-left">
                  <SlidersHorizontal size={16} />
                  <span>
                    Vista de <b>{currentPeriod}</b>
                  </span>
                  <label>
                    Ámbito{" "}
                    <select
                      aria-label="Filtrar por ámbito"
                      value={scope}
                      onChange={(e) => setScope(e.target.value)}
                    >
                      {scopes.map((s) => (
                        <option key={s}>{s}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Categoría{" "}
                    <select
                      aria-label="Filtrar por categoría"
                      value={category}
                      onChange={(e) => setCategory(e.target.value)}
                    >
                      {categories.map((c) => (
                        <option key={c}>{c}</option>
                      ))}
                    </select>
                  </label>
                  {(scope !== "Todos" || category !== "Todas" || month !== 0 || search) && (
                    <button className="text-action" onClick={clear}>
                      Restablecer
                    </button>
                  )}
                </div>
              </div>
            </>
          )}

          {page === "inicio" && (
            <>
              <div className="home-command-rail">
                <div>
                  <span className="eyebrow">CENTRO DE OPERACIONES</span>
                  <strong>Lo que necesitas para seguir trabajando</strong>
                  <p>
                    {work.expenses.length + work.budgets.length
                      ? `${work.expenses.length} movimientos y ${work.budgets.length} presupuestos en preparación.`
                      : "Comienza registrando un movimiento o preparando tu presupuesto."}
                  </p>
                </div>
                <button onClick={() => navigate("bandeja")}>
                  <ClipboardList size={18} /> Ir a mi trabajo <ArrowRight size={15} />
                </button>
                <button onClick={() => navigate("movimientos")}>
                  <ReceiptText size={18} /> Registrar movimiento <ArrowRight size={15} />
                </button>
              </div>
              <section className="overview-metrics" aria-label="Resumen del período">
                <div className="lead-metric">
                  <span className="eyebrow">TOTAL CONFIRMADO · PEN</span>
                  <div className="lead-number">{compact(summary.total)}</div>
                  <div className="lead-caption">
                    <IconBubble icon={CheckCheck} tone="mint" />
                    <span>{number.format(summary.count)} pagos contabilizados en la selección</span>
                  </div>
                </div>
                <div className="support-metrics">
                  <div>
                    <IconBubble icon={ReceiptText} tone="blue" />
                    <span>Transacciones</span>
                    <b>{number.format(summary.count)}</b>
                    <small>Confirmadas</small>
                  </div>
                  <div>
                    <IconBubble icon={Activity} tone="rose" />
                    <span>Ticket promedio</span>
                    <b>{compact(summary.count ? summary.total / summary.count : 0)}</b>
                    <small>Por movimiento</small>
                  </div>
                  <div>
                    <IconBubble icon={Layers3} tone="lavender" />
                    <span>Gasto compartido</span>
                    <b>{compact(summary.shared)}</b>
                    <small>
                      {summary.total ? ((summary.shared / summary.total) * 100).toFixed(1) : "0"}%
                      del gasto
                    </small>
                  </div>
                </div>
              </section>
              <section className="home-brief" aria-label="Contexto del período">
                <article className="home-brief-card">
                  <span className="eyebrow">COMPARATIVO</span>
                  <strong className="home-brief-heading">
                    {homeContext.delta === null
                      ? "Sin base comparable"
                      : `${homeContext.delta > 0 ? "+" : ""}${homeContext.delta.toLocaleString("es-PE", { maximumFractionDigits: 1 })} %`}
                  </strong>
                  <p>
                    {homeContext.delta === null
                      ? "No hay movimientos en el período de referencia."
                      : `${homeContext.delta > 0 ? "Mayor" : homeContext.delta < 0 ? "Menor" : "Igual"} importe frente a ${homeContext.priorLabel}.`}
                  </p>
                  <div className="home-brief-sub">
                    <span>Período anterior</span>
                    <b>{compact(homeContext.prior)}</b>
                  </div>
                </article>
                <article className="home-brief-card">
                  <div className="home-brief-top">
                    <span className="eyebrow">DISTRIBUCIÓN POR SEDE</span>
                    <button onClick={() => navigate("categorias")}>
                      Explorar <ArrowRight size={14} />
                    </button>
                  </div>
                  <div className="home-brief-branches">
                    {homeContext.branches.slice(0, 4).map((branch) => (
                      <div key={branch.name}>
                        <div className="home-brief-branch-label">
                          <span>{branch.name}</span>
                          <b>
                            {summary.total
                              ? ((100 * branch.amount) / summary.total).toFixed(1)
                              : "0"}{" "}
                            %
                          </b>
                        </div>
                        <div className="home-brief-meter">
                          <span
                            style={{
                              width: `${summary.total ? (100 * branch.amount) / summary.total : 0}%`,
                            }}
                          />
                        </div>
                      </div>
                    ))}
                    {!homeContext.branches.length && <p>Sin movimientos para esta selección.</p>}
                  </div>
                </article>
                <article className="home-brief-card home-brief-next">
                  <span className="eyebrow">EN PREPARACIÓN</span>
                  <strong className="home-brief-heading">
                    {number.format(homeContext.pending)}
                  </strong>
                  <p>Borradores iniciados en esta sesión que puedes continuar.</p>
                  <div className="home-brief-sub">
                    <span>Categorías con gasto</span>
                    <b>{homeContext.categories}</b>
                  </div>
                  <button onClick={() => navigate("bandeja")}>
                    Continuar registros <ArrowRight size={15} />
                  </button>
                </article>
              </section>
              <SectionHead
                eyebrow="TU PULSO FINANCIERO"
                title="Lo que pasó en este período"
                more={
                  <button className="inline-link" onClick={() => navigate("analisis")}>
                    Profundizar <ArrowUpRight size={16} />
                  </button>
                }
              />
              <div className="analysis-grid">
                <article className="surface chart-surface">
                  <div className="surface-heading">
                    <div>
                      <h3>Movimiento del gasto</h3>
                      <ChartHint>
                        {month
                          ? "Distribución semanal del mes en PEN"
                          : "Importes mensuales expresados en PEN"}
                      </ChartHint>
                    </div>
                    <span className="mini-label">
                      {month ? `Semanas de ${MONTH_LONG[month - 1]}` : `Evolución ${year}`}
                    </span>
                  </div>
                  <div
                    className="large-chart"
                    role="img"
                    aria-label="Gráfico del gasto total por mes"
                  >
                    <ResponsiveContainer width="100%" height="100%">
                      <AreaChart
                        data={summary.monthly}
                        margin={{ top: 15, right: 6, left: -13, bottom: 2 }}
                      >
                        <defs>
                          <linearGradient id="spendingArea" x1="0" y1="0" x2="0" y2="1">
                            <stop offset="0%" stopColor="#91b0b0" stopOpacity=".35" />
                            <stop offset="95%" stopColor="#91b0b0" stopOpacity=".02" />
                          </linearGradient>
                        </defs>
                        <CartesianGrid
                          vertical={false}
                          stroke="var(--grid)"
                          strokeDasharray="2 5"
                        />
                        <XAxis
                          dataKey="name"
                          axisLine={false}
                          tickLine={false}
                          tick={{ fill: "var(--muted)", fontSize: 11 }}
                          interval={0}
                        />
                        <YAxis
                          axisLine={false}
                          tickLine={false}
                          tick={{ fill: "var(--muted)", fontSize: 11 }}
                          tickFormatter={(v) => `${Math.round(v / 1000)}k`}
                        />
                        <Tooltip content={<TooltipMoney />} />
                        <Area
                          name="Gasto"
                          type="monotone"
                          dataKey="amount"
                          stroke="#587e83"
                          strokeWidth={2.7}
                          fill="url(#spendingArea)"
                          dot={false}
                          activeDot={{ r: 5 }}
                          isAnimationActive={false}
                        />
                      </AreaChart>
                    </ResponsiveContainer>
                  </div>
                  <div className="chart-bottom">
                    <span>
                      <span className="legend-dot teal" />
                      Gasto mensual
                    </span>
                    <span>Periodo seleccionado · PEN</span>
                  </div>
                </article>
                <article className="surface category-surface">
                  <div className="surface-heading">
                    <div>
                      <h3>¿Dónde se concentra?</h3>
                      <ChartHint>Categorías con mayor gasto</ChartHint>
                    </div>
                    <IconBubble icon={Layers3} tone="lavender" />
                  </div>
                  <div className="category-list">
                    {summary.byCategory.slice(0, 5).map((cat, i) => (
                      <button
                        className="category-row"
                        key={cat.name}
                        onClick={() => {
                          setCategory(cat.name);
                          navigate("movimientos");
                        }}
                      >
                        <span
                          className="category-square"
                          style={{ background: palette[i % palette.length] }}
                        />
                        <span className="category-name">{cat.name}</span>
                        <span className="category-money">{compact(cat.amount)}</span>
                        <span className="category-progress">
                          <span
                            style={{
                              width: `${summary.total ? (100 * cat.amount) / summary.total : 0}%`,
                              background: palette[i % palette.length],
                            }}
                          />
                        </span>
                      </button>
                    ))}
                  </div>
                  <button className="subtle-link" onClick={() => navigate("categorias")}>
                    Ver todas las categorías <ArrowRight size={16} />
                  </button>
                </article>
              </div>
              <SectionHead
                eyebrow="PAGOS CONFIRMADOS"
                title="Movimientos a la vista"
                more={
                  <button className="inline-link" onClick={() => navigate("movimientos")}>
                    Ver historial <ArrowRight size={16} />
                  </button>
                }
              />
              <div className="operational-grid">
                <TransactionList
                  payments={visible}
                  onSelect={setSelected}
                  selected={selected?.id}
                  compactList
                />
                <aside className="insight-panel">
                  <div className="insight-overline">LECTURA RÁPIDA</div>
                  <div className="insight-graphic">
                    <span
                      className="insight-ring"
                      style={{
                        background: `conic-gradient(#698c8b ${summary.total ? (summary.shared / summary.total) * 100 : 0}%, #e4e9e6 0)`,
                      }}
                    >
                      <span>
                        {summary.total ? ((summary.shared / summary.total) * 100).toFixed(1) : "0"}
                        <small>%</small>
                      </span>
                    </span>
                    <span>
                      <strong>Compartido</strong>
                      <small>Del importe confirmado</small>
                    </span>
                  </div>
                  <div className="insight-rule" />
                  {summary.byFlow.map((flow, i) => (
                    <div className="flow-row" key={flow.name}>
                      <span className="flow-dot" style={{ background: palette[i] }} />
                      {flow.name}
                      <strong>{compact(flow.amount)}</strong>
                    </div>
                  ))}
                  <p>Importes de pagos contabilizados; no equivalen a saldo disponible.</p>
                </aside>
              </div>
            </>
          )}
          {page === "movimientos" && (
            <>
              <SectionHead
                eyebrow="GESTIÓN DIARIA"
                title="Registro de movimientos"
                more={
                  <button className="inline-link" onClick={() => navigate("bandeja")}>
                    Mi bandeja <ArrowRight size={16} />
                  </button>
                }
              />
              <LocalDraftWorkbench
                categories={categories.slice(1)}
                branches={[
                  ...new Set(
                    transactions
                      .map((t) => t.branch)
                      .filter((name) => name && name !== "Compartido"),
                  ),
                ].sort()}
                accounts={[...new Set(transactions.map((t) => t.account))].sort()}
              />
              <div className="toolstrip">
                <label className="search-field">
                  <Search size={18} />
                  <input
                    aria-label="Buscar movimiento"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Buscar referencia, cuenta o categoría..."
                  />
                </label>
                <button className="outline-button" onClick={exportCSV} disabled={!filtered.length}>
                  <Download size={16} /> Exportar CSV
                </button>
              </div>
              <div className="page-summary">
                <div>
                  <span>REGISTROS EN LA SELECCIÓN</span>
                  <strong>{number.format(summary.count)}</strong>
                </div>
                <div>
                  <span>IMPORTE TOTAL</span>
                  <strong>{compact(summary.total)}</strong>
                </div>
                <div>
                  <span>ÚLTIMO MOVIMIENTO</span>
                  <strong>{latest.length ? dateLabel(latest[0].date) : "Sin datos"}</strong>
                </div>
              </div>
              <TransactionList payments={visible} onSelect={setSelected} selected={selected?.id} />
              {visible.length < filtered.length && (
                <p className="table-footnote">
                  Mostrando los 80 movimientos más recientes del filtro. La exportación incluye
                  todos los registros filtrados.
                </p>
              )}
            </>
          )}
          {page === "categorias" && (
            <>
              <section className="category-brief" aria-label="Lectura ejecutiva de categorías">
                <div className="category-brief-overview">
                  <span className="eyebrow">CATEGORÍA PRINCIPAL</span>
                  <h2>{categoryInsights.top?.name ?? "Sin movimientos"}</h2>
                  <p>
                    {categoryInsights.top
                      ? `${compact(categoryInsights.top.amount)} en ${number.format(categoryInsights.top.count)} movimientos. Representa el ${categoryInsights.share.toFixed(1)} % del importe filtrado.`
                      : "Selecciona otro período para revisar la distribución del gasto."}
                  </p>
                  {categoryInsights.top && (
                    <button
                      onClick={() => {
                        setCategory(categoryInsights.top!.name);
                        navigate("movimientos");
                      }}
                    >
                      Examinar movimientos <ArrowRight size={15} />
                    </button>
                  )}
                </div>
                <div className="category-brief-stat">
                  <span>Importe promedio</span>
                  <strong>{compact(categoryInsights.average)}</strong>
                  <small>Por movimiento del período</small>
                </div>
                <div className="category-brief-stat">
                  <span>Cuentas involucradas</span>
                  <strong>{categoryInsights.accounts}</strong>
                  <small>Con movimientos registrados</small>
                </div>
                <div className="category-brief-trend">
                  <div>
                    <span>Distribución mensual</span>
                    <strong>
                      {categoryInsights.peakLabel} <small>mayor importe</small>
                    </strong>
                  </div>
                  <div className="category-brief-bars" aria-label="Importes mensuales relativos">
                    {categoryInsights.months.map((amount, index) => (
                      <span
                        key={index}
                        title={`${MONTHS[index]}: ${pen.format(amount)}`}
                        style={{
                          height: `${categoryInsights.maxMonth ? Math.max(5, (amount / categoryInsights.maxMonth) * 100) : 5}%`,
                        }}
                      />
                    ))}
                  </div>
                </div>
              </section>
              <SectionHead
                eyebrow="EXPLORACIÓN FINANCIERA"
                title="Comprende el origen del gasto"
                more={
                  <button className="inline-link" onClick={() => navigate("movimientos")}>
                    Ver registro completo <ArrowRight size={16} />
                  </button>
                }
              />
              <CategoryExplorer
                records={filtered}
                onOpenTransactions={(name) => {
                  setCategory(name);
                  setSearch("");
                  navigate("movimientos");
                }}
              />
            </>
          )}
          {page === "bandeja" && <WorkflowHub view="bandeja" onNavigate={navigate} />}
          {page === "presupuestos" && (
            <BudgetPlanner
              categories={categories.slice(1)}
              branches={[...new Set(transactions.map((t) => t.branch).filter(Boolean))].sort()}
            />
          )}
          {page === "auditoria" && <WorkflowHub view="auditoria" onNavigate={navigate} />}
          {page === "analisis" && (
            <AnalyticsWorkbench
              payments={transactions}
              year={year}
              month={month}
              scope={scope}
              category={category}
              onOpenCategory={(name) => {
                setCategory(name);
                setSearch("");
                navigate("movimientos");
              }}
              onOpenTransaction={(record) => {
                setSearch("");
                navigate("movimientos");
                setSelected(transactions.find((t) => t.id === record.id) ?? null);
              }}
            />
          )}
          <footer className="main-footer">
            <span>Financial Manager</span>
            <span>Importes expresados en soles peruanos</span>
          </footer>
        </main>
      </div>
      {selected && (
        <>
          <button
            className="drawer-backdrop"
            aria-label="Cerrar detalle"
            onClick={() => {
              setSelected(null);
              setCopyMessage("");
            }}
          />
          <aside className="detail-drawer" aria-label="Detalle del movimiento">
            <div className="drawer-title">
              <span>DETALLE DEL MOVIMIENTO</span>
              <button
                className="icon-button"
                aria-label="Cerrar detalle"
                onClick={() => {
                  setSelected(null);
                  setCopyMessage("");
                }}
              >
                <X size={20} />
              </button>
            </div>
            <div className="drawer-symbol">
              <ArrowDownLeft size={26} />
            </div>
            <h2>{selected.id}</h2>
            <p>Movimiento contabilizado</p>
            <div className="drawer-amount">{pen.format(selected.amount)}</div>
            <span className="drawer-success">
              <Check size={14} /> Contabilizado
            </span>
            <div className="drawer-fields">
              {(
                [
                  ["Fecha de pago", dateLabel(selected.date)],
                  ["Categoría", selected.category],
                  ["Sede", selected.branch],
                  ["Ámbito", selected.scope],
                  ["Cuenta de origen", selected.account],
                  ["Clasificación", selected.flow],
                ] as const
              ).map(([label, value]) => (
                <div key={label}>
                  <span>{label}</span>
                  <strong>{value}</strong>
                </div>
              ))}
            </div>
            <button className="outline-button full" onClick={() => void copyReference(selected.id)}>
              <Copy size={16} /> Copiar referencia
            </button>
            {copyMessage && (
              <p className="copy-status" role="status">
                {copyMessage}
              </p>
            )}
            <div className="drawer-warning">
              <ShieldCheck size={17} /> El movimiento contabilizado no admite edición directa.
            </div>
          </aside>
        </>
      )}
      {helpOpen && (
        <div
          className="modal-overlay"
          role="presentation"
          onMouseDown={(e) => {
            if (e.currentTarget === e.target) setHelpOpen(false);
          }}
        >
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="help-heading"
            className="help-modal"
          >
            <button
              className="icon-button modal-close"
              onClick={() => setHelpOpen(false)}
              aria-label="Cerrar"
            >
              <X size={19} />
            </button>
            <IconBubble icon={ShieldCheck} tone="mint" />
            <h2 id="help-heading">Sobre los datos de esta vista</h2>
            <p>
              Los gráficos y movimientos de esta vista utilizan datos de muestra para explorar el
              producto. Las consultas a datos empresariales se realizan en la vista conectada
              mediante Microsoft Entra y Dataverse.
            </p>
            <p>
              Las anotaciones y los borradores de esta vista permanecen en el navegador y no se
              envían a Dataverse. Para consultar información real, usa «Consultar Dataverse» e
              inicia sesión. Las operaciones financieras protegidas requieren autorización adicional
              del servidor.
            </p>
            <button className="primary-button" onClick={() => setHelpOpen(false)}>
              Entendido <ArrowRight size={16} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
function TransactionList({
  payments,
  onSelect,
  selected,
  compactList = false,
}: {
  payments: Payment[];
  onSelect: (p: Payment) => void;
  selected?: string;
  compactList?: boolean;
}) {
  return (
    <div className={`surface transaction-list ${compactList ? "compact-list" : ""}`}>
      <div className="surface-heading">
        <div>
          <h3>{compactList ? "Últimos pagos registrados" : "Movimientos confirmados"}</h3>
          <ChartHint>Selecciona cualquier registro para explorar sus detalles</ChartHint>
        </div>
        <span className="mini-label">{number.format(payments.length)} visibles</span>
      </div>
      <div
        className="table-scroll"
        role="region"
        aria-label="Tabla de movimientos, desplazable horizontalmente"
        tabIndex={0}
      >
        <table>
          <thead>
            <tr>
              <th>Movimiento</th>
              <th>Categoría</th>
              <th>Fecha</th>
              <th>Importe</th>
              <th>
                <span className="sr-only">Detalle</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {payments.length ? (
              payments.map((t) => (
                <tr
                  key={t.id}
                  className={selected === t.id ? "row-active" : ""}
                  onClick={() => onSelect(t)}
                >
                  <td>
                    <button className="row-link" onClick={() => onSelect(t)}>
                      {t.id}
                      <small>{t.scope === "Compartido" ? "Compartido" : t.branch}</small>
                    </button>
                  </td>
                  <td>
                    <span className="table-category-dot" />
                    {t.category}
                  </td>
                  <td>{dateLabel(t.date)}</td>
                  <td className="numeric">{pen.format(t.amount)}</td>
                  <td>
                    <ArrowUpRight size={16} />
                  </td>
                </tr>
              ))
            ) : (
              <tr>
                <td colSpan={5} className="empty-state">
                  No hay movimientos que coincidan con estos filtros. Cambia el período o restablece
                  la selección.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
export default function FinancialWorkspace() {
  return (
    <WorkspaceProvider>
      <App />
    </WorkspaceProvider>
  );
}
