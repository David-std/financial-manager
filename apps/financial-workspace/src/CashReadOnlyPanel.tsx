import { useEffect, useState } from "react";
import type {
  CashLiveCollection,
  DelegatedLiveRepository,
  LivePage,
  LiveRow,
} from "./infrastructure/data/live-read-repository";

const views: ReadonlyArray<{
  key: CashLiveCollection;
  label: string;
  fields: ReadonlyArray<[string, string]>;
}> = [
  {
    key: "cashReceipts",
    label: "Cobros",
    fields: [
      ["fmg_name", "Referencia"],
      ["fmg_receivedon", "Fecha"],
      ["fmg_amount", "Importe PEN"],
      ["fmg_status", "Estado"],
    ],
  },
  {
    key: "cashTransfers",
    label: "Transferencias",
    fields: [
      ["fmg_name", "Referencia"],
      ["fmg_transferredon", "Fecha"],
      ["fmg_amount", "Importe PEN"],
      ["fmg_status", "Estado"],
    ],
  },
  {
    key: "cashPostings",
    label: "Asientos",
    fields: [
      ["fmg_name", "Referencia"],
      ["fmg_postedon", "Fecha"],
      ["fmg_signedamount", "Movimiento PEN"],
      ["fmg_kind", "Tipo"],
    ],
  },
  {
    key: "cashOpenings",
    label: "Aperturas",
    fields: [
      ["fmg_name", "Referencia"],
      ["fmg_effectiveon", "Fecha"],
      ["fmg_amount", "Saldo de apertura PEN"],
      ["fmg_status", "Estado"],
    ],
  },
  {
    key: "bankStatements",
    label: "Extractos",
    fields: [
      ["fmg_name", "Referencia"],
      ["fmg_asof", "Corte"],
      ["fmg_closingbalance", "Saldo bancario PEN"],
      ["fmg_status", "Estado"],
    ],
  },
  {
    key: "bankReconciliations",
    label: "Conciliaciones",
    fields: [
      ["fmg_name", "Referencia"],
      ["fmg_asof", "Corte"],
      ["fmg_bookbalance", "Saldo libro PEN"],
      ["fmg_statementbalance", "Saldo banco PEN"],
      ["fmg_difference", "Diferencia PEN"],
      ["fmg_status", "Estado"],
    ],
  },
];

function formatCell(field: string, value: unknown): string {
  if (value === null || value === undefined) return "Sin registrar";
  if (
    typeof value === "number" &&
    [
      "fmg_amount",
      "fmg_signedamount",
      "fmg_closingbalance",
      "fmg_bookbalance",
      "fmg_statementbalance",
      "fmg_difference",
    ].includes(field)
  )
    return new Intl.NumberFormat("es-PE", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(value);
  if (typeof value === "number") return String(value);
  if (typeof value === "boolean") return value ? "Sí" : "No";
  if (
    typeof value === "string" &&
    ["fmg_receivedon", "fmg_transferredon", "fmg_postedon", "fmg_effectiveon", "fmg_asof"].includes(
      field,
    )
  )
    return value.slice(0, 10);
  return typeof value === "string" ? value.slice(0, 160) : "Dato no disponible";
}

export default function CashReadOnlyPanel({ repo }: { repo: DelegatedLiveRepository }) {
  const [section, setSection] = useState<CashLiveCollection>("cashReceipts");
  const [page, setPage] = useState<LivePage>({ rows: [], next: null });
  const [loading, setLoading] = useState(false);
  const [issue, setIssue] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const selected = views.find((v) => v.key === section)!;

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setIssue(null);
    setPage({ rows: [], next: null });
    void repo
      .readPage(section)
      .then(
        (value) => {
          if (!cancelled) setPage(value);
        },
        (error: unknown) => {
          if (!cancelled)
            setIssue(error instanceof Error ? error.message : "Consulta no autorizada.");
        },
      )
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [repo, section, reloadKey]);

  async function loadNext() {
    if (!page.next || loading) return;
    const next = page.next;
    setLoading(true);
    setIssue(null);
    try {
      const additional = await repo.readPage(section, next);
      setPage((prior) =>
        prior.next === next
          ? { rows: [...prior.rows, ...additional.rows], next: additional.next }
          : prior,
      );
    } catch (error) {
      setIssue(
        error instanceof Error ? error.message : "No se pudo recuperar la siguiente página.",
      );
    } finally {
      setLoading(false);
    }
  }

  return (
    <section className="live-cash-readonly" aria-label="Consulta de caja en Dataverse">
      <h2>Caja y conciliación</h2>
      <p>
        Consulta segura de registros autorizados por Dataverse. No se permiten cobros,
        transferencias, aperturas ni conciliaciones desde esta pantalla mientras F5 siga bloqueada.
      </p>
      <div className="live-tabs" role="group" aria-label="Secciones de caja">
        {views.map((view) => (
          <button
            type="button"
            key={view.key}
            aria-pressed={section === view.key}
            onClick={() => setSection(view.key)}
          >
            {view.label}
          </button>
        ))}
      </div>
      <div className="live-section-heading">
        <div>
          <h3>{selected.label}</h3>
          <small>
            Registros cargados en esta página: {page.rows.length}. No representa el saldo total.
          </small>
        </div>
        <button disabled={loading} type="button" onClick={() => setReloadKey((v) => v + 1)}>
          Actualizar
        </button>
      </div>
      {loading && <p role="status">Consultando Dataverse...</p>}
      {issue && <p role="alert">No se pudo consultar esta sección: {issue}</p>}
      {!loading && !issue && page.rows.length === 0 && (
        <p>No hay registros visibles. No se asume saldo cero.</p>
      )}
      {!issue && page.rows.length > 0 && (
        <div
          className="live-table-scroll"
          role="region"
          aria-label="Registros de caja"
          tabIndex={0}
        >
          <table className="live-table">
            <thead>
              <tr>
                {selected.fields.map(([field, label]) => (
                  <th scope="col" key={field}>
                    {label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {page.rows.map((row: LiveRow, index: number) => (
                <tr
                  key={String(
                    row.fmg_cashreceiptid ??
                      row.fmg_cashtransferid ??
                      row.fmg_cashpostingid ??
                      row.fmg_accountopeningid ??
                      row.fmg_bankstatementid ??
                      row.fmg_bankreconciliationid ??
                      index,
                  )}
                >
                  {selected.fields.map(([field]) => (
                    <td key={field}>{formatCell(field, row[field])}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {page.next && (
        <button type="button" disabled={loading || Boolean(issue)} onClick={() => void loadNext()}>
          Cargar siguientes 25
        </button>
      )}
      <p className="live-field-help">
        La auditoría oficial corresponde a Dataverse. Sin grant aprobado, no existe permiso para
        publicar ni modificar movimientos de caja.
      </p>
    </section>
  );
}
