import { useState } from "react";
import { ArrowRight, Database, RefreshCcw, ShieldCheck } from "lucide-react";
import "./live-workspace.css";
import type { DraftVersion } from "./infrastructure/data/conditional-draft";
import {
  verifyDelegatedDataverseIdentity,
  type VerifiedDelegatedSession,
} from "./infrastructure/data/identity-check";
import {
  DelegatedLiveRepository,
  type LiveCollection,
  type LivePage,
} from "./infrastructure/data/live-read-repository";

const sections: ReadonlyArray<{ key: LiveCollection; name: string }> = [
  { key: "expenses", name: "Movimientos" },
  { key: "budgets", name: "Presupuestos" },
  { key: "allocations", name: "Asignaciones" },
  { key: "activity", name: "Actividad" },
  { key: "categories", name: "Categorías" },
  { key: "branches", name: "Sedes" },
  { key: "accounts", name: "Cuentas" },
];
const empty: Record<LiveCollection, LivePage> = {
  expenses: { rows: [], next: null },
  budgets: { rows: [], next: null },
  allocations: { rows: [], next: null },
  activity: { rows: [], next: null },
  categories: { rows: [], next: null },
  branches: { rows: [], next: null },
  accounts: { rows: [], next: null },
};
const labels: Record<string, string> = {
  fmg_name: "Nombre",
  fmg_status: "Estado (Dataverse)",
  fmg_amount: "Importe PEN",
  fmg_transactiondate: "Fecha",
  fmg_scope: "Ámbito (Dataverse)",
  fmg_flowclass: "Clase (Dataverse)",
  fmg_year: "Año",
  fmg_version: "Versión",
  fmg_month: "Mes",
  fmg_eventtime: "Fecha del evento",
  fmg_summary: "Descripción",
  fmg_code: "Código",
  fmg_isactive: "Activa (Dataverse)",
};
const fields: Record<LiveCollection, ReadonlyArray<string>> = {
  expenses: ["fmg_name", "fmg_transactiondate", "fmg_amount", "fmg_status", "fmg_scope"],
  budgets: ["fmg_name", "fmg_year", "fmg_version", "fmg_status", "fmg_scope"],
  allocations: ["fmg_name", "fmg_month", "fmg_amount"],
  activity: ["fmg_eventtime", "fmg_name", "fmg_summary"],
  categories: ["fmg_name", "fmg_code", "fmg_isactive"],
  branches: ["fmg_name", "fmg_isactive"],
  accounts: ["fmg_name", "fmg_isactive", "_fmg_branch_value"],
};

function value(field: string, data: unknown, collection: LiveCollection): string {
  if (data === null || data === undefined) return "Sin asignar";
  if (typeof data === "boolean") return data ? "Sí" : "No";
  if (field === "fmg_status" && typeof data === "number") {
    const statuses: Record<string, Record<number, string>> = {
      expenses: { 797280000: "Borrador", 797280001: "Contabilizado", 797280002: "Anulado" },
      budgets: { 797280400: "Borrador", 797280401: "Presentado", 797280402: "Aprobado" },
    };
    return statuses[collection]?.[data] ?? String(data);
  }
  if (field === "fmg_scope" && typeof data === "number")
    return (
      ({ 797280000: "Por sede", 797280001: "Compartido" } as Record<number, string>)[data] ??
      String(data)
    );
  if (field === "fmg_amount" && typeof data === "number" && Number.isFinite(data))
    return new Intl.NumberFormat("es-PE", { style: "currency", currency: "PEN" }).format(data);
  if ((field === "fmg_eventtime" || field === "fmg_transactiondate") && typeof data === "string") {
    const parsed = Date.parse(data);
    if (Number.isFinite(parsed))
      return new Intl.DateTimeFormat("es-PE", { dateStyle: "medium" }).format(new Date(parsed));
  }
  if (typeof data === "string") return data.length > 200 ? data.slice(0, 200) + "…" : data;
  if (typeof data === "number") return Number.isFinite(data) ? String(data) : "Sin dato";
  return "Sin dato";
}

