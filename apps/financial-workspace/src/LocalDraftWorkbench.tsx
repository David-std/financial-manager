import { useState } from "react";
import { Check, Clock3, FilePenLine, PencilLine, Plus, Trash2, X } from "lucide-react";
import { cleanAmount, useWorkspace } from "./WorkspaceStore";
import type { DraftFields } from "./WorkspaceStore";
import "./local-draft-workbench.css";
const blank = (): DraftFields => ({
  amount: "",
  date: "",
  category: "",
  scope: "Por sede",
  branch: "",
  account: "",
  flow: "Operativo",
  description: "",
});
type FormError = Partial<Record<keyof DraftFields, string>>;
export default function LocalDraftWorkbench({
  categories,
  branches,
  accounts,
  onCountChange,
}: {
  categories: string[];
  branches: string[];
  accounts: string[];
  onCountChange?: (n: number) => void;
}) {
  const { state, send } = useWorkspace();
  const drafts = state.expenses;
  const [editing, setEditing] = useState<string | null>(null);
  const [fields, setFields] = useState<DraftFields>(blank());
  const [errors, setErrors] = useState<FormError>({});
  const [notice, setNotice] = useState("");
  const update = <K extends keyof DraftFields>(field: K, value: DraftFields[K]) => {
    setFields((old) => ({ ...old, [field]: value, ...(field === "scope" ? { branch: "" } : {}) }));
    setErrors((old) => ({ ...old, [field]: undefined }));
    setNotice("");
  };
  const open = (id?: string) => {
    const source = drafts.find((d) => d.id === id);
    setFields(
      source
        ? {
            amount: source.amount,
            date: source.date,
            category: source.category,
            scope: source.scope,
            branch: source.branch,
            account: source.account,
            flow: source.flow,
            description: source.description,
          }
        : blank(),
    );
    setEditing(id ?? "new");
    setErrors({});
    setNotice("");
  };
  const save = () => {
    const invalid: FormError = {};
    if (fields.amount && cleanAmount(fields.amount) === null)
      invalid.amount = "Introduce un importe positivo en PEN, con hasta dos decimales.";
    if (fields.description.length > 500) invalid.description = "Máximo 500 caracteres.";
    if (fields.date && !/^\d{4}-\d{2}-\d{2}$/.test(fields.date)) invalid.date = "Fecha inválida.";
    if (fields.category && !categories.includes(fields.category))
      invalid.category = "Selecciona una categoría disponible.";
    if (fields.account && !accounts.includes(fields.account))
      invalid.account = "Selecciona una cuenta disponible.";
    if (fields.branch && !branches.includes(fields.branch))
      invalid.branch = "Selecciona una sede disponible.";
    setErrors(invalid);
    if (Object.keys(invalid).length) return;
    if (!editing) return;
    send({
      type: "saveExpense",
      draft: { ...fields, amount: fields.amount ? cleanAmount(fields.amount)!.toFixed(2) : "" },
      id: editing === "new" ? undefined : editing,
    });
    onCountChange?.(drafts.length + (editing === "new" ? 1 : 0));
    setNotice("Cambios guardados en esta sesión.");
    setEditing(null);
  };
  const discard = (id: string) => {
    send({ type: "deleteExpense", id });
    onCountChange?.(drafts.length - 1);
    setNotice("El registro se ha retirado de esta sesión.");
  };
  return (
    <section className="draft-workbench" aria-label="Bandeja de borradores">
      <div className="draft-workbench-header">
        <div>
          <span className="eyebrow">OPERACIONES</span>
          <h3>
            <FilePenLine size={21} /> Borradores de movimientos
          </h3>
          <p>
            Completa los datos de una salida, revisa las cuentas y retoma el registro cuando lo
            necesites.
          </p>
        </div>
        {!editing && (
          <button className="draft-create" onClick={() => open()}>
            <Plus size={16} /> Nuevo movimiento
          </button>
        )}
      </div>
      {notice && (
        <div className="draft-notice" role="status">
          <Check size={16} />
          {notice}
        </div>
      )}
      {editing ? (
        <div className="draft-editor">
          <div className="draft-editor-top">
            <div>
              <span className="eyebrow">DATOS DEL MOVIMIENTO</span>
              <h4>{editing === "new" ? "Nuevo movimiento" : `Editar ${editing}`}</h4>
            </div>
            <button
              className="draft-back"
              onClick={() => {
                setEditing(null);
                setNotice("Cambios descartados.");
              }}
            >
              <X size={16} /> Cancelar
            </button>
          </div>
          <div className="draft-form-grid">
            <label>
              Importe (PEN)
              <input
                inputMode="decimal"
                value={fields.amount}
                placeholder="0,00"
                onChange={(e) => update("amount", e.target.value)}
                aria-invalid={Boolean(errors.amount)}
              />
              {errors.amount && <small role="alert">{errors.amount}</small>}
            </label>
            <label>
              Fecha estimada
              <input
                type="date"
                value={fields.date}
                onChange={(e) => update("date", e.target.value)}
                aria-invalid={Boolean(errors.date)}
              />
              {errors.date && <small role="alert">{errors.date}</small>}
            </label>
            <label>
              Categoría
              <select value={fields.category} onChange={(e) => update("category", e.target.value)}>
                <option value="">Seleccionar categoría</option>
                {categories.map((c) => (
                  <option key={c}>{c}</option>
                ))}
              </select>
              {errors.category && <small role="alert">{errors.category}</small>}
            </label>
            <label>
              Clasificación
              <select value={fields.flow} onChange={(e) => update("flow", e.target.value)}>
                {["Operativo", "Capital", "Impuestos"].map((x) => (
                  <option key={x}>{x}</option>
                ))}
              </select>
            </label>
            <label>
              Ámbito
              <select
                value={fields.scope}
                onChange={(e) => update("scope", e.target.value as DraftFields["scope"])}
              >
                <option>Por sede</option>
                <option>Compartido</option>
              </select>
            </label>
            {fields.scope === "Por sede" && (
              <label>
                Sede
                <select value={fields.branch} onChange={(e) => update("branch", e.target.value)}>
                  <option value="">Seleccionar sede</option>
                  {branches.map((x) => (
                    <option key={x}>{x}</option>
                  ))}
                </select>
                {errors.branch && <small role="alert">{errors.branch}</small>}
              </label>
            )}
            <label>
              Cuenta de origen
              <select value={fields.account} onChange={(e) => update("account", e.target.value)}>
                <option value="">Seleccionar cuenta</option>
                {accounts.map((x) => (
                  <option key={x}>{x}</option>
                ))}
              </select>
              {errors.account && <small role="alert">{errors.account}</small>}
            </label>
            <label className="draft-description">
              Descripción
              <textarea
                rows={3}
                maxLength={520}
                placeholder="Motivo y contexto del movimiento"
                value={fields.description}
                onChange={(e) => update("description", e.target.value)}
                aria-invalid={Boolean(errors.description)}
              />
              {errors.description && <small role="alert">{errors.description}</small>}
            </label>
          </div>
          <div className="draft-form-actions">
            <span>
              <Clock3 size={15} /> Puedes guardar un registro incompleto. Contabilizar requiere
              todos los datos y validación del servidor.
            </span>
            <button className="draft-create" onClick={save}>
              <Check size={17} /> Guardar borrador
            </button>
          </div>
        </div>
      ) : drafts.length ? (
        <div className="draft-items" role="list">
          {drafts.map((d) => (
            <article key={d.id} role="listitem" className="draft-item">
              <div className="draft-item-summary">
                <strong>{d.id}</strong>
                <span>
                  {d.category || "Categoría pendiente"} · {d.scope}
                </span>
                <small>
                  <Clock3 size={13} />{" "}
                  {new Date(d.modifiedAt).toLocaleTimeString("es-PE", {
                    hour: "2-digit",
                    minute: "2-digit",
                  })}
                </small>
              </div>
              <div className="draft-item-right">
                <b>
                  {d.amount
                    ? `S/ ${Number(d.amount).toLocaleString("es-PE", { minimumFractionDigits: 2 })}`
                    : "Sin importe"}
                </b>
                <span>Borrador</span>
                <div>
                  <button onClick={() => open(d.id)}>
                    <PencilLine size={14} /> Editar
                  </button>
                  <button onClick={() => discard(d.id)}>
                    <Trash2 size={14} /> Descartar
                  </button>
                </div>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <div className="draft-empty">
          <FilePenLine size={24} />
          <div>
            <strong>No hay borradores.</strong>
            <span>Los movimientos que prepares aparecerán aquí para continuar su edición.</span>
          </div>
          <button onClick={() => open()}>
            <Plus size={15} /> Crear
          </button>
        </div>
      )}
    </section>
  );
}
