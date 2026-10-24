import React, { useEffect, useMemo, useState } from "react";
import {
  Badge,
  Button,
  Card,
  CardHeader,
  Input,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  ArrowClockwiseRegular,
  OpenRegular,
  MoneyRegular,
  CalendarRegular,
  ShieldLockRegular,
} from "@fluentui/react-icons";
import type {
  GeneratedComponentProps,
  fmg_budget,
  fmg_budgetallocation,
  fmg_branch,
  fmg_category,
  transactioncurrency,
} from "./RuntimeTypes";

type B = fmg_budget;
type A = fmg_budgetallocation;
type State = {
  loading: boolean;
  budgets: B[];
  allocations: A[];
  branches: fmg_branch[];
  categories: fmg_category[];
  currencies: transactioncurrency[];
  error: string;
  limited: boolean;
};
const cache = "__financialManagerBudgetPage2026v7";
const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1,
    minHeight: "100%",
    boxSizing: "border-box",
  },
  heading: { fontSize: tokens.fontSizeHero700, fontWeight: tokens.fontWeightSemibold },
  top: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    flexWrap: "wrap",
    gap: tokens.spacingHorizontalM,
  },
  description: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase300 },
  cards: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
    gap: tokens.spacingHorizontalM,
  },
  big: { fontSize: tokens.fontSizeHero600, fontWeight: tokens.fontWeightSemibold },
  toolbar: {
    display: "flex",
    gap: tokens.spacingHorizontalM,
    flexWrap: "wrap",
    alignItems: "center",
  },
  panel: {
    backgroundColor: tokens.colorNeutralBackground1,
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
  },
  formGrid: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fit,minmax(170px,1fr))",
    gap: tokens.spacingHorizontalM,
    alignItems: "end",
  },
  formField: { display: "flex", flexDirection: "column", gap: tokens.spacingVerticalXS },
  actions: { display: "flex", gap: tokens.spacingHorizontalS, marginTop: tokens.spacingVerticalM },
  formControl: {
    minHeight: "34px",
    padding: "6px",
    border: "1px solid " + tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  tableContainer: { overflowX: "auto", backgroundColor: tokens.colorNeutralBackground1 },
  table: { borderCollapse: "collapse", minWidth: "710px", width: "100%" },
  cell: {
    padding: tokens.spacingHorizontalM,
    borderBottom: "1px solid " + tokens.colorNeutralStroke2,
    textAlign: "left",
  },
  th: { fontWeight: tokens.fontWeightSemibold, backgroundColor: tokens.colorNeutralBackground3 },
  info: { padding: tokens.spacingHorizontalM, backgroundColor: tokens.colorNeutralBackground1 },
  error: { color: tokens.colorPaletteRedForeground1 },
});
function active(value: unknown): boolean {
  return value === true || value === 1;
}
function idFromLookup(value?: string): string {
  if (!value) return "";
  const valueMatch = value.match(/[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}/i);
  return valueMatch ? valueMatch[0].toLowerCase() : "";
}
function displayAmount(value: number): string {
  return (
    "S/ " + value.toLocaleString("es-PE", { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  );
}
function displayStatus(value: number): string {
  const map: Record<number, string> = {
    797280400: "Borrador",
    797280401: "Presentado",
    797280402: "Aprobado",
    797280403: "Rechazado",
    797280404: "Sustituido",
  };
  return map[value] || "Sin estado";
}
async function navigateBudget(id?: string): Promise<void> {
  const xrm = (window as any).Xrm;
  if (!xrm?.Navigation?.navigateTo)
    throw new Error("No se puede abrir el formulario de presupuestos.");
  const pageInput = id
    ? { pageType: "entityrecord", entityName: "fmg_budget", entityId: id }
    : { pageType: "entityrecord", entityName: "fmg_budget" };
  await xrm.Navigation.navigateTo(pageInput, {
    target: 2,
    position: 1,
    width: { value: 80, unit: "%" },
    height: { value: 85, unit: "%" },
  });
}
const BudgetPage = ({ dataApi }: GeneratedComponentProps) => {
  const classes = useStyles();
  const [state, setState] = useState<State>(
    () =>
      (window as any)[cache] ?? {
        loading: true,
        budgets: [],
        allocations: [],
        branches: [],
        categories: [],
        currencies: [],
        error: "",
        limited: false,
      },
  );
  const [reload, setReload] = useState(0);
  const [year, setYear] = useState("all");
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("all");
  const [navigationError, setNavigationError] = useState("");
  const [draftOpen, setDraftOpen] = useState(false);
  const [editingBudgetId, setEditingBudgetId] = useState("");
  const [draftName, setDraftName] = useState("");
  const [draftYear, setDraftYear] = useState(String(new Date().getFullYear()));
  const [draftVersion, setDraftVersion] = useState("1");
  const [draftMonth, setDraftMonth] = useState("1");
  const [draftScope, setDraftScope] = useState("797280301");
  const [draftBranch, setDraftBranch] = useState("");
  const [draftSaving, setDraftSaving] = useState(false);
  const [draftFeedback, setDraftFeedback] = useState("");
  const [allocationOpen, setAllocationOpen] = useState(false);
  const [allocationBudget, setAllocationBudget] = useState("");
  const [allocationCategory, setAllocationCategory] = useState("");
  const [allocationMonth, setAllocationMonth] = useState("1");
  const [allocationAmount, setAllocationAmount] = useState("");
  const [allocationSaving, setAllocationSaving] = useState(false);
  const [allocationFeedback, setAllocationFeedback] = useState("");
  const [allocationEditing, setAllocationEditing] = useState("");
  const [reviewBudgetId, setReviewBudgetId] = useState("");
  const [submitCandidate, setSubmitCandidate] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [submissionFeedback, setSubmissionFeedback] = useState("");
  const ready = !!dataApi;
  useEffect(() => {
    if (!ready) return;
    let active = true;
    const shared = window as any;
    if (reload === 0 && shared[cache]) {
      setState(shared[cache]);
      return;
    }
    setState((s) => ({ ...s, loading: true, error: "" }));
    const key = cache + "_inflight";
    if (!shared[key])
      shared[key] = Promise.all([
        dataApi.queryTable("fmg_budget", {
          select: [
            "fmg_budgetid",
            "fmg_name",
            "fmg_year",
            "fmg_version",
            "fmg_status",
            "fmg_scope",
            "fmg_effectivefrommonth",
            "_fmg_branch_value",
          ],
          orderBy: "fmg_year desc",
          pageSize: 200,
        }),
        dataApi.queryTable("fmg_budgetallocation", {
          select: [
            "fmg_budgetallocationid",
            "_fmg_budget_value",
            "fmg_month",
            "fmg_amount",
            "_fmg_category_value",
          ],
          pageSize: 1000,
        }),
        dataApi.queryTable("fmg_branch", {
          select: ["fmg_branchid", "fmg_name", "fmg_isactive"],
          pageSize: 100,
        }),
        dataApi.queryTable("fmg_category", {
          select: ["fmg_categoryid", "fmg_code", "fmg_name", "fmg_isactive", "fmg_isgroup"],
          pageSize: 100,
        }),
        dataApi.queryTable("transactioncurrency", {
          select: ["transactioncurrencyid", "isocurrencycode"],
          pageSize: 100,
        }),
      ])
        .then(([b, a, branches, categories, currencies]) => {
          const next: State = {
            loading: false,
            budgets: b.rows,
            allocations: a.rows,
            branches: branches.rows,
            categories: categories.rows,
            currencies: currencies.rows,
            error: "",
            limited:
              b.hasMoreRows ||
              a.hasMoreRows ||
              branches.hasMoreRows ||
              categories.hasMoreRows ||
              currencies.hasMoreRows,
          };
          shared[cache] = next;
          return next;
        })
        .catch((err: unknown) => {
          const next: State = {
            loading: false,
            budgets: [],
            allocations: [],
            branches: [],
            categories: [],
            currencies: [],
            limited: false,
            error: err instanceof Error ? err.message : "No se pudieron cargar los presupuestos.",
          };
          shared[cache] = next;
          return next;
        })
        .finally(() => {
          shared[key] = null;
        });
    (shared[key] as Promise<State>).then((result) => {
      if (active) setState(result);
    });
    return () => {
      active = false;
    };
  }, [ready, reload]);
  const counts = useMemo(() => {
    const sums = new Map<string, number>();
    for (const row of state.allocations) {
      const id = idFromLookup(row._fmg_budget_value as unknown as string);
      sums.set(id, (sums.get(id) || 0) + (Number(row.fmg_amount) || 0));
    }
    return sums;
  }, [state.allocations]);
  const visible = useMemo(
    () =>
      state.budgets.filter((row) => {
        return (
          (year === "all" || String(row.fmg_year) === year) &&
          (filter === "all" || String(row.fmg_status) === filter) &&
          (!search.trim() ||
            String(row.fmg_name || "")
              .toLowerCase()
              .includes(search.trim().toLowerCase()))
        );
      }),
    [state.budgets, year, filter, search],
  );
  const years = Array.from(new Set(state.budgets.map((row) => String(row.fmg_year))))
    .sort()
    .reverse();
  const submitted = state.budgets.filter((row) => row.fmg_status === 797280401).length;
  function refresh(): void {
    (window as any)[cache] = null;
    (window as any)[cache + "_inflight"] = null;
    setReload((v) => v + 1);
  }
  const editingBudget = state.budgets.find((b) => b.fmg_budgetid === editingBudgetId);
  const editingHasAllocations =
    !!editingBudget &&
    state.allocations.some(
      (a) =>
        idFromLookup(a._fmg_budget_value as unknown as string) ===
        editingBudget.fmg_budgetid.toLowerCase(),
    );
  function resetBudgetForm(): void {
    setEditingBudgetId("");
    setDraftName("");
    setDraftYear(String(new Date().getFullYear()));
    setDraftVersion("1");
    setDraftMonth("1");
    setDraftScope("797280301");
    setDraftBranch("");
    setDraftFeedback("");
  }
  function editBudget(row: B): void {
    if (row.fmg_status !== 797280400) {
      setDraftFeedback("Solo se pueden editar presupuestos en borrador.");
      return;
    }
    setEditingBudgetId(row.fmg_budgetid);
    setDraftName(row.fmg_name || "");
    setDraftYear(String(row.fmg_year));
    setDraftVersion(String(row.fmg_version));
    setDraftMonth(String(row.fmg_effectivefrommonth));
    setDraftScope(String(row.fmg_scope));
    setDraftBranch(idFromLookup(row._fmg_branch_value as unknown as string));
    setDraftFeedback("");
    setAllocationOpen(false);
    setAllocationEditing("");
    setSubmitCandidate("");
    setDraftOpen(true);
  }
  async function saveBudgetDraft(): Promise<void> {
    if (draftSaving) return;
    const year = Number(draftYear),
      version = Number(draftVersion),
      month = Number(draftMonth);
    if (
      !draftName.trim() ||
      !Number.isInteger(year) ||
      year < 2020 ||
      year > 2100 ||
      !Number.isInteger(version) ||
      version < 1 ||
      !Number.isInteger(month) ||
      month < 1 ||
      month > 12 ||
      (draftScope === "797280300" && !draftBranch)
    ) {
      setDraftFeedback("Indica nombre, año, versión, mes efectivo y sede cuando corresponda.");
      return;
    }
    setDraftSaving(true);
    setDraftFeedback("");
    try {
      const identity: Record<string, unknown> = {
        fmg_year: year,
        fmg_version: version,
        fmg_effectivefrommonth: month,
        fmg_scope: Number(draftScope),
        _fmg_branch_value: draftScope === "797280300" ? "/fmg_branch(" + draftBranch + ")" : null,
      };
      if (editingBudgetId) {
        if (!editingBudget || editingBudget.fmg_status !== 797280400) {
          throw new Error("El presupuesto ya no está disponible para edición.");
        }
        // Once allocations exist, identity fields are frozen in this editor.
        await dataApi.updateRow("fmg_budget", editingBudgetId, {
          fmg_name: draftName.trim(),
          ...(editingHasAllocations ? {} : identity),
        } as any);
      } else {
        await dataApi.createRow("fmg_budget", {
          fmg_name: draftName.trim(),
          fmg_status: 797280400,
          ...identity,
        } as any);
      }
      const wasEditing = !!editingBudgetId;
      setDraftOpen(false);
      resetBudgetForm();
      setDraftFeedback(
        wasEditing ? "Presupuesto borrador actualizado." : "Presupuesto borrador creado.",
      );
      refresh();
    } catch (err) {
      setDraftFeedback(err instanceof Error ? err.message : "No se pudo crear el presupuesto.");
    } finally {
      setDraftSaving(false);
    }
  }
  function editAllocation(row: A): void {
    const budgetId = idFromLookup(row._fmg_budget_value as unknown as string);
    const budget = state.budgets.find((b) => b.fmg_budgetid.toLowerCase() === budgetId);
    if (!budget || budget.fmg_status !== 797280400) {
      setAllocationFeedback("Solo es posible modificar asignaciones de presupuestos en borrador.");
      return;
    }
    setAllocationEditing(row.fmg_budgetallocationid);
    setAllocationBudget(budget.fmg_budgetid);
    setAllocationCategory(idFromLookup(row._fmg_category_value as unknown as string));
    setAllocationMonth(String(row.fmg_month));
    setAllocationAmount(String(row.fmg_amount));
    setAllocationFeedback("");
    setDraftOpen(false);
    setAllocationOpen(true);
  }
  function newAllocation(): void {
    setAllocationEditing("");
    setAllocationCategory("");
    setAllocationAmount("");
    setAllocationBudget(
      reviewBudgetId &&
        state.budgets.some((b) => b.fmg_budgetid === reviewBudgetId && b.fmg_status === 797280400)
        ? reviewBudgetId
        : "",
    );
    setAllocationOpen(true);
    setDraftOpen(false);
    setAllocationFeedback("");
  }
  async function saveAllocation(): Promise<void> {
    if (allocationSaving) return;
    const budget = state.budgets.find(
      (b) => b.fmg_budgetid === allocationBudget && b.fmg_status === 797280400,
    );
    const category = state.categories.find(
      (c) =>
        c.fmg_categoryid === allocationCategory &&
        active(c.fmg_isactive) &&
        !active(c.fmg_isgroup) &&
        c.fmg_code !== "999",
    );
    const currency = state.currencies.find((c) => c.isocurrencycode === "PEN");
    const original = state.allocations.find((a) => a.fmg_budgetallocationid === allocationEditing);
    const month = Number(allocationMonth),
      amount = Number(allocationAmount);
    if (
      !budget ||
      !category ||
      !currency ||
      !Number.isInteger(month) ||
      month < 1 ||
      month > 12 ||
      !/^\d+(?:\.\d{1,2})?$/.test(allocationAmount.trim()) ||
      !Number.isFinite(amount) ||
      amount < 0 ||
      (allocationEditing &&
        (!original ||
          idFromLookup(original._fmg_budget_value as unknown as string) !==
            budget.fmg_budgetid.toLowerCase()))
    ) {
      setAllocationFeedback(
        "Revisa el presupuesto borrador, la categoría y el importe PEN (máximo dos decimales).",
      );
      return;
    }
    setAllocationSaving(true);
    setAllocationFeedback("");
    try {
      if (allocationEditing) {
        await dataApi.updateRow("fmg_budgetallocation", allocationEditing, { fmg_amount: amount });
      } else {
        await dataApi.createRow("fmg_budgetallocation", {
          fmg_name:
            budget.fmg_name + " · " + category.fmg_code + " · " + String(month).padStart(2, "0"),
          fmg_month: month,
          fmg_amount: amount,
          _fmg_budget_value: "/fmg_budget(" + budget.fmg_budgetid + ")",
          _fmg_category_value: "/fmg_category(" + category.fmg_categoryid + ")",
          _transactioncurrencyid_value:
            "/transactioncurrency(" + currency.transactioncurrencyid + ")",
        } as any);
      }
      setAllocationOpen(false);
      setAllocationAmount("");
      setAllocationCategory("");
      setAllocationEditing("");
      setReviewBudgetId(budget.fmg_budgetid);
      setAllocationFeedback(
        allocationEditing
          ? "Importe actualizado."
          : "Asignación creada en el presupuesto borrador.",
      );
      refresh();
    } catch (err) {
      setAllocationFeedback(err instanceof Error ? err.message : "No se pudo crear la asignación.");
    } finally {
      setAllocationSaving(false);
    }
  }
  async function submitDraftBudget(): Promise<void> {
    if (submitting || !submitCandidate) return;
    const budget = state.budgets.find((b) => b.fmg_budgetid === submitCandidate);
    const hasLines = state.allocations.some(
      (a) =>
        idFromLookup(a._fmg_budget_value as unknown as string) === submitCandidate.toLowerCase(),
    );
    if (!budget || budget.fmg_status !== 797280400 || !hasLines) {
      setSubmissionFeedback("Solo puede presentarse un presupuesto borrador con asignaciones.");
      return;
    }
    setSubmitting(true);
    setSubmissionFeedback("");
    try {
      await dataApi.updateRow("fmg_budget", submitCandidate, { fmg_status: 797280401 });
      setSubmitCandidate("");
      setAllocationOpen(false);
      setAllocationEditing("");
      setSubmissionFeedback("Presupuesto presentado. Sus asignaciones ahora son de solo lectura.");
      refresh();
    } catch (err) {
      setSubmissionFeedback(
        err instanceof Error ? err.message : "No se pudo presentar el presupuesto.",
      );
    } finally {
      setSubmitting(false);
    }
  }
  async function openBudget(id?: string): Promise<void> {
    setNavigationError("");
    try {
      await navigateBudget(id);
      refresh();
    } catch (err) {
      setNavigationError(
        err instanceof Error ? err.message : "No se pudo abrir el formulario de presupuesto.",
      );
    }
  }
  return (
    <main className={classes.root} aria-label="Gestión de presupuestos">
      <header className={classes.top}>
        <div>
          <Text as="h1" className={classes.heading}>
            Presupuestos
          </Text>
          <p className={classes.description}>
            Planificación anual y asignaciones por categoría y mes · PEN
          </p>
        </div>
        <div className={classes.toolbar}>
          <Button
            appearance="primary"
            onClick={() => {
              if (draftOpen) {
                setDraftOpen(false);
                resetBudgetForm();
              } else {
                resetBudgetForm();
                setDraftOpen(true);
              }
              setAllocationOpen(false);
            }}
          >
            {draftOpen ? "Cerrar formulario" : "Nuevo presupuesto"}
          </Button>
          <Button
            appearance="secondary"
            disabled={!state.budgets.some((b) => b.fmg_status === 797280400) || state.loading}
            onClick={() => {
              if (allocationOpen) {
                setAllocationOpen(false);
                setAllocationEditing("");
              } else newAllocation();
            }}
          >
            {allocationOpen ? "Cerrar asignación" : "Nueva asignación"}
          </Button>
          <Button icon={<ArrowClockwiseRegular />} onClick={refresh} disabled={state.loading}>
            Actualizar
          </Button>
        </div>
      </header>
      {navigationError && (
        <Text role="alert" className={classes.error}>
          {navigationError}
        </Text>
      )}
      {submissionFeedback && <Text role="status">{submissionFeedback}</Text>}
      {submitCandidate && (
        <section className={classes.panel} aria-label="Confirmar presentación del presupuesto">
          <Text weight="semibold">Presentar presupuesto</Text>
          <Text size={200}>
            Al presentarlo, el presupuesto y sus asignaciones dejarán de ser editables. La
            aprobación sigue restringida.
          </Text>
          <div className={classes.actions}>
            <Button
              appearance="primary"
              disabled={submitting}
              onClick={() => {
                void submitDraftBudget();
              }}
            >
              {submitting ? "Presentando..." : "Confirmar presentación"}
            </Button>
            <Button
              appearance="secondary"
              disabled={submitting}
              onClick={() => setSubmitCandidate("")}
            >
              Cancelar
            </Button>
          </div>
        </section>
      )}
      {draftFeedback && <Text role="status">{draftFeedback}</Text>}
      {draftOpen && (
        <section className={classes.panel} aria-label="Editar presupuesto borrador">
          <Text weight="semibold">
            {editingBudgetId ? "Editar presupuesto borrador" : "Nuevo presupuesto"}
          </Text>
          {editingHasAllocations && (
            <Text size={200}>
              Este presupuesto ya tiene asignaciones: solo puedes cambiar su nombre. Año, sede,
              versión y vigencia permanecen fijos.
            </Text>
          )}
          <div className={classes.formGrid}>
            <label className={classes.formField}>
              Nombre
              <Input
                aria-label="Nombre del presupuesto"
                value={draftName}
                onChange={(_, v) => setDraftName(v.value)}
                placeholder="Ej. Plan anual 2027"
              />
            </label>
            <label className={classes.formField}>
              Año
              <Input
                aria-label="Año del presupuesto"
                type="number"
                disabled={editingHasAllocations}
                value={draftYear}
                onChange={(_, v) => setDraftYear(v.value)}
              />
            </label>
            <label className={classes.formField}>
              Versión
              <Input
                aria-label="Versión del presupuesto"
                type="number"
                disabled={editingHasAllocations}
                value={draftVersion}
                onChange={(_, v) => setDraftVersion(v.value)}
              />
            </label>
            <label className={classes.formField}>
              Desde el mes
              <select
                className={classes.formControl}
                disabled={editingHasAllocations}
                value={draftMonth}
                onChange={(e) => setDraftMonth(e.target.value)}
                aria-label="Mes inicial de vigencia"
              >
                {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                  <option key={m} value={String(m)}>
                    {m}
                  </option>
                ))}
              </select>
            </label>
            <label className={classes.formField}>
              Ámbito
              <select
                className={classes.formControl}
                disabled={editingHasAllocations}
                value={draftScope}
                onChange={(e) => {
                  setDraftScope(e.target.value);
                  setDraftBranch("");
                }}
                aria-label="Ámbito del presupuesto"
              >
                <option value="797280301">Compartido</option>
                <option value="797280300">Por sede</option>
              </select>
            </label>
            {draftScope === "797280300" && (
              <label className={classes.formField}>
                Sede
                <select
                  className={classes.formControl}
                  disabled={editingHasAllocations}
                  value={draftBranch}
                  onChange={(e) => setDraftBranch(e.target.value)}
                  aria-label="Sede del presupuesto"
                >
                  <option value="">Seleccionar sede</option>
                  {state.branches
                    .filter((b) => active(b.fmg_isactive))
                    .map((b) => (
                      <option key={b.fmg_branchid} value={b.fmg_branchid}>
                        {b.fmg_name}
                      </option>
                    ))}
                </select>
              </label>
            )}
          </div>
          <div className={classes.actions}>
            <Button
              appearance="primary"
              disabled={draftSaving || state.loading}
              onClick={() => {
                void saveBudgetDraft();
              }}
            >
              {draftSaving
                ? "Guardando..."
                : editingBudgetId
                  ? "Guardar cambios"
                  : "Guardar borrador"}
            </Button>
            <Button
              appearance="secondary"
              disabled={draftSaving}
              onClick={() => {
                setDraftOpen(false);
                resetBudgetForm();
              }}
            >
              Cancelar
            </Button>
          </div>
        </section>
      )}
      {allocationFeedback && <Text role="status">{allocationFeedback}</Text>}
      {allocationOpen && (
        <section className={classes.panel} aria-label="Crear asignación mensual">
          <Text weight="semibold">
            {allocationEditing ? "Editar importe mensual" : "Nueva asignación mensual"}
          </Text>
          <div className={classes.formGrid}>
            <label className={classes.formField}>
              Presupuesto borrador
              <select
                className={classes.formControl}
                disabled={!!allocationEditing}
                value={allocationBudget}
                onChange={(e) => setAllocationBudget(e.target.value)}
                aria-label="Presupuesto de la asignación"
              >
                <option value="">Seleccionar presupuesto</option>
                {state.budgets
                  .filter((b) => b.fmg_status === 797280400)
                  .map((b) => (
                    <option key={b.fmg_budgetid} value={b.fmg_budgetid}>
                      {b.fmg_name} · {b.fmg_year} · v{b.fmg_version}
                    </option>
                  ))}
              </select>
            </label>
            <label className={classes.formField}>
              Categoría de detalle
              <select
                className={classes.formControl}
                disabled={!!allocationEditing}
                value={allocationCategory}
                onChange={(e) => setAllocationCategory(e.target.value)}
                aria-label="Categoría de la asignación"
              >
                <option value="">Seleccionar categoría</option>
                {state.categories
                  .filter(
                    (c) => active(c.fmg_isactive) && !active(c.fmg_isgroup) && c.fmg_code !== "999",
                  )
                  .map((c) => (
                    <option key={c.fmg_categoryid} value={c.fmg_categoryid}>
                      {c.fmg_code} · {c.fmg_name}
                    </option>
                  ))}
              </select>
            </label>
            <label className={classes.formField}>
              Mes
              <select
                className={classes.formControl}
                disabled={!!allocationEditing}
                value={allocationMonth}
                onChange={(e) => setAllocationMonth(e.target.value)}
                aria-label="Mes de asignación"
              >
                {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                  <option key={m} value={String(m)}>
                    {m}
                  </option>
                ))}
              </select>
            </label>
            <label className={classes.formField}>
              Importe (PEN)
              <Input
                aria-label="Importe de la asignación"
                type="number"
                min="0"
                step="0.01"
                value={allocationAmount}
                onChange={(_, v) => setAllocationAmount(v.value)}
                placeholder="0.00"
              />
            </label>
          </div>
          <div className={classes.actions}>
            <Button
              appearance="primary"
              disabled={allocationSaving || state.loading}
              onClick={() => {
                void saveAllocation();
              }}
            >
              {allocationSaving
                ? "Guardando..."
                : allocationEditing
                  ? "Guardar importe"
                  : "Guardar asignación"}
            </Button>
            <Button
              appearance="secondary"
              disabled={allocationSaving}
              onClick={() => {
                setAllocationOpen(false);
                setAllocationEditing("");
              }}
            >
              Cancelar
            </Button>
          </div>
        </section>
      )}
      <section className={classes.cards} aria-label="Resumen de presupuestos">
        <Card>
          <CardHeader
            image={<CalendarRegular />}
            header={<Text weight="semibold">Versiones visibles</Text>}
          />
          <Text className={classes.big}>{state.budgets.length}</Text>
        </Card>
        <Card>
          <CardHeader
            image={<MoneyRegular />}
            header={<Text weight="semibold">Asignaciones consultadas</Text>}
          />
          <Text className={classes.big}>{state.allocations.length}</Text>
        </Card>
        <Card>
          <CardHeader
            image={<ShieldLockRegular />}
            header={<Text weight="semibold">Presentados</Text>}
          />
          <Text className={classes.big}>{submitted}</Text>
        </Card>
      </section>
      <Card className={classes.info}>
        <Text weight="semibold">Aprobación temporalmente deshabilitada</Text>
        <Text size={200}>
          Los presupuestos se pueden preparar y presentar según permisos. La transición a Aprobado
          está bloqueada por Dataverse hasta completar la separación de funciones y el control de
          versiones concurrentes. No hay todavía presupuesto aprobado válido para calcular
          diferencias frente a ejecución.
        </Text>
      </Card>
      <section className={classes.panel}>
        <div className={classes.toolbar}>
          <Input
            aria-label="Buscar presupuesto por nombre"
            placeholder="Buscar presupuesto"
            value={search}
            onChange={(_, data) => setSearch(data.value)}
          />
          <label htmlFor="fmBudgetYear">Año </label>
          <select id="fmBudgetYear" value={year} onChange={(e) => setYear(e.target.value)}>
            <option value="all">Todos</option>
            {years.map((y) => (
              <option key={y} value={y}>
                {y}
              </option>
            ))}
          </select>
          <label htmlFor="fmBudgetStatus">Estado </label>
          <select id="fmBudgetStatus" value={filter} onChange={(e) => setFilter(e.target.value)}>
            <option value="all">Todos</option>
            <option value="797280400">Borrador</option>
            <option value="797280401">Presentado</option>
            <option value="797280402">Aprobado</option>
            <option value="797280403">Rechazado</option>
            <option value="797280404">Sustituido</option>
          </select>
        </div>
      </section>
      {state.loading && <Spinner label="Cargando presupuesto y asignaciones..." />}
      {state.error && (
        <Text role="alert" className={classes.error}>
          {state.error}
        </Text>
      )}
      {!state.loading && !state.error && visible.length === 0 && (
        <div className={classes.info}>
          <Text>No hay presupuestos para los filtros seleccionados.</Text>
        </div>
      )}
      {!state.loading && !state.error && visible.length > 0 && (
        <div className={classes.tableContainer}>
          <table className={classes.table}>
            <thead>
              <tr>
                {[
                  "Presupuesto",
                  "Año",
                  "Ámbito",
                  "Versión",
                  "Desde mes",
                  "Estado",
                  "Asignado (consultado)",
                  "Detalle",
                ].map((t) => (
                  <th key={t} className={classes.cell + " " + classes.th}>
                    {t}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {visible.map((row) => (
                <tr key={row.fmg_budgetid}>
                  <td className={classes.cell}>{row.fmg_name}</td>
                  <td className={classes.cell}>{row.fmg_year}</td>
                  <td className={classes.cell}>
                    {row.fmg_scope === 797280301 ? "Compartido" : "Por sede"}
                  </td>
                  <td className={classes.cell}>{row.fmg_version}</td>
                  <td className={classes.cell}>{row.fmg_effectivefrommonth}</td>
                  <td className={classes.cell}>
                    <Badge appearance="outline">{displayStatus(row.fmg_status)}</Badge>
                  </td>
                  <td className={classes.cell}>
                    {displayAmount(counts.get(String(row.fmg_budgetid).toLowerCase()) || 0)}
                  </td>
                  <td className={classes.cell}>
                    <Button
                      appearance="subtle"
                      onClick={() => {
                        setReviewBudgetId(row.fmg_budgetid);
                        setAllocationOpen(false);
                        setAllocationEditing("");
                      }}
                    >
                      Asignaciones
                    </Button>
                    {row.fmg_status === 797280400 && (
                      <Button appearance="subtle" onClick={() => editBudget(row)}>
                        Editar
                      </Button>
                    )}
                    {row.fmg_status === 797280400 && (
                      <Button
                        appearance="subtle"
                        disabled={
                          !state.allocations.some(
                            (a) =>
                              idFromLookup(a._fmg_budget_value as unknown as string) ===
                              row.fmg_budgetid.toLowerCase(),
                          )
                        }
                        onClick={() => {
                          setSubmitCandidate(row.fmg_budgetid);
                          setSubmissionFeedback("");
                        }}
                      >
                        Presentar
                      </Button>
                    )}
                    <Button
                      appearance="subtle"
                      icon={<OpenRegular />}
                      onClick={() => {
                        void openBudget(row.fmg_budgetid);
                      }}
                    >
                      Abrir
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {!state.loading &&
        !state.error &&
        reviewBudgetId &&
        (() => {
          const budget = state.budgets.find((b) => b.fmg_budgetid === reviewBudgetId);
          if (!budget) return null;
          const lines = state.allocations
            .filter(
              (a) =>
                idFromLookup(a._fmg_budget_value as unknown as string) ===
                reviewBudgetId.toLowerCase(),
            )
            .sort((a, b) => a.fmg_month - b.fmg_month);
          return (
            <section className={classes.panel} aria-label="Asignaciones del presupuesto">
              <div className={classes.top}>
                <div>
                  <Text weight="semibold">Asignaciones · {budget.fmg_name}</Text>
                  <Text size={200}>
                    Total consultado: {displayAmount(counts.get(reviewBudgetId.toLowerCase()) || 0)}{" "}
                    · {lines.length} líneas
                  </Text>
                </div>
                <Button onClick={() => setReviewBudgetId("")}>Cerrar</Button>
              </div>
              {lines.length === 0 ? (
                <Text size={200}>Este presupuesto todavía no tiene asignaciones.</Text>
              ) : (
                <div className={classes.tableContainer}>
                  <table className={classes.table}>
                    <thead>
                      <tr>
                        {["Mes", "Categoría", "Importe (PEN)", "Acción"].map((label) => (
                          <th className={classes.cell + " " + classes.th} key={label}>
                            {label}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {lines.map((line) => {
                        const category = state.categories.find(
                          (c) =>
                            c.fmg_categoryid.toLowerCase() ===
                            idFromLookup(line._fmg_category_value as unknown as string),
                        );
                        return (
                          <tr key={line.fmg_budgetallocationid}>
                            <td className={classes.cell}>{line.fmg_month}</td>
                            <td className={classes.cell}>
                              {category
                                ? category.fmg_code + " · " + category.fmg_name
                                : "Categoría no disponible"}
                            </td>
                            <td className={classes.cell}>
                              {displayAmount(Number(line.fmg_amount) || 0)}
                            </td>
                            <td className={classes.cell}>
                              {budget.fmg_status === 797280400 ? (
                                <Button appearance="subtle" onClick={() => editAllocation(line)}>
                                  Editar importe
                                </Button>
                              ) : (
                                <Text size={200}>Solo lectura</Text>
                              )}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          );
        })()}
      {state.limited && (
        <Text size={200}>
          Listado parcial por paginación. Use la vista nativa para consultar el conjunto completo de
          registros.
        </Text>
      )}
    </main>
  );
};
export default BudgetPage;