export default function LiveWorkspace() {
  const [session, setSession] = useState<VerifiedDelegatedSession | null>(null);
  const [repo, setRepo] = useState<DelegatedLiveRepository | null>(null);
  const [tab, setTab] = useState<LiveCollection>("expenses");
  const [pages, setPages] = useState<Record<LiveCollection, LivePage>>(empty);
  const [issues, setIssues] = useState<Partial<Record<LiveCollection, string>>>({});
  const [busy, setBusy] = useState(false);
  const [newName, setNewName] = useState("");
  const [selected, setSelected] = useState<DraftVersion | null>(null);
  const [draftId, setDraftId] = useState("");
  const [amount, setAmount] = useState("");
  const [date, setDate] = useState("");
  const [description, setDescription] = useState("");
  const [reference, setReference] = useState("");
  const [message, setMessage] = useState(
    "Inicia sesión para consultar las operaciones a las que tienes acceso.",
  );

  async function fetchAll(source: DelegatedLiveRepository) {
    const results = await Promise.allSettled(sections.map(({ key }) => source.readPage(key)));
    const nextPages = { ...empty };
    const nextIssues: Partial<Record<LiveCollection, string>> = {};
    results.forEach((result, index) => {
      const key = sections[index].key;
      if (result.status === "fulfilled") nextPages[key] = result.value;
      else
        nextIssues[key] =
          result.reason instanceof Error ? result.reason.message : "Acceso denegado.";
    });
    setPages(nextPages);
    setIssues(nextIssues);
  }

  async function signIn() {
    if (busy) return;
    setBusy(true);
    setMessage("Iniciando sesión y verificando WhoAmI...");
    try {
      const identity = await verifyDelegatedDataverseIdentity();
      const connected = new DelegatedLiveRepository(identity);
      await fetchAll(connected);
      setSession(identity);
      setRepo(connected);
      setMessage(
        "Datos consultados directamente de Dataverse. Los resultados están paginados y sujetos al permiso real del usuario.",
      );
    } catch (error) {
      setSession(null);
      setRepo(null);
      setMessage(error instanceof Error ? error.message : "No fue posible validar la sesión.");
    } finally {
      setBusy(false);
    }
  }

  async function refresh() {
    if (!repo || busy) return;
    setBusy(true);
    try {
      await fetchAll(repo);
    } finally {
      setBusy(false);
    }
  }

  async function nextPage() {
    if (!repo || !pages[tab].next || busy) return;
    setBusy(true);
    try {
      const additional = await repo.readPage(tab, pages[tab].next!);
      setPages((prev) => ({
        ...prev,
        [tab]: { rows: [...prev[tab].rows, ...additional.rows], next: additional.next },
      }));
      setIssues((prev) => ({ ...prev, [tab]: undefined }));
    } catch (error) {
      setIssues((prev) => ({
        ...prev,
        [tab]: error instanceof Error ? error.message : "No se pudo leer la página.",
      }));
    } finally {
      setBusy(false);
    }
  }

  async function openDraft(id: string) {
    if (!repo || busy) return;
    setBusy(true);
    setMessage("Comprobando borrador y ETag en Dataverse...");
    try {
      const value = await repo.loadDraft(id);
      setSelected(value.version);
      setDraftId(id);
      setAmount(
        value.record.fmg_amount === null || value.record.fmg_amount === undefined
          ? ""
          : String(value.record.fmg_amount),
      );
      setDate(
        typeof value.record.fmg_transactiondate === "string"
          ? value.record.fmg_transactiondate.slice(0, 10)
          : "",
      );
      setDescription(
        typeof value.record.fmg_description === "string" ? value.record.fmg_description : "",
      );
      setReference(
        typeof value.record.fmg_documentreference === "string"
          ? value.record.fmg_documentreference
          : "",
      );
      setMessage("Borrador cargado y ETag comprobado.");
    } catch (err) {
      setSelected(null);
      setMessage(err instanceof Error ? err.message : "No se pudo abrir el borrador.");
    } finally {
      setBusy(false);
    }
  }

  async function createDraft() {
    if (!repo || busy) return;
    setBusy(true);
    setMessage("Creando únicamente un borrador Draft en Dataverse...");
    try {
      const id = await repo.createDraft(newName);
      setDraftId(id);
      setNewName("");
      setSelected(null);
      setMessage("Borrador creado. Se está comprobando su versión...");
      await fetchAll(repo);
      const value = await repo.loadDraft(id);
      setSelected(value.version);
      setAmount("");
      setDate("");
      setDescription("");
      setReference("");
      setMessage("Borrador real creado y recuperado. No es un gasto contabilizado.");
    } catch (err) {
      setMessage(
        (err instanceof Error ? err.message : "No se confirmó el Draft.") +
          " No repitas la creación si existe una posibilidad de que Dataverse la haya guardado.",
      );
    } finally {
      setBusy(false);
    }
  }

  async function saveDraft() {
    if (!repo || !selected || busy) return;
    if (
      amount.trim() &&
      (!/^(?:0|[1-9]\\d*)(?:\\.\\d{1,2})?$/.test(amount.trim()) || Number(amount) <= 0)
    ) {
      setMessage("El importe debe ser positivo, con un máximo de dos decimales.");
      return;
    }
    if (
      date &&
      (!/^\\d{4}-\\d{2}-\\d{2}$/.test(date) || Number.isNaN(Date.parse(date + "T12:00:00Z")))
    ) {
      setMessage("Introduce una fecha válida.");
      return;
    }
    setBusy(true);
    try {
      await repo.saveDraft(selected, {
        fmg_amount: amount.trim() ? Number(amount) : null,
        fmg_transactiondate: date ? date + "T12:00:00.000Z" : null,
        fmg_description: description,
        fmg_documentreference: reference,
      });
      setMessage("Guardado condicional confirmado. Releyendo la versión...");
      const reloaded = await repo.loadDraft(selected.id);
      setSelected(reloaded.version);
      await fetchAll(repo);
      setMessage("Borrador guardado en Dataverse con control de concurrencia.");
    } catch (err) {
      setMessage(
        err instanceof Error
          ? err.message
          : "No se confirmó el guardado. No reintentes sin releer.",
      );
    } finally {
      setBusy(false);
    }
  }

  async function deleteDraft() {
    if (
      !repo ||
      !selected ||
      busy ||
      !window.confirm("¿Eliminar este borrador? Esta operación no se puede deshacer.")
    )
      return;
    setBusy(true);
    try {
      await repo.deleteDraft(selected);
      setSelected(null);
      setDraftId("");
      await fetchAll(repo);
      setMessage("Eliminación del borrador confirmada por Dataverse.");
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "No se confirmó la eliminación.");
    } finally {
      setBusy(false);
    }
  }

  async function signOut() {
    const previous = session;
    setSession(null);
    setRepo(null);
    setPages(empty);
    setIssues({});
    setSelected(null);
    setDraftId("");
    setMessage("Sesión cerrada. Los registros ya no se muestran.");
    if (previous) {
      try {
        await previous.close();
      } catch {
        setMessage(
          "Sesión local cerrada; verifica también la sesión de Microsoft en el navegador.",
        );
      }
    }
  }

  return (
    <main className="live-shell">
      <header className="live-header">
        <div className="live-header-copy">
          <span className="live-kicker">
            <Database size={15} /> DATAVERSE
          </span>
          <h1>Tu centro financiero</h1>
          <p>Consulta registros, trabaja con borradores y revisa información de tu organización.</p>
        </div>
        <div className="live-header-actions">
          {!session && (
            <button disabled={busy} onClick={() => void signIn()}>
              Iniciar sesión Entra
            </button>
          )}
          {session && (
            <button disabled={busy} onClick={() => void refresh()}>
              Actualizar registros
            </button>
          )}
          {session && (
            <button disabled={busy} onClick={() => void signOut()}>
              Cerrar sesión
            </button>
          )}
          <a href="/" className="live-back-link">
            Panel analítico <ArrowRight size={15} />
          </a>
        </div>
      </header>
      <div className="live-system-status" role="status" aria-live="polite">
        <ShieldCheck size={17} />
        <p>{message}</p>
      </div>
      {session && (
        <>
          <section className="live-overview" aria-label="Registros cargados">
            <div>
              <span>Movimientos</span>
              <strong>{pages.expenses.rows.length}</strong>
              <small>Registros cargados</small>
            </div>
            <div>
              <span>Presupuestos</span>
              <strong>{pages.budgets.rows.length}</strong>
              <small>Disponibles para tu usuario</small>
            </div>
            <div>
              <span>Asignaciones</span>
              <strong>{pages.allocations.rows.length}</strong>
              <small>Registros consultados</small>
            </div>
            <div>
              <span>Eventos</span>
              <strong>{pages.activity.rows.length}</strong>
              <small>Actividad consultada</small>
            </div>
          </section>
          <nav aria-label="Secciones Dataverse" className="live-tabs">
            {sections.map(({ key, name }) => (
              <button
                key={key}
                onClick={() => setTab(key)}
                aria-current={tab === key ? "page" : undefined}
              >
                {name}
              </button>
            ))}
          </nav>
          {tab === "expenses" && (
            <section aria-label="Borradores de gastos" className="live-draft-editor">
              <span className="live-kicker">GESTIÓN DE GASTOS</span>
              <h2>Preparar movimiento</h2>
              <p>
                Completa tu registro antes de enviarlo a la siguiente etapa. Esta sección solo
                administra borradores.
              </p>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  void createDraft();
                }}
                className="live-draft-create"
              >
                <label>
                  Nombre del nuevo Draft{" "}
                  <input
                    value={newName}
                    maxLength={100}
                    onChange={(e) => setNewName(e.target.value)}
                  />
                </label>
                <button type="submit" disabled={busy || newName.trim().length < 4}>
                  Crear Draft
                </button>
              </form>
              {draftId && (
                <p>
                  Identificador del Draft: <code>{draftId}</code>
                </p>
              )}
              {selected && (
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    void saveDraft();
                  }}
                  className="live-draft-form"
                >
                  <label>
                    Importe PEN{" "}
                    <input
                      inputMode="decimal"
                      value={amount}
                      onChange={(e) => setAmount(e.target.value)}
                    />
                  </label>
                  <label>
                    Fecha{" "}
                    <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
                  </label>
                  <label>
                    Descripción{" "}
                    <input value={description} onChange={(e) => setDescription(e.target.value)} />
                  </label>
                  <label>
                    Referencia documental{" "}
                    <input value={reference} onChange={(e) => setReference(e.target.value)} />
                  </label>
                  <div className="live-draft-actions">
                    <button type="submit" disabled={busy}>
                      Guardar con If-Match
                    </button>
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => void openDraft(selected.id)}
                    >
                      Releer borrador
                    </button>
                    <button type="button" disabled={busy} onClick={() => void deleteDraft()}>
                      Eliminar Draft
                    </button>
                  </div>
                </form>
              )}
            </section>
          )}
          <div className="live-section-heading">
            <div>
              <span className="live-kicker">REGISTROS</span>
              <h2>{sections.find(({ key }) => key === tab)?.name}</h2>
            </div>
            <button disabled={busy} onClick={() => void refresh()}>
              <RefreshCcw size={15} /> Actualizar
            </button>
          </div>
          <p>
            Filas recuperadas: {pages[tab].rows.length}. No se asume que sean todos los registros.
          </p>
          {issues[tab] && <p role="alert">Sin acceso o fallo de consulta: {issues[tab]}</p>}
          {!issues[tab] && (
            <div
              tabIndex={0}
              role="region"
              aria-label="Datos reales desplazables"
              className="live-table-scroll"
            >
              <table className="live-table">
                <thead>
                  <tr>
                    {fields[tab].map((field) => (
                      <th key={field} scope="col">
                        {labels[field]}
                      </th>
                    ))}
                    {tab === "expenses" && <th scope="col">Acción</th>}
                  </tr>
                </thead>
                <tbody>
                  {pages[tab].rows.map((row, i) => (
                    <tr
                      key={String(
                        row.fmg_financialtransactionid ??
                          row.fmg_budgetid ??
                          row.fmg_budgetallocationid ??
                          row.fmg_activityeventid ??
                          row.fmg_categoryid ??
                          row.fmg_branchid ??
                          row.fmg_cashaccountid ??
                          i,
                      )}
                    >
                      {fields[tab].map((field) => (
                        <td key={field}>{value(field, row[field], tab)}</td>
                      ))}
                      {tab === "expenses" && (
                        <td>
                          {row.fmg_status === 797280000 &&
                            typeof row.fmg_financialtransactionid === "string" && (
                              <button
                                disabled={busy}
                                onClick={() =>
                                  void openDraft(row.fmg_financialtransactionid as string)
                                }
                              >
                                Abrir Draft
                              </button>
                            )}
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {!issues[tab] && !pages[tab].rows.length && (
            <p>Sin registros visibles para este usuario.</p>
          )}
          {pages[tab].next && (
            <button disabled={busy} onClick={() => void nextPage()}>
              Cargar otros 25 registros
            </button>
          )}
          <p>
            El historial de Actividad es un registro empresarial restringido; no sustituye la
            auditoría nativa de Dataverse.
          </p>
        </>
      )}
    </main>
  );
}
