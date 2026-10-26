import { createContext, useContext, useMemo, useReducer } from "react";
import type { ReactNode } from "react";

// UI-only session store: NO browser storage, endpoint, token or persisted financial changes.
export type DraftFields = {
  amount: string;
  date: string;
  category: string;
  scope: "Por sede" | "Compartido";
  branch: string;
  account: string;
  flow: string;
  description: string;
};
export type ExpenseDraft = DraftFields & { id: string; modifiedAt: string };
export type BudgetLine = { id: string; category: string; month: number; amount: number };
export type BudgetDraft = {
  id: string;
  name: string;
  year: number;
  scope: string;
  branch: string;
  lines: BudgetLine[];
  modifiedAt: string;
};
export type SessionEvent = {
  sequence: number;
  at: string;
  action: "Creación" | "Edición" | "Descarte";
  object: "Movimiento" | "Presupuesto";
  reference: string;
  summary: string;
};
type WorkspaceSession = {
  expenses: ExpenseDraft[];
  budgets: BudgetDraft[];
  events: SessionEvent[];
  counter: number;
};
type Command =
  | { type: "saveExpense"; draft: DraftFields; id?: string }
  | { type: "deleteExpense"; id: string }
  | { type: "saveBudget"; plan: Omit<BudgetDraft, "id" | "modifiedAt">; id?: string }
  | { type: "deleteBudget"; id: string };
const initial: WorkspaceSession = { expenses: [], budgets: [], events: [], counter: 1 };
export function cleanAmount(value: string): number | null {
  const text = value.trim().replace(",", ".");
  return /^\d+(?:\.\d{1,2})?$/.test(text) && Number(text) > 0 ? Number(text) : null;
}
function reduce(state: WorkspaceSession, command: Command): WorkspaceSession {
  const at = new Date().toISOString();
  const isExpense = command.type === "saveExpense" || command.type === "deleteExpense";
  const type = command.type.startsWith("save") ? "Edición" : "Descarte";
  const editing = "id" in command && Boolean(command.id);
  const reference =
    command.type === "saveExpense"
      ? (command.id ?? `D-${String(state.counter).padStart(4, "0")}`)
      : command.type === "saveBudget"
        ? (command.id ?? `P-${String(state.counter).padStart(4, "0")}`)
        : command.id;
  const entity = isExpense ? "Movimiento" : "Presupuesto";
  const newEvent: SessionEvent = {
    sequence: state.events.length + 1,
    at,
    action: !editing && type === "Edición" ? "Creación" : type,
    object: entity,
    reference,
    summary:
      type === "Descarte"
        ? "Borrador descartado"
        : editing
          ? "Cambios guardados"
          : "Borrador creado",
  };
  if (command.type === "saveExpense") {
    const draft = { ...command.draft, id: reference, modifiedAt: at };
    return {
      ...state,
      expenses: [draft, ...state.expenses.filter((x) => x.id !== reference)],
      counter: editing ? state.counter : state.counter + 1,
      events: [newEvent, ...state.events],
    };
  }
  if (command.type === "deleteExpense") {
    if (!state.expenses.some((x) => x.id === reference)) return state;
    return {
      ...state,
      expenses: state.expenses.filter((x) => x.id !== reference),
      events: [newEvent, ...state.events],
    };
  }
  if (command.type === "saveBudget") {
    const budget = { ...command.plan, id: reference, modifiedAt: at };
    return {
      ...state,
      budgets: [budget, ...state.budgets.filter((x) => x.id !== reference)],
      counter: editing ? state.counter : state.counter + 1,
      events: [newEvent, ...state.events],
    };
  }
  if (!state.budgets.some((x) => x.id === reference)) return state;
  return {
    ...state,
    budgets: state.budgets.filter((x) => x.id !== reference),
    events: [newEvent, ...state.events],
  };
}
type WorkspaceContext = { state: WorkspaceSession; send: (c: Command) => void };
const Context = createContext<WorkspaceContext | null>(null);
export function WorkspaceProvider({ children }: { children: ReactNode }) {
  const [state, send] = useReducer(reduce, initial);
  const value = useMemo(() => ({ state, send }), [state]);
  return <Context.Provider value={value}>{children}</Context.Provider>;
}
export function useWorkspace() {
  const session = useContext(Context);
  if (!session) throw new Error("WorkspaceProvider required");
  return session;
}
