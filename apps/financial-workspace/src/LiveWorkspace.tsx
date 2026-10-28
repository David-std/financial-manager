import { useEffect, useState } from "react";
import CashReadOnlyPanel from "./CashReadOnlyPanel";
import { ArrowRight, Database, RefreshCcw, ShieldCheck } from "lucide-react";
import "./live-workspace.css";
import {
  draftPenValue,
  formatPenOnBlur,
  missingPostingFields,
  validDraftPenAmount,
  validDraftDate,
} from "./infrastructure/data/expense-draft-rules";
import type { DataverseDraftResult, DraftVersion } from "./infrastructure/data/conditional-draft";
import {
  hydrateDraftFields,
  loadCompleteReferenceCatalog,
} from "./infrastructure/data/draft-editor-state";
import {
  verifyDelegatedDataverseIdentity,
  prepareDelegatedDataverseIdentity,
  explainDelegatedAuthError,
  type VerifiedDelegatedSession,
} from "./infrastructure/data/identity-check";
import {
  DelegatedLiveRepository,
  type StandardLiveCollection,
  type LivePage,
} from "./infrastructure/data/live-read-repository";

const paymentCommandsEnabled = import.meta.env.VITE_FM_ENABLE_PAYMENT_COMMANDS === "true";
const sections: ReadonlyArray<{ key: StandardLiveCollection; name: string }> = [
  { key: "expenses", name: "Movimientos" },
  { key: "budgets", name: "Presupuestos" },
  { key: "allocations", name: "Asignaciones" },
  { key: "activity", name: "Actividad" },
  { key: "categories", name: "Categorías" },
  { key: "branches", name: "Sedes" },
  { key: "accounts", name: "Cuentas" },
];
const empty: Record<StandardLiveCollection, LivePage> = {
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
const fields: Record<StandardLiveCollection, ReadonlyArray<string>> = {
  expenses: ["fmg_name", "fmg_transactiondate", "fmg_amount", "fmg_status", "fmg_scope"],
  budgets: ["fmg_name", "fmg_year", "fmg_version", "fmg_status", "fmg_scope"],
  allocations: ["fmg_name", "fmg_month", "fmg_amount"],
  activity: ["fmg_eventtime", "fmg_name", "fmg_summary"],
  categories: ["fmg_name", "fmg_code", "fmg_isactive"],
  branches: ["fmg_name", "fmg_isactive"],
  accounts: ["fmg_name", "fmg_isactive", "_fmg_branch_value"],
};

function value(field: string, data: unknown, collection: StandardLiveCollection): string {
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
  const [tab, setTab] = useState<StandardLiveCollection>("expenses");
  const [showCash, setShowCash] = useState(false);
  const [pages, setPages] = useState<Record<StandardLiveCollection, LivePage>>(empty);
  const [issues, setIssues] = useState<Partial<Record<StandardLiveCollection, string>>>({});
  const [busy, setBusy] = useState(false);
  const [authReady, setAuthReady] = useState(false);
  useEffect(() => {
    let active = true;
    prepareDelegatedDataverseIdentity().then(
      () => {
        if (active) setAuthReady(true);
      },
      (error: unknown) => {
        if (active)
          setMessage(
            error instanceof Error ? error.message : "No se pudo preparar Microsoft Entra.",
          );
      },
    );
    return () => {
      active = false;
    };
  }, []);
  const [newName, setNewName] = useState("");
  const [selected, setSelected] = useState<DraftVersion | null>(null);
  const [draftId, setDraftId] = useState("");
  const [amount, setAmount] = useState("");
  const [date, setDate] = useState("");
  const [description, setDescription] = useState("");
  const [reference, setReference] = useState("");
  const [scopeCode, setScopeCode] = useState("797280000");
  const [flowCode, setFlowCode] = useState("797280000");
  const [categoryId, setCategoryId] = useState("");
  const [branchId, setBranchId] = useState("");
  const [accountId, setAccountId] = useState("");
  const [message, setMessage] = useState(
    "Inicia sesión para consultar las operaciones a las que tienes acceso.",
  );

  const missingDraftFields = selected
    ? [
        ...(!amount.trim() || !validDraftPenAmount(amount) ? ["importe positivo en PEN"] : []),
        ...(!date || !validDraftDate(date) ? ["fecha válida"] : []),
        ...(!categoryId ? ["categoría activa"] : []),
        ...(scopeCode === "797280000" && !branchId ? ["sede"] : []),
        ...(!accountId ? ["cuenta autorizada"] : []),
      ]
    : [];

  async function fetchAll(source: DelegatedLiveRepository) {
    const results = await Promise.allSettled(
      sections.map(({ key }) => loadCompleteReferenceCatalog(key, source)),
    );
    const nextPages = { ...empty };
    const nextIssues: Partial<Record<StandardLiveCollection, string>> = {};
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
    if (busy || !authReady) return;
    setBusy(true);
    setMessage("Abriendo Microsoft Entra y verificando WhoAmI...");
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
      setMessage(explainDelegatedAuthError(error));
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

  async function command(kind: "post" | "void", row: Record<string, unknown>) {
    if (
      !repo ||
      busy ||
      !paymentCommandsEnabled ||
      typeof row.fmg_financialtransactionid !== "string"
    )
      return;
    let reason: string | undefined;
    if (kind === "void") {
      const typed = window.prompt("Motivo de la anulación (mínimo 10 caracteres):");
      if (typed === null) return;
      reason = typed;
    }
    if (
      !window.confirm(
        kind === "post"
          ? "¿Contabilizar este gasto? No podrás modificar sus datos financieros después."
          : "¿Anular este gasto contabilizado? El registro se conservará para auditoría.",
      )
    )
      return;
    setBusy(true);
    try {
      await repo.executePaymentCommand(
        kind,
        row.fmg_financialtransactionid,
        row.versionnumber,
        reason,
      );
      await fetchAll(repo);
      setSelected(null);
      setMessage(
        kind === "post"
          ? "Gasto contabilizado y releído en Dataverse."
          : "Gasto anulado con motivo y releído en Dataverse.",
      );
    } catch (error) {
      setMessage(
        (error instanceof Error ? error.message : "Operación no confirmada.") +
          " Relee el gasto antes de repetir la acción.",
      );
    } finally {
      setBusy(false);
    }
  }

  function applyHydratedDraft(record: DataverseDraftResult) {
    const fields = hydrateDraftFields(record);
    setAmount(fields.amount);
    setDate(fields.date);
    setDescription(fields.description);
    setReference(fields.reference);
    setScopeCode(fields.scopeCode);
    setFlowCode(fields.flowCode);
    setCategoryId(fields.categoryId);
    setBranchId(fields.branchId);
    setAccountId(fields.accountId);
  }

  async function openDraft(id: string) {
    if (!repo || busy) return;
    setBusy(true);
    setMessage("Comprobando borrador y ETag en Dataverse...");
    try {
      const value = await repo.loadDraft(id);
      setSelected(value.version);
      setDraftId(id);
      applyHydratedDraft(value.record);
      const missing = missingPostingFields(value.record);
      setMessage(
        missing.length
          ? "Borrador recuperado de Dataverse; aún faltan campos persistidos para contabilizar: " +
              missing.join(", ") +
              ". Completa y guarda los datos."
          : "Borrador recuperado de Dataverse con sus campos y ETag.",
      );
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
      setScopeCode("797280000");
      setFlowCode("797280000");
      setCategoryId("");
      setBranchId("");
      setAccountId("");
      setMessage(
        "Borrador creado. Ahora completa el importe, fecha, categoría, sede y cuenta; pulsa Guardar con If-Match para persistirlos.",
      );
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
    if (!validDraftPenAmount(amount)) {
      setMessage("El importe debe ser positivo, con un máximo de dos decimales.");
      return;
    }
    if (!validDraftDate(date)) {
      setMessage("Introduce una fecha válida.");
      return;
    }
    const category = pages.categories.rows.find((row) => row.fmg_categoryid === categoryId);
    const branch = pages.branches.rows.find((row) => row.fmg_branchid === branchId);
    const account = pages.accounts.rows.find((row) => row.fmg_cashaccountid === accountId);
    if (
      (categoryId &&
        (!category || category.fmg_isactive !== true || category.fmg_isgroup === true)) ||
      (branchId && (!branch || branch.fmg_isactive !== true)) ||
      (accountId && (!account || account.fmg_isactive !== true))
    ) {
      setMessage(
        "Selecciona únicamente categorías, sedes y cuentas activas que tengas autorizadas en Dataverse.",
      );
      return;
    }
    if (
      scopeCode === "797280000" &&
      accountId &&
      (!branchId ||
        String(account?._fmg_branch_value ?? "").toLowerCase() !== branchId.toLowerCase())
    ) {
      setMessage("Para gastos por sede, la cuenta debe pertenecer a la sede seleccionada.");
      return;
    }
    if (scopeCode === "797280001" && (branchId || (accountId && account?._fmg_branch_value))) {
      setMessage("Para gastos compartidos, selecciona únicamente una cuenta corporativa sin sede.");
      return;
    }
    setBusy(true);
    try {
      const penId = await repo.resolvePenCurrency();
      await repo.saveDraft(selected, {
        fmg_amount: draftPenValue(amount),
        fmg_transactiondate: date ? date + "T12:00:00.000Z" : null,
        fmg_description: description,
        fmg_documentreference: reference,
        fmg_scope: Number(scopeCode),
        fmg_flowclass: Number(flowCode),
        "fmg_category@odata.bind": categoryId ? `/fmg_categories(${categoryId})` : null,
        "fmg_branch@odata.bind":
          scopeCode === "797280000" && branchId ? `/fmg_branchs(${branchId})` : null,
        "fmg_cashaccount@odata.bind": accountId ? `/fmg_cashaccounts(${accountId})` : null,
        "transactioncurrencyid@odata.bind": `/transactioncurrencies(${penId})`,
      });
      setMessage("Guardado condicional confirmado. Releyendo la versión...");
      const reloaded = await repo.loadDraft(selected.id);
      setSelected(reloaded.version);
      applyHydratedDraft(reloaded.record);
      await fetchAll(repo);
      const missing = missingPostingFields(reloaded.record);
      setMessage(
        missing.length
          ? "Borrador guardado en Dataverse. Para contabilizar todavía faltan: " +
              missing.join(", ") +
              "."
          : "Borrador guardado y releído de Dataverse; los datos requeridos están presentes.",
      );
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
    setShowCash(false);
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
            <button disabled={busy || !authReady} onClick={() => void signIn()}>
              {authReady ? "Iniciar sesión Entra" : "Preparando acceso seguro…"}
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
          <section className="live-cash-access">
            <button
              type="button"
              aria-expanded={showCash}
              onClick={() => setShowCash((current) => !current)}
            >
              {showCash ? "Ocultar consulta de caja" : "Consultar caja y conciliaciones"}
            </button>
            {showCash && repo && <CashReadOnlyPanel repo={repo} />}
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
                Primero crea el borrador con un nombre. Después completa sus datos y pulsa Guardar
                con If-Match. Crear Draft por sí solo no guarda el importe ni los catálogos.
              </p>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  void createDraft();
                }}
                className="live-draft-create"
              >
                <label>
                  Paso 1 · Nombre del nuevo borrador{" "}
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
                    Importe PEN <span aria-label="Obligatorio para contabilizar">*</span>{" "}
                    <input
                      inputMode="decimal"
                      value={amount}
                      placeholder="0.00"
                      onChange={(e) => setAmount(e.target.value)}
                      onBlur={() => setAmount((current) => formatPenOnBlur(current))}
                    />
                  </label>
                  <label>
                    Fecha <span aria-label="Obligatorio para contabilizar">*</span>{" "}
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
                  <label>
                    Clase de gasto
                    <select value={flowCode} onChange={(e) => setFlowCode(e.target.value)}>
                      <option value="797280000">Operativo</option>
                      <option value="797280001">Capital</option>
                      <option value="797280002">Impuestos</option>
                      <option value="797280003">Otros</option>
                    </select>
                  </label>
                  <label>
                    Ámbito
                    <select
                      value={scopeCode}
                      onChange={(e) => {
                        setScopeCode(e.target.value);
                        setBranchId("");
                        setAccountId("");
                      }}
                    >
                      <option value="797280000">Por sede</option>
                      <option value="797280001">Compartido</option>
                    </select>
                  </label>
                  <label>
                    Categoría *
                    <select
                      value={categoryId}
                      onChange={(e) => setCategoryId(e.target.value)}
                      disabled={Boolean(issues.categories)}
                    >
                      <option value="">Sin categoría</option>
                      {categoryId &&
                        !pages.categories.rows.some((row) => row.fmg_categoryid === categoryId) && (
                          <option value={categoryId}>
                            Categoría anterior no disponible en tus catálogos
                          </option>
                        )}
                      {pages.categories.rows
                        .filter((row) => row.fmg_isactive === true && row.fmg_isgroup !== true)
                        .map((row) => (
                          <option
                            key={String(row.fmg_categoryid)}
                            value={String(row.fmg_categoryid)}
                          >
                            {String(row.fmg_name)}
                          </option>
                        ))}
                    </select>
                  </label>
                  {scopeCode === "797280000" && (
                    <label>
                      Sede *
                      <select
                        value={branchId}
                        onChange={(e) => {
                          setBranchId(e.target.value);
                          setAccountId("");
                        }}
                        disabled={Boolean(issues.branches)}
                      >
                        <option value="">Seleccionar sede</option>
                        {pages.branches.rows
                          .filter((row) => row.fmg_isactive === true)
                          .map((row) => (
                            <option key={String(row.fmg_branchid)} value={String(row.fmg_branchid)}>
                              {String(row.fmg_name)}
                            </option>
                          ))}
                      </select>
                    </label>
                  )}
                  <label>
                    Cuenta autorizada *
                    <select
                      value={accountId}
                      onChange={(e) => setAccountId(e.target.value)}
                      disabled={Boolean(issues.accounts)}
                    >
                      <option value="">Sin cuenta</option>
                      {accountId &&
                        !pages.accounts.rows.some((row) => row.fmg_cashaccountid === accountId) && (
                          <option value={accountId}>
                            Cuenta anterior no disponible en tus catálogos
                          </option>
                        )}
                      {pages.accounts.rows
                        .filter(
                          (row) =>
                            row.fmg_isactive === true &&
                            (scopeCode === "797280000"
                              ? Boolean(branchId) &&
                                String(row._fmg_branch_value ?? "").toLowerCase() ===
                                  branchId.toLowerCase()
                              : !row._fmg_branch_value),
                        )
                        .map((row) => (
                          <option
                            key={String(row.fmg_cashaccountid)}
                            value={String(row.fmg_cashaccountid)}
                          >
                            {String(row.fmg_name)}
                          </option>
                        ))}
                    </select>
                  </label>
                  <p className="live-field-help">
                    * Obligatorio para contabilizar; puedes guardar un borrador incompleto.
                  </p>
                  <p className="live-field-help">
                    Paso 2 · Elige importe, fecha, categoría, sede y cuenta. La moneda PEN se asocia
                    al guardar. Cambiar sede o ámbito limpia la cuenta anterior.
                  </p>
                  <p className="live-field-help" role="status" aria-live="polite">
                    {missingDraftFields.length
                      ? "Pendiente para contabilizar: " +
                        missingDraftFields.join(", ") +
                        ". Guarda el borrador y completa estos datos antes de publicar."
                      : "Campos de formulario completos. Guarda y relee el borrador; la moneda PEN y los demás controles se verifican en Dataverse."}
                  </p>
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
                          {paymentCommandsEnabled && row.fmg_status === 797280000 && (
                            <button
                              disabled={busy || missingPostingFields(row).length > 0}
                              title={
                                missingPostingFields(row).length
                                  ? "Completar antes de contabilizar: " +
                                    missingPostingFields(row).join(", ")
                                  : "Contabilizar borrador válido"
                              }
                              onClick={() => void command("post", row)}
                            >
                              Contabilizar
                            </button>
                          )}
                          {paymentCommandsEnabled &&
                            row.fmg_status === 797280000 &&
                            missingPostingFields(row).length > 0 && (
                              <small className="live-post-hint">
                                Faltan: {missingPostingFields(row).join(", ")}. Abre el borrador
                                para completarlo.
                              </small>
                            )}
                          {paymentCommandsEnabled && row.fmg_status === 797280001 && (
                            <button disabled={busy} onClick={() => void command("void", row)}>
                              Anular
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
