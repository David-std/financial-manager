import React, { useEffect, useMemo, useState } from "react";
import { Badge, Button, Card, CardHeader, Field, Input, Spinner, Text, makeStyles, tokens } from "@fluentui/react-components";
import { AddRegular, ArrowClockwiseRegular, CalendarRegular, DocumentRegular, OpenRegular, WalletRegular } from "@fluentui/react-icons";
import type { GeneratedComponentProps, fmg_financialtransaction } from "./RuntimeTypes";

type Payment = fmg_financialtransaction;
type PaymentsResult = { rows: Payment[]; hasMoreRows: boolean; loadMoreRows?: () => Promise<{ rows: Payment[]; hasMoreRows: boolean }> };
type LoadState = { loading: boolean; records: Payment[]; error: string; hasMore: boolean };
const cacheKey = "__financialManagerExpenses2026";

const useStyles = makeStyles({
  root: { display: "flex", flexDirection: "column", gap: tokens.spacingVerticalL, padding: tokens.spacingHorizontalXXL, backgroundColor: tokens.colorNeutralBackground2, color: tokens.colorNeutralForeground1, minHeight: "100%", boxSizing: "border-box" },
  title: { fontSize: tokens.fontSizeHero700, fontWeight: tokens.fontWeightSemibold, lineHeight: tokens.lineHeightHero700 },
  subheading: { fontSize: tokens.fontSizeBase300, color: tokens.colorNeutralForeground3 },
  header: { display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: tokens.spacingHorizontalM },
  toolbar: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: tokens.spacingHorizontalS },
  stats: { display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: tokens.spacingHorizontalM },
  statValue: { fontWeight: tokens.fontWeightSemibold, fontSize: tokens.fontSizeHero600 },
  card: { padding: tokens.spacingHorizontalM, backgroundColor: tokens.colorNeutralBackground1 },
  form: { display: "flex", alignItems: "end", flexWrap: "wrap", gap: tokens.spacingHorizontalM, padding: tokens.spacingHorizontalM, backgroundColor: tokens.colorNeutralBackground1, borderRadius: tokens.borderRadiusMedium },
  formInput: { flexGrow: 1, minWidth: "220px", maxWidth: "500px" },
  tableWrap: { overflowX: "auto", backgroundColor: tokens.colorNeutralBackground1, borderRadius: tokens.borderRadiusMedium },
  table: { borderCollapse: "collapse", width: "100%", minWidth: "760px" },
  cell: { borderBottom: "1px solid " + tokens.colorNeutralStroke2, padding: tokens.spacingHorizontalM, textAlign: "left", verticalAlign: "middle", fontSize: tokens.fontSizeBase300 },
  heading: { fontWeight: tokens.fontWeightSemibold, backgroundColor: tokens.colorNeutralBackground3 },
  empty: { textAlign: "center", padding: tokens.spacingHorizontalXXXL, color: tokens.colorNeutralForeground3 },
  error: { color: tokens.colorPaletteRedForeground1, fontWeight: tokens.fontWeightMedium },
  notice: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 }
});

