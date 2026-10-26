import { useMemo, useState } from "react";
import {
  CalendarRange,
  Check,
  ChevronRight,
  CircleHelp,
  FilePlus2,
  Layers3,
  LockKeyhole,
  Plus,
  Save,
  Trash2,
} from "lucide-react";
import { useWorkspace } from "./WorkspaceStore";
import type { BudgetLine } from "./WorkspaceStore";
import "./budget-planner.css";
const months = ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];
const format = new Intl.NumberFormat("es-PE", { style: "currency", currency: "PEN" });
const compact = (n: number) =>
  n >= 1000
    ? "S/ " + (n / 1000).toLocaleString("es-PE", { maximumFractionDigits: 1 }) + " mil"
    : format.format(n);
type Form = { name: string; year: number; scope: string; branch: string; lines: BudgetLine[] };
const empty = (): Form => ({ name: "", year: 2026, scope: "Compartido", branch: "", lines: [] });
export default function BudgetPlanner({
  categories,
  branches,
}: {
  categories: string[];
  branches: string[];
}) {
  const { state, send } = useWorkspace();
  const [selected, setSelected] = useState<string | null>(null);
  const [form, setForm] = useState<Form | null>(null);
  const [notice, setNotice] = useState("");
  const [nextLine, setNextLine] = useState(1);
  const [error, setError] = useState("");
  const plans = state.budgets;
  const active = plans.find((p) => p.id === selected);
  const shown =
    form ??
    (active
      ? {
          name: active.name,
          year: active.year,
          scope: active.scope,
          branch: active.branch,
          lines: active.lines,
        }
      : null);
  const total = shown?.lines.reduce((a, b) => a + b.amount, 0) ?? 0;
  const monthTotals = useMemo(
    () =>
      months.map((name, i) => ({
        name,
        amount:
          shown?.lines.filter((l) => l.month === i + 1).reduce((sum, l) => sum + l.amount, 0) ?? 0,
      })),
    [shown],
  );
  const maxAmount = Math.max(...monthTotals.map((m) => m.amount), 1);
  const create = () => {
    setSelected(null);
    setForm(empty());
    setNotice("");
    setError("");
  };
  const select = (id: string) => {
    setSelected(id);
    setForm(null);
    setNotice("");
    setError("");
  };
  const edit = () => {
    if (active)
      setForm({
        name: active.name,
        year: active.year,
        scope: active.scope,
        branch: active.branch,
        lines: active.lines.map((l) => ({ ...l })),
      });
  };
  const patch = (key: keyof Omit<Form, "lines">, value: string | number) =>
    setForm((old) =>
      old ? { ...old, [key]: value, ...(key === "scope" ? { branch: "" } : {}) } : old,
    );
  const addLine = () => {
    if (!form) return;
    setForm({
      ...form,
      lines: [
        ...form.lines,
        { id: "line-" + nextLine, category: categories[0] ?? "", month: 1, amount: 0 },
      ],
    });
    setNextLine((n) => n + 1);
    setError("");
  };
  const patchLine = (id: string, key: keyof BudgetLine, value: string | number) =>
    setForm((old) =>
      old
        ? { ...old, lines: old.lines.map((l) => (l.id === id ? { ...l, [key]: value } : l)) }
        : old,
    );
  const save = () => {
    if (!form) return;
    if (!form.name.trim()) {
      setError("Introduce un nombre para identificar el presupuesto.");
      return;
    }
    if (form.name.length > 120) {
      setError("El nombre no puede superar 120 caracteres.");
      return;
    }
    if (![2024, 2025, 2026, 2027, 2028].includes(form.year)) {
      setError("Selecciona un ejercicio permitido.");
      return;
    }
    if (form.scope === "Por sede" && !form.branch) {
      setError("Selecciona una sede para el presupuesto.");
      return;
    }
    if (
      form.lines.some(
        (l) =>
          !categories.includes(l.category) ||
          l.month < 1 ||
          l.month > 12 ||
          !Number.isFinite(l.amount) ||
          l.amount < 0 ||
          Math.round(l.amount * 100) !== l.amount * 100,
      )
    ) {
      setError(
        "Corrige categorías, meses o importes. Usa importes no negativos con hasta dos decimales.",
      );
      return;
    }
    const duplicates = new Set(form.lines.map((l) => l.category + "|" + l.month));
    if (duplicates.size !== form.lines.length) {
      setError("No repitas la misma categoría y mes; modifica la asignación existente.");
      return;
    }
    send({
      type: "saveBudget",
      id: selected ?? undefined,
      plan: { ...form, name: form.name.trim() },
    });
    setNotice("El presupuesto se ha guardado en esta sesión.");
    setError("");
    setForm(null);
  };
  return (
    <section className="planning-workspace" aria-label="Planificación de presupuestos">
      <div className="plan-intro">
        <div>
          <span className="eyebrow">PLANIFICACIÓN FINANCIERA</span>
          <h3>Prepara tu próximo ejercicio.</h3>
          <p>
            Construye asignaciones por categoría y mes. El calendario muestra en qué momentos se
            concentra la planificación.
          </p>
        </div>
        <button className="plan-new" onClick={create}>
          <FilePlus2 size={17} /> Nuevo presupuesto
        </button>
      </div>
      <div className="plan-grid">
        <aside className="plan-library">
          <div className="plan-label">
            <span>BORRADORES</span>
            <strong>{plans.length}</strong>
          </div>
          {plans.length ? (
            plans.map((plan) => (
              <button
                type="button"
                key={plan.id}
                aria-current={selected === plan.id ? "true" : undefined}
                className={"plan-list-item " + (selected === plan.id ? "chosen" : "")}
                onClick={() => select(plan.id)}
              >
                <span className="plan-year">{plan.year}</span>
                <strong>{plan.name}</strong>
                <small>
                  {plan.scope === "Por sede" ? plan.branch : "Compartido"} ·{" "}
                  {compact(plan.lines.reduce((a, b) => a + b.amount, 0))}
                </small>
                <ChevronRight size={16} />
              </button>
            ))
          ) : (
            <div className="plan-library-empty">
              <Layers3 size={21} />
              <span>Sin presupuestos todavía.</span>
              <small>Crea un borrador para organizar las asignaciones mensuales.</small>
            </div>
          )}
          <div className="plan-rule">
            <LockKeyhole size={18} />
            <p>
              <strong>Decisiones separadas</strong>
              <span>
                Las aprobaciones reales requieren otra identidad autorizada y controles del
                servidor.
              </span>
            </p>
          </div>
        </aside>
        <div className="plan-editor">
          {shown ? (
            <>
              <div className="plan-editor-header">
                <div>
                  <span className="eyebrow">{form ? "EDITANDO" : "VISTA DEL PLAN"}</span>
                  <h4>{form ? "Asignaciones del ejercicio" : shown.name}</h4>
                </div>
                <span className="plan-status">
                  <span /> Borrador
                </span>
              </div>
              {form && (
                <div className="plan-fields">
                  <label>
                    Nombre del plan
                    <input
                      aria-label="Nombre del plan"
                      value={form.name}
                      onChange={(e) => patch("name", e.target.value)}
                      placeholder="Ej. Operación anual 2026"
                    />
                  </label>
                  <label>
                    Ejercicio
                    <select
                      aria-label="Ejercicio"
                      value={form.year}
                      onChange={(e) => patch("year", Number(e.target.value))}
                    >
                      {[2024, 2025, 2026, 2027, 2028].map((y) => (
                        <option key={y}>{y}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Ámbito
                    <select
                      aria-label="Ámbito del plan"
                      value={form.scope}
                      onChange={(e) => patch("scope", e.target.value)}
                    >
                      <option>Compartido</option>
                      <option>Por sede</option>
                    </select>
                  </label>
                  {form.scope === "Por sede" && (
                    <label>
                      Sede
                      <select
                        aria-label="Sede del plan"
                        value={form.branch}
                        onChange={(e) => patch("branch", e.target.value)}
                      >
                        <option value="">Seleccionar</option>
                        {branches.map((s) => (
                          <option key={s}>{s}</option>
                        ))}
                      </select>
                    </label>
                  )}
                </div>
              )}
              <div className="plan-calendar-header">
                <div>
                  <CalendarRange size={18} />
                  <strong>Calendario de asignaciones</strong>
                </div>
                <span>Distribución del ejercicio {shown.year}</span>
              </div>
              <div className="plan-months" role="list" aria-label="Importe planificado por mes">
                {monthTotals.map((m) => (
                  <div key={m.name} role="listitem" className="plan-month">
                    <span>{m.name}</span>
                    <div className="plan-month-meter">
                      <span
                        style={{
                          height: `${Math.max(2, Math.round((100 * m.amount) / maxAmount))}%`,
                        }}
                      />
                    </div>
                    <strong title={format.format(m.amount)}>
                      {m.amount ? compact(m.amount) : "—"}
                    </strong>
                  </div>
                ))}
              </div>
              <div className="plan-total">
                <div>
                  <span>IMPORTE PLANIFICADO · PEN</span>
                  <strong>{format.format(total)}</strong>
                </div>
                <span>{shown.lines.length} asignaciones</span>
              </div>
              <div className="plan-lines-heading">
                <h4>Asignaciones por concepto</h4>
                {form && (
                  <button onClick={addLine}>
                    <Plus size={15} /> Añadir línea
                  </button>
                )}
              </div>
              <div className="plan-table-wrap">
                <table className="plan-table">
                  <thead>
                    <tr>
                      <th>Concepto</th>
                      <th>Mes</th>
                      <th>Importe PEN</th>
                      {form && <th>Quitar</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {shown.lines.length ? (
                      shown.lines.map((line) => (
                        <tr key={line.id}>
                          <td>
                            {form ? (
                              <select
                                aria-label={"Categoría " + line.id}
                                value={line.category}
                                onChange={(e) => patchLine(line.id, "category", e.target.value)}
                              >
                                {categories.map((c) => (
                                  <option key={c}>{c}</option>
                                ))}
                              </select>
                            ) : (
                              line.category
                            )}
                          </td>
                          <td>
                            {form ? (
                              <select
                                aria-label={"Mes " + line.id}
                                value={line.month}
                                onChange={(e) =>
                                  patchLine(line.id, "month", Number(e.target.value))
                                }
                              >
                                {months.map((m, i) => (
                                  <option value={i + 1} key={m}>
                                    {m}
                                  </option>
                                ))}
                              </select>
                            ) : (
                              months[line.month - 1]
                            )}
                          </td>
                          <td>
                            {form ? (
                              <input
                                aria-label={"Importe " + line.id}
                                type="number"
                                min="0"
                                step="0.01"
                                value={line.amount}
                                onChange={(e) =>
                                  patchLine(line.id, "amount", Number(e.target.value))
                                }
                              />
                            ) : (
                              format.format(line.amount)
                            )}
                          </td>
                          {form && (
                            <td>
                              <button
                                className="plan-delete-line"
                                aria-label={"Eliminar línea " + line.id}
                                onClick={() =>
                                  setForm((f) =>
                                    f
                                      ? { ...f, lines: f.lines.filter((l) => l.id !== line.id) }
                                      : f,
                                  )
                                }
                              >
                                <Trash2 size={15} />
                              </button>
                            </td>
                          )}
                        </tr>
                      ))
                    ) : (
                      <tr>
                        <td colSpan={form ? 4 : 3} className="plan-no-lines">
                          Sin asignaciones. Añade una línea por categoría y mes.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
              {error && (
                <div className="plan-error" role="alert">
                  {error}
                </div>
              )}
              <div className="plan-actions">
                {form ? (
                  <>
                    <button
                      className="plan-muted"
                      onClick={() => {
                        setForm(null);
                        setError("");
                      }}
                    >
                      Cancelar cambios
                    </button>
                    <button className="plan-new" onClick={save}>
                      <Save size={16} /> Guardar presupuesto
                    </button>
                  </>
                ) : (
                  <>
                    <button
                      className="plan-muted"
                      onClick={() => {
                        if (active) send({ type: "deleteBudget", id: active.id });
                        setSelected(null);
                      }}
                    >
                      Eliminar borrador
                    </button>
                    <button className="plan-new" onClick={edit}>
                      Modificar asignaciones
                    </button>
                  </>
                )}
              </div>
            </>
          ) : (
            <div className="plan-start">
              <CalendarRange size={44} strokeWidth={1.2} />
              <span className="eyebrow">GESTIÓN PRESUPUESTARIA</span>
              <h4>Planifica antes de gastar.</h4>
              <p>
                Empieza con un presupuesto nuevo o selecciona uno de la bandeja. Los totales se
                calculan desde las asignaciones registradas en este espacio.
              </p>
              <button className="plan-new" onClick={create}>
                <Plus size={17} /> Crear presupuesto
              </button>
            </div>
          )}
        </div>
        <aside className="plan-guidance">
          <span className="eyebrow">CÓMO FUNCIONA</span>
          <div className="plan-step">
            <span>01</span>
            <strong>Preparar</strong>
            <p>Define el ejercicio y el alcance de la planificación.</p>
          </div>
          <div className="plan-step">
            <span>02</span>
            <strong>Distribuir</strong>
            <p>Asigna valores por mes y categoría; revisa el patrón anual.</p>
          </div>
          <div className="plan-step">
            <span>03</span>
            <strong>Revisar</strong>
            <p>El envío y la aprobación se habilitarán únicamente en el flujo autenticado.</p>
          </div>
          <div className="plan-guidance-note">
            <CircleHelp size={17} />
            <p>
              El gasto contabilizado es un resultado; el presupuesto es una autorización/versiones
              distintas. No se compara sin versión aprobada y efectiva.
            </p>
          </div>
          {notice && (
            <div className="plan-success" role="status">
              <Check size={16} />
              {notice}
            </div>
          )}
        </aside>
      </div>
    </section>
  );
}
