/**
 * Contract between the visual workspace and the financial system of record.
 * No UI label, view mode or fabricated business role grants data privileges.
 * The only running adapter today is read-only and uses deterministic demo data.
 */
import fixture from "../../data/demo.json";

export type PostedExpense = {
  id: string;
  date: string;
  amount: number;
  category: string;
  categoryCode: string;
  branch: string;
  scope: string;
  account: string;
  flow: string;
  status: string;
};

export type BackendMode = "offline-preview" | "delegated-dataverse";

export interface ReadonlyExpenseRepository {
  readonly mode: BackendMode;
  listPosted(): ReadonlyArray<PostedExpense>;
}

const immutablePosted = Object.freeze(
  (fixture.records as PostedExpense[]).map((payment) => Object.freeze({ ...payment })),
);
if (
  !fixture.demo ||
  fixture.count !== immutablePosted.length ||
  immutablePosted.some((record) => record.status !== "Contabilizado")
) {
  throw new Error("El conjunto sintético no coincide con su declaración.");
}

export const demoExpenses: ReadonlyExpenseRepository = Object.freeze({
  mode: "offline-preview" as const,
  listPosted: () => immutablePosted,
});

export class FinanceAuthorizationUnavailable extends Error {
  constructor() {
    super(
      "Operación no disponible: la sesión no dispone de identidad y permisos Dataverse verificados.",
    );
    this.name = "FinanceAuthorizationUnavailable";
  }
}

export interface DelegatedContext {
  /** Verified by the trusted backend, not user-supplied form or role selectors. */
  readonly principalId: string;
  readonly authenticated: true;
}

export function requireDelegatedContext(
  context: DelegatedContext | null,
): asserts context is DelegatedContext {
  if (!context?.authenticated || !context.principalId.trim()) {
    throw new FinanceAuthorizationUnavailable();
  }
}
