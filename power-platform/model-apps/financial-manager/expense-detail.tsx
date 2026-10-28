import React, { useEffect, useMemo, useRef, useState } from "react";
import {
  Badge,
  Button,
  Card,
  Field,
  Input,
  Spinner,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  ArrowClockwiseRegular,
  CheckmarkRegular,
  SaveRegular,
  ShieldLockRegular,
} from "@fluentui/react-icons";
import type {
  GeneratedComponentProps,
  fmg_financialtransaction,
  fmg_category,
  fmg_cashaccount,
  fmg_branch,
  transactioncurrency,
} from "./RuntimeTypes";

// Power Apps supplies pageInput as a runtime global, not as a React component prop.
declare const pageInput: { recordId?: string; entityName?: string } | undefined;

type Payment = fmg_financialtransaction;
type Data = {
  loading: boolean;
  record: Payment | null;
  categories: fmg_category[];
  accounts: fmg_cashaccount[];
  branches: fmg_branch[];
  pen: string;
  error: string;
};
type Inputs = {
  date: string;
  amount: string;
  flow: string;
  scope: string;
  group: string;
  category: string;
  branch: string;
  account: string;
  description: string;
  reference: string;
  voidReason: string;
};
const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground2,
    // Generative pages run inside a fixed-height model-driven app surface.
    // Give that surface its own vertical scroll, including on mobile/touch.
    height: "100%",
    minHeight: 0,
    overflowY: "auto",
    overflowX: "hidden",
    overscrollBehaviorY: "contain",
    WebkitOverflowScrolling: "touch",
    boxSizing: "border-box",
    color: tokens.colorNeutralForeground1,
  },
  header: {
    display: "flex",
    flexWrap: "wrap",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
  },
  title: { fontSize: tokens.fontSizeHero700, fontWeight: tokens.fontWeightSemibold },
  form: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
    gap: tokens.spacingHorizontalM,
  },
  panel: { padding: tokens.spacingHorizontalL, backgroundColor: tokens.colorNeutralBackground1 },
  toolbar: {
    display: "flex",
    flexWrap: "wrap",
    gap: tokens.spacingHorizontalM,
    alignItems: "center",
  },
  error: { color: tokens.colorPaletteRedForeground1 },
  select: {
    width: "100%",
    padding: "10px",
    borderRadius: "5px",
    border: "1px solid #aaa",
    backgroundColor: "#fff",
  },
  wide: { gridColumn: "1/-1" },
});
const initial: Inputs = {
  date: "",
  amount: "",
  flow: "",
  scope: "",
  group: "",
  category: "",
  branch: "",
  account: "",
  description: "",
  reference: "",
  voidReason: "",
};
function guid(value: unknown): string {
  if (typeof value !== "string") return "";
  const m = value.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i);
  return m ? m[0].toLowerCase() : "";
}
function active(value: unknown): boolean {
  return value === 1 || value === true;
}
function dateInput(value: Date | string | undefined): string {
  if (!value) return "";
  const d = new Date(value);
  return Number.isFinite(d.getTime()) ? d.toISOString().slice(0, 10) : "";
}
function buildInputs(row: Payment, categories: fmg_category[]): Inputs {
  const category = guid(row._fmg_category_value);
  const cat = categories.find((c) => c.fmg_categoryid.toLowerCase() === category);
  return {
    date: dateInput(row.fmg_transactiondate),
    amount: row.fmg_amount == null ? "" : String(row.fmg_amount),
    flow: row.fmg_flowclass == null ? "" : String(row.fmg_flowclass),
    scope: row.fmg_scope == null ? "" : String(row.fmg_scope),
    group: guid(cat?._fmg_parentcategory_value),
    category: category,
    branch: guid(row._fmg_branch_value),
    account: guid(row._fmg_cashaccount_value),
    description: row.fmg_description || "",
    reference: row.fmg_documentreference || "",
    voidReason: row.fmg_voidreason || "",
  };
}
function statusText(status: number): string {
  return status === 797280001 ? "Confirmado" : status === 797280002 ? "Anulado" : "Borrador";
}
// Only business fields editable in a Draft enter this comparison.
// It is a best-effort stale-editor check, NOT transactional optimistic locking.
function draftFingerprint(row: Payment): string {
  return JSON.stringify([
    row.fmg_status,
    dateInput(row.fmg_transactiondate),
    row.fmg_amount == null ? "" : String(Number(row.fmg_amount)),
    row.fmg_flowclass == null ? "" : String(row.fmg_flowclass),
    row.fmg_scope == null ? "" : String(row.fmg_scope),
    guid(row._fmg_category_value),
    guid(row._fmg_branch_value),
    guid(row._fmg_cashaccount_value),
    row.fmg_description || "",
    row.fmg_documentreference || "",
  ]);
}
function editorFingerprint(values: Inputs): string {
  return JSON.stringify([
    values.date,
    values.amount === "" ? "" : String(Number(values.amount)),
    values.flow,
    values.scope,
    values.category,
    values.branch,
    values.account,
    values.description,
    values.reference,
  ]);
}
const draftReadColumns = [
  "fmg_status",
  "fmg_transactiondate",
  "fmg_amount",
  "fmg_flowclass",
  "fmg_scope",
  "_fmg_category_value",
  "_fmg_branch_value",
  "_fmg_cashaccount_value",
  "fmg_description",
  "fmg_documentreference",
] as const;
const ExpenseDetail = (props: GeneratedComponentProps) => {
  const css = useStyles();
  const { dataApi } = props;
  const pageProps = props as unknown as { pageInput?: { recordId?: string }; recordId?: string };
  const recordId = guid(
    pageProps.pageInput?.recordId ||
      pageProps.recordId ||
      (typeof pageInput !== "undefined" ? pageInput?.recordId : "") ||
      (window as any).pageInput?.recordId,
  );
  const [data, setData] = useState<Data>({
    loading: !!recordId,
    record: null,
    categories: [],
    accounts: [],
    branches: [],
    pen: "",
    error: "",
  });
  const [inputs, setInputs] = useState<Inputs>(initial);
  const [version, setVersion] = useState(0);
  const [pendingPost, setPendingPost] = useState(false);
  const [pendingVoid, setPendingVoid] = useState(false);
  const [pendingRefresh, setPendingRefresh] = useState(false);
  const [busy, setBusy] = useState(false);
  // Synchronous per-page guard: rapid clicks cannot issue duplicate financial transitions.
  const transitionInProgress = useRef(false);
  const [feedback, setFeedback] = useState("");
  const ready = !!dataApi;
  useEffect(() => {
    if (!recordId || !ready) return;
    let mounted = true;
    setData((d) => ({ ...d, loading: true, error: "" }));
    Promise.all([
      dataApi.retrieveRow("fmg_financialtransaction", {
        id: recordId,
        select: [
          "fmg_financialtransactionid",
          "fmg_name",
          "fmg_status",
          "fmg_transactiondate",
          "fmg_amount",
          "fmg_flowclass",
          "fmg_scope",
          "_fmg_category_value",
          "_fmg_branch_value",
          "_fmg_cashaccount_value",
          "fmg_description",
          "fmg_documentreference",
          "fmg_voidreason",
        ],
      }),
      dataApi.queryTable("fmg_category", {
        select: [
          "fmg_categoryid",
          "fmg_name",
          "fmg_code",
          "fmg_isactive",
          "fmg_isgroup",
          "_fmg_parentcategory_value",
        ],
        pageSize: 100,
      }),
      dataApi.queryTable("fmg_cashaccount", {
        select: ["fmg_cashaccountid", "fmg_name", "fmg_isactive", "_fmg_branch_value"],
        pageSize: 100,
      }),
      dataApi.queryTable("fmg_branch", {
        select: ["fmg_branchid", "fmg_name", "fmg_isactive"],
        pageSize: 100,
      }),
      dataApi.queryTable("transactioncurrency", {
        select: ["transactioncurrencyid", "isocurrencycode"],
        filter: "isocurrencycode eq 'PEN'",
        pageSize: 5,
      }),
    ])
      .then(([row, cats, accounts, branches, currencies]) => {
        if (!mounted) return;
        const categories = cats.rows;
        const pen =
          (currencies.rows as transactioncurrency[]).length === 1
            ? currencies.rows[0].transactioncurrencyid
            : "";
        setData({
          loading: false,
          record: row,
          categories,
          accounts: accounts.rows,
          branches: branches.rows,
          pen,
          error: "",
        });
        setInputs(buildInputs(row, categories));
      })
      .catch((err: unknown) => {
        if (mounted)
          setData((d) => ({
            ...d,
            loading: false,
            error: err instanceof Error ? err.message : "No se pudo cargar el movimiento.",
          }));
      });
    return () => {
      mounted = false;
    };
  }, [recordId, ready, version]);
  const groups = useMemo(
    () => data.categories.filter((c) => active(c.fmg_isactive) && active(c.fmg_isgroup)),
    [data.categories],
  );
  const leaves = useMemo(
    () =>
      data.categories.filter(
        (c) =>
          active(c.fmg_isactive) &&
          !active(c.fmg_isgroup) &&
          c.fmg_code !== "999" &&
          guid(c._fmg_parentcategory_value) === inputs.group,
      ),
    [data.categories, inputs.group],
  );
  const accounts = useMemo(
    () =>
      data.accounts.filter((c) => {
        if (!active(c.fmg_isactive)) return false;
        const assigned = guid(c._fmg_branch_value);
        if (inputs.scope === "797280001") return !assigned;
        if (inputs.scope === "797280000")
          return !assigned || (!!inputs.branch && assigned === inputs.branch);
        // Never offer a location-specific account before the spending scope is selected.
        return !assigned;
      }),
    [data.accounts, inputs.scope, inputs.branch],
  );
  function change(field: keyof Inputs, value: string) {
    setInputs((current) => ({ ...current, [field]: value }));
    setFeedback("");
    if (field !== "voidReason") {
      setPendingPost(false);
      setPendingRefresh(false);
    }
  }
  function reload() {
    setVersion((v) => v + 1);
    setPendingPost(false);
    setPendingVoid(false);
    setPendingRefresh(false);
    setFeedback("");
  }
  const draft = data.record?.fmg_status === 797280000;
  const dirty = !!(
    draft &&
    data.record &&
    editorFingerprint(inputs) !== editorFingerprint(buildInputs(data.record, data.categories))
  );
  function requestRefresh(): void {
    if (dirty) setPendingRefresh(true);
    else reload();
  }
  function validOptionalSelections(): string {
    if (inputs.date) {
      const date = new Date(inputs.date + "T12:00:00.000Z");
      if (
        !/^\d{4}-\d{2}-\d{2}$/.test(inputs.date) ||
        !Number.isFinite(date.getTime()) ||
        date.toISOString().slice(0, 10) !== inputs.date
      )
        return "Indica una fecha válida.";
    }
    if (inputs.flow && !["797280000", "797280001", "797280002", "797280003"].includes(inputs.flow))
      return "La clasificación no es válida.";
    if (inputs.scope && !["797280000", "797280001"].includes(inputs.scope))
      return "El ámbito no es válido.";
    if (inputs.branch) {
      const branch = data.branches.find((b) => guid(b.fmg_branchid) === inputs.branch);
      if (inputs.scope !== "797280000" || !branch || !active(branch.fmg_isactive))
        return "Selecciona una sede activa compatible con el ámbito.";
    }
    if (inputs.category) {
      const category = data.categories.find((c) => guid(c.fmg_categoryid) === inputs.category);
      if (
        !category ||
        !active(category.fmg_isactive) ||
        active(category.fmg_isgroup) ||
        category.fmg_code === "999" ||
        guid(category._fmg_parentcategory_value) !== inputs.group
      )
        return "Selecciona una categoría de detalle activa del grupo indicado.";
    }
    if (inputs.account) {
      const account = data.accounts.find((a) => guid(a.fmg_cashaccountid) === inputs.account);
      const assigned = guid(account?._fmg_branch_value);
      if (
        !account ||
        !active(account.fmg_isactive) ||
        !(inputs.scope === "797280001"
          ? !assigned
          : inputs.scope === "797280000" &&
            (!assigned || (!!inputs.branch && assigned === inputs.branch)))
      )
        return "La cuenta seleccionada no corresponde al ámbito o a la sede.";
    }
    return "";
  }
  async function saveDraft(): Promise<boolean> {
    if (!recordId || !draft || busy) return false;
    if (
      inputs.amount.trim() &&
      (!/^(?:0|[1-9]\d*)(?:\.\d{1,2})?$/.test(inputs.amount.trim()) ||
        !Number.isFinite(Number(inputs.amount)) ||
        Number(inputs.amount) <= 0)
    ) {
      setFeedback("Indica un importe positivo con máximo dos decimales, usando punto decimal.");
      return false;
    }
    const invalidSelection = validOptionalSelections();
    if (invalidSelection) {
      setFeedback(invalidSelection);
      return false;
    }
    const payload: Record<string, unknown> = {
      fmg_description: inputs.description,
      fmg_documentreference: inputs.reference,
    };
    // Omitted fields retain their previous Dataverse values; cleared draft inputs must be explicit.
    payload.fmg_transactiondate = inputs.date ? new Date(inputs.date + "T12:00:00.000Z") : null;
    payload.fmg_amount = inputs.amount ? Number(inputs.amount) : null;
    payload.fmg_flowclass = inputs.flow ? Number(inputs.flow) : null;
    payload.fmg_scope = inputs.scope ? Number(inputs.scope) : null;
    // Generated-page dataApi writes lookups through typed foreign-key columns, not @odata.bind.
    payload._fmg_category_value = inputs.category ? "/fmg_category(" + inputs.category + ")" : null;
    payload._fmg_cashaccount_value = inputs.account
      ? "/fmg_cashaccount(" + inputs.account + ")"
      : null;
    payload._fmg_branch_value =
      inputs.scope === "797280001"
        ? null
        : inputs.branch
          ? "/fmg_branch(" + inputs.branch + ")"
          : null;
    if (data.pen) payload._transactioncurrencyid_value = "/transactioncurrency(" + data.pen + ")";
    setBusy(true);
    setFeedback("");
    try {
      // This read detects an editor left open after another session changed a Draft.
      // Server-side concurrency is still authoritative: dataApi.updateRow has no If-Match here.
      const latest = await dataApi.retrieveRow("fmg_financialtransaction", {
        id: recordId,
        select: [...draftReadColumns],
      });
      if (
        !data.record ||
        latest.fmg_status !== 797280000 ||
        draftFingerprint(latest) !== draftFingerprint(data.record)
      )
        throw new Error(
          "Este borrador cambió en Dataverse desde que se abrió. Conserva tus datos, " +
            "revisa los cambios y actualiza antes de guardar. No se ha enviado ninguna modificación.",
        );
      await dataApi.updateRow("fmg_financialtransaction", recordId, payload as any);
      const saved = await dataApi.retrieveRow("fmg_financialtransaction", {
        id: recordId,
        select: [
          "fmg_financialtransactionid",
          "fmg_name",
          ...draftReadColumns,
          "_transactioncurrencyid_value",
          "fmg_voidreason",
        ],
      });
      const mismatches: string[] = [];
      if (guid(saved._fmg_category_value) !== inputs.category) mismatches.push("categoría");
      if (guid(saved._fmg_cashaccount_value) !== inputs.account)
        mismatches.push("cuenta de origen");
      if (guid(saved._fmg_branch_value) !== (inputs.scope === "797280001" ? "" : inputs.branch))
        mismatches.push("sede");
      if (data.pen && guid(saved._transactioncurrencyid_value) !== data.pen)
        mismatches.push("moneda");
      if (mismatches.length) {
        setFeedback(
          "No se conservaron las relaciones: " +
            mismatches.join(", ") +
            ". El borrador debe revisarse antes de confirmar.",
        );
        return false;
      }
      if (
        saved.fmg_status !== 797280000 ||
        editorFingerprint(buildInputs(saved, data.categories)) !== editorFingerprint(inputs)
      ) {
        setFeedback(
          "Dataverse devolvió valores distintos de los enviados. " +
            "No se confirmará el pago: revisa la versión actual y los campos antes de continuar.",
        );
        return false;
      }
      // Refresh the baseline after each save so later edits can detect external changes.
      setData((previous) => ({ ...previous, record: saved }));
      setInputs(buildInputs(saved, data.categories));
      setFeedback("Borrador guardado correctamente.");
      return true;
    } catch (err) {
      setFeedback(err instanceof Error ? err.message : "No se pudo guardar el borrador.");
      return false;
    } finally {
      setBusy(false);
    }
  }
  async function post(): Promise<void> {
    if (!recordId || busy || !draft) return;
    if (
      !inputs.date ||
      !inputs.amount ||
      !inputs.flow ||
      !inputs.scope ||
      !inputs.category ||
      !inputs.account ||
      (inputs.scope === "797280000" && !inputs.branch) ||
      !data.pen
    ) {
      setFeedback(
        "Completa fecha, importe, clasificación, ámbito, categoría, cuenta y sede si corresponde; la moneda PEN debe estar disponible.",
      );
      return;
    }
    // UX checks never replace the server plug-in, but avoid asking it to post stale
    // or plainly invalid selections. This read is not an atomic concurrency guard.
    const selectedCategory = data.categories.find(
      (c) => c.fmg_categoryid.toLowerCase() === inputs.category,
    );
    const selectedAccount = data.accounts.find(
      (a) => a.fmg_cashaccountid.toLowerCase() === inputs.account,
    );
    const assignedBranch = guid(selectedAccount?._fmg_branch_value);
    const categoryValid =
      !!selectedCategory &&
      active(selectedCategory.fmg_isactive) &&
      !active(selectedCategory.fmg_isgroup) &&
      selectedCategory.fmg_code !== "999";
    const accountValid =
      !!selectedAccount &&
      active(selectedAccount.fmg_isactive) &&
      (inputs.scope === "797280001"
        ? !assignedBranch
        : inputs.scope === "797280000" &&
          !!inputs.branch &&
          (!assignedBranch || assignedBranch === inputs.branch));
    const parsedDate = new Date(inputs.date + "T12:00:00.000Z");
    const dateValid =
      /^\d{4}-\d{2}-\d{2}$/.test(inputs.date) &&
      Number.isFinite(parsedDate.getTime()) &&
      parsedDate.toISOString().slice(0, 10) === inputs.date;
    if (!categoryValid || !accountValid || !dateValid) {
      setFeedback(
        "Revisa la categoría de detalle activa, fecha, ámbito, sede y cuenta de origen antes de confirmar.",
      );
      setPendingPost(false);
      return;
    }
    // Require the operator to save and independently verify a Draft before Post.
    // Auto-saving during a state transition hides stale edits and creates a race.
    if (
      !data.record ||
      editorFingerprint(inputs) !== editorFingerprint(buildInputs(data.record, data.categories))
    ) {
      setFeedback("Guarda el borrador y comprueba los valores persistidos antes de contabilizar.");
      setPendingPost(false);
      return;
    }
    if (transitionInProgress.current) return;
    transitionInProgress.current = true;
    setBusy(true);
    setFeedback("");
    try {
      const current = await dataApi.retrieveRow("fmg_financialtransaction", {
        id: recordId,
        select: [...draftReadColumns],
      });
      if (
        current.fmg_status !== 797280000 ||
        draftFingerprint(current) !== draftFingerprint(data.record)
      )
        throw new Error(
          "El borrador cambió en Dataverse. Actualiza la pantalla antes de contabilizar.",
        );
    } catch (err) {
      setFeedback(err instanceof Error ? err.message : "No se pudo comprobar el estado actual.");
      setBusy(false);
      setPendingPost(false);
      transitionInProgress.current = false;
      return;
    }
    try {
      // Posting remains server-authorized (FTV007); no client-role bypass.
      await dataApi.updateRow("fmg_financialtransaction", recordId, { fmg_status: 797280001 });
      const confirmed = await dataApi.retrieveRow("fmg_financialtransaction", {
        id: recordId,
        select: ["fmg_status"],
      });
      if (confirmed.fmg_status !== 797280001) {
        throw new Error(
          "Dataverse no confirmó el estado contabilizado. Actualiza antes de reintentar.",
        );
      }
      reload();
    } catch (err) {
      setFeedback(
        err instanceof Error
          ? err.message
          : "No se pudo confirmar el gasto. Comprueba permisos y datos.",
      );
    } finally {
      setBusy(false);
      setPendingPost(false);
      transitionInProgress.current = false;
    }
  }
  async function voidPayment(): Promise<void> {
    if (!recordId || busy || transitionInProgress.current || data.record?.fmg_status !== 797280001)
      return;
    if (!inputs.voidReason.trim()) {
      setFeedback("Es obligatorio indicar el motivo de anulación.");
      return;
    }
    transitionInProgress.current = true;
    setBusy(true);
    setFeedback("");
    try {
      const current = await dataApi.retrieveRow("fmg_financialtransaction", {
        id: recordId,
        select: ["fmg_status"],
      });
      if (current.fmg_status !== 797280001)
        throw new Error(
          "El movimiento ya no está contabilizado. Actualiza la pantalla antes de continuar.",
        );
      await dataApi.updateRow("fmg_financialtransaction", recordId, {
        fmg_status: 797280002,
        fmg_voidreason: inputs.voidReason.trim(),
      });
      const voided = await dataApi.retrieveRow("fmg_financialtransaction", {
        id: recordId,
        select: ["fmg_status", "fmg_voidreason"],
      });
      if (
        voided.fmg_status !== 797280002 ||
        voided.fmg_voidreason?.trim() !== inputs.voidReason.trim()
      ) {
        throw new Error(
          "No se confirmó la anulación o el motivo en Dataverse. Actualiza antes de reintentar.",
        );
      }
      reload();
    } catch (err) {
      setFeedback(
        err instanceof Error ? err.message : "No se pudo anular. Comprueba permisos y motivo.",
      );
    } finally {
      setBusy(false);
      setPendingVoid(false);
      transitionInProgress.current = false;
    }
  }
  if (!recordId)
    return (
      <main className={css.root}>
        <Text as="h1" className={css.title}>
          Detalle de gasto
        </Text>
        <Card>Selecciona un movimiento en Gastos para ver su detalle.</Card>
      </main>
    );
  return (
    <main className={css.root} aria-label="Editar o consultar pago">
      <header className={css.header}>
        <div>
          <Text as="h1" className={css.title}>
            Detalle de gasto
          </Text>
        </div>
        <Button
          icon={<ArrowClockwiseRegular />}
          onClick={requestRefresh}
          disabled={busy || data.loading}
        >
          Actualizar
        </Button>
      </header>
      {pendingRefresh && (
        <Card className={css.panel} aria-label="Confirmar actualización de borrador">
          <Text weight="semibold">Hay cambios sin guardar</Text>
          <Text>
            Actualizar descartará las modificaciones realizadas en este formulario. No se ha
            modificado el registro en Dataverse.
          </Text>
          <div className={css.toolbar}>
            <Button appearance="primary" disabled={busy} onClick={reload}>
              Descartar cambios y actualizar
            </Button>
            <Button disabled={busy} onClick={() => setPendingRefresh(false)}>
              Seguir editando
            </Button>
          </div>
        </Card>
      )}
      {dirty && !pendingRefresh && (
        <Text role="status">
          Tienes cambios sin guardar. Guárdalos antes de abandonar esta página.
        </Text>
      )}
      {data.loading && <Spinner label="Cargando movimiento y catálogos..." />}
      {data.error && (
        <Text role="alert" className={css.error}>
          {data.error}
        </Text>
      )}
      {!data.loading && !data.error && data.record && (
        <>
          <div className={css.header}>
            <Text weight="semibold">{data.record.fmg_name || "Movimiento"}</Text>
            <Badge appearance="outline">{statusText(data.record.fmg_status)}</Badge>
          </div>
          <Card className={css.panel}>
            <div className={css.form}>
              <Field label="Fecha del pago">
                <Input
                  type="date"
                  disabled={!draft || busy}
                  value={inputs.date}
                  onChange={(_, v) => change("date", v.value)}
                />
              </Field>
              <Field label="Importe (PEN)">
                <Input
                  type="number"
                  min="0.01"
                  step="0.01"
                  disabled={!draft || busy}
                  value={inputs.amount}
                  onChange={(_, v) => change("amount", v.value)}
                />
              </Field>
              <Field label="Clasificación del pago">
                <select
                  aria-label="Clasificación del pago"
                  className={css.select}
                  disabled={!draft || busy}
                  value={inputs.flow}
                  onChange={(e) => change("flow", e.target.value)}
                >
                  <option value="">Seleccionar</option>
                  <option value="797280000">Pago operativo</option>
                  <option value="797280001">Pago de capital</option>
                  <option value="797280002">Pago tributario</option>
                  <option value="797280003">Otro pago</option>
                </select>
              </Field>
              <Field label="Ámbito">
                <select
                  aria-label="Ámbito"
                  className={css.select}
                  disabled={!draft || busy}
                  value={inputs.scope}
                  onChange={(e) => {
                    change("scope", e.target.value);
                    change("branch", "");
                    change("account", "");
                  }}
                >
                  <option value="">Seleccionar</option>
                  <option value="797280000">Por sede</option>
                  <option value="797280001">Compartido</option>
                </select>
              </Field>
              {inputs.scope === "797280000" && (
                <Field label="Sede">
                  <select
                    aria-label="Sede"
                    className={css.select}
                    disabled={!draft || busy}
                    value={inputs.branch}
                    onChange={(e) => {
                      change("branch", e.target.value);
                      change("account", "");
                    }}
                  >
                    <option value="">Seleccionar sede</option>
                    {data.branches
                      .filter((b) => active(b.fmg_isactive))
                      .map((b) => (
                        <option key={b.fmg_branchid} value={b.fmg_branchid}>
                          {b.fmg_name}
                        </option>
                      ))}
                  </select>
                </Field>
              )}
              <Field label="Grupo de categoría">
                <select
                  aria-label="Grupo de categoría"
                  className={css.select}
                  disabled={!draft || busy}
                  value={inputs.group}
                  onChange={(e) => {
                    change("group", e.target.value);
                    change("category", "");
                  }}
                >
                  <option value="">Seleccionar grupo</option>
                  {groups.map((g) => (
                    <option key={g.fmg_categoryid} value={g.fmg_categoryid}>
                      {g.fmg_code} · {g.fmg_name}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Categoría de detalle">
                <select
                  aria-label="Categoría de detalle"
                  className={css.select}
                  disabled={!draft || busy}
                  value={inputs.category}
                  onChange={(e) => change("category", e.target.value)}
                >
                  <option value="">Seleccionar categoría</option>
                  {leaves.map((c) => (
                    <option key={c.fmg_categoryid} value={c.fmg_categoryid}>
                      {c.fmg_code} · {c.fmg_name}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Cuenta de origen">
                <select
                  aria-label="Cuenta de origen"
                  className={css.select}
                  disabled={!draft || busy}
                  value={inputs.account}
                  onChange={(e) => change("account", e.target.value)}
                >
                  <option value="">Seleccionar cuenta</option>
                  {accounts.map((a) => (
                    <option key={a.fmg_cashaccountid} value={a.fmg_cashaccountid}>
                      {a.fmg_name}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Referencia de documento">
                <Input
                  disabled={!draft || busy}
                  maxLength={100}
                  value={inputs.reference}
                  onChange={(_, v) => change("reference", v.value)}
                />
              </Field>
              <Field label="Descripción" className={css.wide}>
                <Textarea
                  disabled={!draft || busy}
                  value={inputs.description}
                  onChange={(_, v) => change("description", v.value)}
                  rows={3}
                />
              </Field>
            </div>
          </Card>
          <div className={css.toolbar}>
            {draft && (
              <>
                <Button
                  appearance="secondary"
                  icon={<SaveRegular />}
                  onClick={() => {
                    void saveDraft();
                  }}
                  disabled={busy}
                >
                  Guardar borrador
                </Button>
                {!pendingPost && (
                  <Button
                    appearance="primary"
                    icon={<CheckmarkRegular />}
                    onClick={() => setPendingPost(true)}
                    disabled={busy}
                  >
                    Confirmar pago
                  </Button>
                )}
              </>
            )}
            {data.record.fmg_status === 797280001 && !pendingVoid && (
              <Button
                icon={<ShieldLockRegular />}
                onClick={() => setPendingVoid(true)}
                disabled={busy}
              >
                Solicitar anulación
              </Button>
            )}
          </div>
          {pendingPost && (
            <Card className={css.panel}>
              <Text weight="semibold">¿Confirmar este pago?</Text>
              <Text size={200}>
                Al confirmar, los datos financieros quedarán protegidos. Solo usuarios autorizados
                por Dataverse pueden hacerlo.
              </Text>
              <div className={css.toolbar}>
                <Button
                  appearance="primary"
                  disabled={busy}
                  onClick={() => {
                    void post();
                  }}
                >
                  Sí, confirmar
                </Button>
                <Button disabled={busy} onClick={() => setPendingPost(false)}>
                  Cancelar
                </Button>
              </div>
            </Card>
          )}
          {pendingVoid && (
            <Card className={css.panel}>
              <Field label="Motivo obligatorio de anulación">
                <Textarea
                  disabled={busy}
                  value={inputs.voidReason}
                  onChange={(_, v) => change("voidReason", v.value)}
                  rows={3}
                />
              </Field>
              <div className={css.toolbar}>
                <Button
                  appearance="primary"
                  disabled={busy || !inputs.voidReason.trim()}
                  onClick={() => {
                    void voidPayment();
                  }}
                >
                  Anular movimiento
                </Button>
                <Button disabled={busy} onClick={() => setPendingVoid(false)}>
                  Cancelar
                </Button>
              </div>
            </Card>
          )}
          {feedback && (
            <Text
              role="status"
              className={feedback.toLowerCase().includes("correctamente") ? "" : css.error}
            >
              {feedback}
            </Text>
          )}
        </>
      )}
    </main>
  );
};
export default ExpenseDetail;
