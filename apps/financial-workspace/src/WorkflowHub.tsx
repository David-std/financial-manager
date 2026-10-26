import { useMemo, useState } from "react";
import {
  Activity,
  ArrowRight,
  CalendarDays,
  CheckCircle2,
  CircleDot,
  ClipboardList,
  FilePenLine,
  FolderOpen,
  History,
  LockKeyhole,
  Search,
  ShieldCheck,
  Wallet,
} from "lucide-react";
import { useWorkspace } from "./WorkspaceStore";
import type { ExpenseDraft, SessionEvent } from "./WorkspaceStore";
import "./workflow-hub.css";
const currency = new Intl.NumberFormat("es-PE", { style: "currency", currency: "PEN" });
function missing(d: ExpenseDraft): string[] {
  const names: [keyof ExpenseDraft, string][] = [
    ["amount", "Importe"],
    ["date", "Fecha"],
    ["category", "Categoría"],
    ["account", "Cuenta"],
    ["description", "Descripción"],
  ];
  if (d.scope === "Por sede") names.push(["branch", "Sede"]);
  return names.filter(([k]) => !d[k]).map(([, name]) => name);
}
function EventFeed({ events, limit = 10 }: { events: SessionEvent[]; limit?: number }) {
  return (
    <div className="work-events">
      {events.length ? (
        events.slice(0, limit).map((e) => (
          <div className="work-event" key={e.sequence}>
            <span className="work-event-marker">
              <CircleDot size={13} />
            </span>
            <div>
              <span className="work-event-title">
                <strong>{e.action}</strong> · {e.object}
              </span>
              <p>
                {e.summary} · <code>{e.reference}</code>
              </p>
              <time dateTime={e.at}>
                {new Date(e.at).toLocaleString("es-PE", {
                  dateStyle: "medium",
                  timeStyle: "short",
                })}
              </time>
            </div>
          </div>
        ))
      ) : (
        <div className="work-events-empty">
          <Activity size={18} />
          <span>Los cambios de esta sesión aparecerán aquí al crear o modificar registros.</span>
        </div>
      )}
    </div>
  );
}
export default function WorkflowHub({
  view,
  onNavigate,
}: {
  view: "bandeja" | "auditoria";
  onNavigate: (p: "movimientos" | "presupuestos") => void;
}) {
  const { state } = useWorkspace();
  const [search, setSearch] = useState("");
  const [object, setObject] = useState<"Todos" | "Movimiento" | "Presupuesto">("Todos");
  const items = useMemo(
    () =>
      [
        ...state.expenses.map((d) => ({
          id: d.id,
          type: "Movimiento" as const,
          title: d.description || d.category || "Nuevo movimiento",
          detail: `${d.scope} · ${d.date || "Fecha pendiente"}`,
          amount: d.amount ? currency.format(Number(d.amount)) : "Importe pendiente",
          missing: missing(d),
          page: "movimientos" as const,
          updated: d.modifiedAt,
        })),
        ...state.budgets.map((b) => ({
          id: b.id,
          type: "Presupuesto" as const,
          title: b.name,
          detail: `${b.year} · ${b.scope}`,
          amount: currency.format(b.lines.reduce((s, l) => s + l.amount, 0)),
          missing: b.lines.length ? [] : ["Asignaciones"],
          page: "presupuestos" as const,
          updated: b.modifiedAt,
        })),
      ].sort((a, b) => b.updated.localeCompare(a.updated)),
    [state.expenses, state.budgets],
  );
  const filtered = items.filter(
    (i) =>
      (object === "Todos" || object === i.type) &&
      [i.title, i.id, i.detail].join(" ").toLowerCase().includes(search.toLowerCase()),
  );
  const attention = items.filter((i) => i.missing.length);
  if (view === "auditoria")
    return (
      <section className="audit-workspace">
        <div className="audit-heading">
          <div className="audit-symbol">
            <History size={26} />
          </div>
          <div>
            <span className="eyebrow">TRAZABILIDAD Y CAMBIOS</span>
            <h2>Historial de actividad</h2>
            <p>
              Consulta la secuencia de operaciones realizadas durante esta sesión. Cada evento
              conserva su referencia y momento.
            </p>
          </div>
        </div>
        <div className="audit-layout">
          <div className="audit-main">
            <div className="audit-main-top">
              <h3>Registro de acciones</h3>
              <span>{state.events.length} eventos</span>
            </div>
            <EventFeed events={state.events} limit={250} />
          </div>
          <aside className="audit-explain">
            <ShieldCheck size={27} />
            <h3>La procedencia importa.</h3>
            <p>
              El historial de este espacio refleja solo interacciones locales. La auditoría oficial
              requiere registros autenticados de Dataverse, atribución del actor y permisos para
              leerlos.
            </p>
            <div className="audit-diff">
              <span>ACCIÓN</span>
              <strong>Guardado / Edición / Descarte</strong>
              <span>REFERENCIA</span>
              <strong>Identificador de la sesión</strong>
              <span>RESPONSABLE</span>
              <strong>Sin identidad empresarial</strong>
            </div>
            <div className="audit-lock">
              <LockKeyhole size={16} /> La auditoría financiera real no se modifica desde esta
              pantalla.
            </div>
          </aside>
        </div>
      </section>
    );
  return (
    <section className="work-center">
      <div className="work-center-header">
        <div className="work-header-icon">
          <ClipboardList size={25} />
        </div>
        <div>
          <span className="eyebrow">CENTRO DE TRABAJO</span>
          <h2>Tu espacio de seguimiento</h2>
          <p>
            Continúa los registros que comenzaste y revisa qué información falta antes de enviarlos
            a validación.
          </p>
        </div>
        <span className="work-header-count">{items.length} en curso</span>
      </div>
      <div className="work-stats" aria-label="Estado del trabajo">
        <div>
          <span>Movimientos por completar</span>
          <strong>{state.expenses.length}</strong>
          <FilePenLine size={19} />
        </div>
        <div>
          <span>Planes en preparación</span>
          <strong>{state.budgets.length}</strong>
          <Wallet size={19} />
        </div>
        <div>
          <span>Con campos pendientes</span>
          <strong>{attention.length}</strong>
          <CalendarDays size={19} />
        </div>
      </div>
      <div className="work-layout">
        <div className="work-main">
          <div className="work-list-toolbar">
            <div>
              <h3>En curso</h3>
              <p>Movimientos y presupuestos que requieren continuación</p>
            </div>
            <label className="work-search">
              <Search size={16} />
              <input
                aria-label="Buscar en bandeja"
                placeholder="Buscar por referencia..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </label>
          </div>
          <div className="work-tabs" role="group" aria-label="Filtrar registros">
            {(["Todos", "Movimiento", "Presupuesto"] as const).map((x) => (
              <button key={x} aria-pressed={object === x} onClick={() => setObject(x)}>
                {x === "Todos" ? "Todos" : x + "s"}{" "}
                <span>
                  {x === "Todos" ? items.length : items.filter((i) => i.type === x).length}
                </span>
              </button>
            ))}
          </div>
          <div className="work-records" role="list">
            {filtered.length ? (
              filtered.map((item) => (
                <button
                  type="button"
                  role="listitem"
                  className="work-record"
                  key={item.id}
                  onClick={() => onNavigate(item.page)}
                >
                  <span className="work-record-icon">
                    {item.page === "movimientos" ? <FilePenLine size={20} /> : <Wallet size={20} />}
                  </span>
                  <span className="work-record-details">
                    <strong>{item.title}</strong>
                    <small>
                      {item.id} · {item.detail}
                    </small>
                    <span className="work-tags">
                      {item.missing.length ? (
                        <>
                          <span className="work-flag">
                            {item.missing.length} campo(s) por revisar
                          </span>
                          <span>{item.missing.slice(0, 3).join(", ")}</span>
                        </>
                      ) : (
                        <span className="work-good">
                          <CheckCircle2 size={13} /> Datos básicos completos
                        </span>
                      )}
                    </span>
                  </span>
                  <span className="work-record-amount">
                    <b>{item.amount}</b>
                    <small>
                      Continuar <ArrowRight size={15} />
                    </small>
                  </span>
                </button>
              ))
            ) : (
              <div className="work-blank">
                <FolderOpen size={31} />
                <strong>
                  {items.length ? "No hay coincidencias" : "Sin registros pendientes"}
                </strong>
                <p>
                  {items.length
                    ? "Prueba con otra búsqueda o filtro."
                    : "Esta bandeja muestra los movimientos y presupuestos que prepares. Comienza desde una de las acciones disponibles."}
                </p>
                <div>
                  <button onClick={() => onNavigate("movimientos")}>
                    <FilePenLine size={16} /> Nuevo movimiento
                  </button>
                  <button onClick={() => onNavigate("presupuestos")}>
                    <Wallet size={16} /> Planificar presupuesto
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
        <aside className="work-right">
          <div className="work-aside-title">
            <span className="eyebrow">ÚLTIMA ACTIVIDAD</span>
            <History size={17} />
          </div>
          <EventFeed events={state.events} limit={6} />
          <div className="work-next">
            <span className="eyebrow">PRÓXIMO PASO</span>
            <h3>Del registro a la revisión</h3>
            <p>
              Completar borradores es el primer paso. Las transiciones que alteran los pagos o
              aprobaciones deben seguir las reglas verificadas del servidor.
            </p>
            <span>
              <LockKeyhole size={15} /> Transiciones protegidas
            </span>
          </div>
        </aside>
      </div>
    </section>
  );
}