function formatMoney(value: number | undefined | null): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return "—";
  return "S/ " + Number(value).toLocaleString("es-PE", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
function dateLabel(value: Date | string | undefined): string {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? date.toLocaleDateString("es-PE") : "—";
}
function statusText(value: number | undefined): string {
  if (value === 797280001) return "Confirmado";
  if (value === 797280002) return "Anulado";
  return "Borrador";
}
function typeText(value: number | undefined): string {
  if (value === 797280000) return "Operativo";
  if (value === 797280001) return "Capital";
  if (value === 797280002) return "Tributario";
  if (value === 797280003) return "Otro pago";
  return "—";
}
function lookupLabel(row: Payment, field: string): string {
  const data = row as unknown as Record<string, unknown>;
  const value = data[field + "@OData.Community.Display.V1.FormattedValue"];
  return typeof value === "string" && value.trim() ? value : "—";
}
function openTransaction(id: string): void {
  const host = (window as any).Xrm;
  if (!host?.Navigation?.navigateTo) return;
  void host.Navigation.navigateTo({ pageType: "generative", pageId: "4a317af5-b6a8-4a82-a22a-6b56447a082f", entityName: "fmg_financialtransaction", recordId: id }, {target: 1});
}

const ExpensePage = ({ dataApi }: GeneratedComponentProps) => {
  const styles = useStyles();
  const currentCache = (window as any)[cacheKey] as LoadState | undefined;
  const [data, setData] = useState<LoadState>(() => currentCache ?? { loading: true, records: [], error: "", hasMore: false });
  const [reload, setReload] = useState(0);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("all");
  const [description, setDescription] = useState("");
  const [saving, setSaving] = useState(false);
  const [feedback, setFeedback] = useState("");
  const ready = !!dataApi;

  useEffect(() => {
    if (!ready) return;
    let mounted = true;
    const initial = (window as any)[cacheKey] as LoadState | undefined;
    if (initial && reload === 0) { setData(initial); return; }
    setData(previous => ({ ...previous, loading: true, error: "" }));
    const globalState = window as any;
    const requestKey = cacheKey + "_request";
    if (!globalState[requestKey]) {
      globalState[requestKey] = dataApi.queryTable("fmg_financialtransaction", {
        select: ["fmg_financialtransactionid", "fmg_name", "fmg_transactiondate", "fmg_amount", "fmg_status", "fmg_flowclass", "fmg_description", "_fmg_category_value", "_fmg_branch_value", "_fmg_cashaccount_value"],
        orderBy: "createdon desc", pageSize: 200
      }).then((result: PaymentsResult) => {
        const state: LoadState = { loading: false, records: result.rows, hasMore: result.hasMoreRows, error: "" };
        globalState[cacheKey] = state;
        return state;
      }).catch((error: unknown) => {
        const state: LoadState = { loading: false, records: [], hasMore: false, error: error instanceof Error ? error.message : "No se pudieron consultar los movimientos." };
        globalState[cacheKey] = state;
        return state;
      }).finally(() => { globalState[requestKey] = null; });
    }
    (globalState[requestKey] as Promise<LoadState>).then(result => { if (mounted) setData(result); });
    return () => { mounted = false; };
  }, [ready, reload]);

  const visible = useMemo(() => data.records.filter(record => {
    const text = search.toLowerCase().trim();
    const matchesText = !text || [record.fmg_name, record.fmg_description].some(v => typeof v === "string" && v.toLowerCase().includes(text));
    const matchesStatus = status === "all" || record.fmg_status === Number(status);
    return matchesText && matchesStatus;
  }), [data.records, search, status]);
  const confirmed = data.records.filter(item => item.fmg_status === 797280001);
  const total = confirmed.reduce((sum, item) => sum + (Number(item.fmg_amount) || 0), 0);
  const draftCount = data.records.filter(item => item.fmg_status === 797280000).length;
  function refresh(): void {
    (window as any)[cacheKey] = null;
    (window as any)[cacheKey + "_request"] = null;
    setReload(count => count + 1);
  }
  async function createDraft(event: React.FormEvent): Promise<void> {
    event.preventDefault();
    if (saving || !description.trim()) return;
    setSaving(true); setFeedback("");
    try {
      const newId = await dataApi.createRow("fmg_financialtransaction", { fmg_description: description.trim(), fmg_status: 797280000 });
      setDescription("");
      setFeedback("Borrador creado correctamente. Completa sus datos antes de confirmar.");
      refresh();
      if (typeof newId === "string" && newId.length > 0) openTransaction(newId);
    } catch (error) {
      setFeedback(error instanceof Error ? error.message : "No se pudo guardar el borrador.");
    } finally {
      setSaving(false);
    }
  }
  return (
    <main className={styles.root} aria-label="Gestión de gastos de Financial Manager">
      <header className={styles.header}>
        <div>
          <Text className={styles.title} as="h1">Gastos</Text>
          <div className={styles.subheading}>Financial Manager · movimientos de pago · entorno de desarrollo</div>
        </div>
        <div className={styles.toolbar}>
          <Badge appearance="outline">Solo egresos · PEN</Badge>
          <Button icon={<ArrowClockwiseRegular />} onClick={refresh} disabled={data.loading}>Actualizar</Button>
        </div>
      </header>
      <section className={styles.stats} aria-label="Resumen de movimientos consultados">
        <Card className={styles.card}><CardHeader image={<WalletRegular />} header={<Text weight="semibold">Gastos confirmados</Text>} /><Text className={styles.statValue}>{formatMoney(total)}</Text><Text size={200}>Total en los registros consultados, no el informe completo</Text></Card>
        <Card className={styles.card}><CardHeader image={<DocumentRegular />} header={<Text weight="semibold">Borradores</Text>} /><Text className={styles.statValue}>{draftCount}</Text><Text size={200}>Pendientes de completar o revisar</Text></Card>
        <Card className={styles.card}><CardHeader image={<CalendarRegular />} header={<Text weight="semibold">Movimientos cargados</Text>} /><Text className={styles.statValue}>{data.records.length}</Text><Text size={200}>Hasta 200 registros recientes</Text></Card>
      </section>
      <form className={styles.form} onSubmit={createDraft} aria-label="Crear borrador de gasto">
        <Field label="Descripción inicial del gasto" className={styles.formInput}>
          <Input value={description} onChange={(_, next) => setDescription(next.value)} maxLength={2000} placeholder="Ej. Servicio de energía, octubre" />
        </Field>
        <Button type="submit" appearance="primary" icon={<AddRegular />} disabled={saving || !description.trim()}>{saving ? "Guardando..." : "Nuevo borrador"}</Button>
        {feedback && <Text role="status">{feedback}</Text>}
      </form>
      <section aria-label="Listado de gastos">
        <div className={styles.toolbar} style={{ marginBottom: "12px" }}>
          <Input aria-label="Buscar por referencia o descripción" placeholder="Buscar referencia o descripción" value={search} onChange={(_, next) => setSearch(next.value)} />
          <label htmlFor="fmStatus">Estado </label>
          <select id="fmStatus" value={status} onChange={event => setStatus(event.target.value)} style={{ padding: "8px", borderRadius: "6px", borderColor: "#aaa" }}>
            <option value="all">Todos</option><option value="797280000">Borradores</option><option value="797280001">Confirmados</option><option value="797280002">Anulados</option>
          </select>
        </div>
        {data.loading && <div className={styles.empty}><Spinner label="Cargando movimientos..." /></div>}
        {data.error && <div className={styles.error} role="alert">{data.error}<Button onClick={refresh}>Reintentar</Button></div>}
        {!data.loading && !data.error && visible.length === 0 && <div className={styles.empty}>No se encontraron movimientos para estos filtros.</div>}
        {!data.loading && !data.error && visible.length > 0 && (
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <thead><tr>{["Referencia","Fecha","Estado","Clase","Categoría","Sede","Importe","Detalle"].map(title => <th key={title} className={styles.cell + " " + styles.heading}>{title}</th>)}</tr></thead>
              <tbody>{visible.map(record => (
                <tr key={record.fmg_financialtransactionid}>
                  <td className={styles.cell}>{record.fmg_name || "—"}</td><td className={styles.cell}>{dateLabel(record.fmg_transactiondate)}</td>
                  <td className={styles.cell}><Badge appearance="outline">{statusText(record.fmg_status)}</Badge></td>
                  <td className={styles.cell}>{typeText(record.fmg_flowclass)}</td>
                  <td className={styles.cell}>{lookupLabel(record, "_fmg_category_value")}</td>
                  <td className={styles.cell}>{lookupLabel(record, "_fmg_branch_value")}</td>
                  <td className={styles.cell}>{formatMoney(record.fmg_amount)}</td>
                  <td className={styles.cell}><Button appearance="subtle" icon={<OpenRegular />} onClick={() => openTransaction(record.fmg_financialtransactionid)}>Abrir</Button></td>
                </tr>))}
              </tbody>
            </table>
          </div>)}
      </section>
      <Text className={styles.notice}>Los importes confirmados se consultan sin duplicar gastos compartidos. Este resumen es orientativo y no sustituye el informe analítico de Power BI. Confirmación y anulación protegidas por Dataverse; la disponibilidad depende de los permisos reales del usuario.</Text>
      {data.hasMore && <Text className={styles.notice}>Existen movimientos adicionales. Consulte la vista nativa de Dataverse para el historial completo.</Text>}
    </main>
  );
};
export default ExpensePage;
