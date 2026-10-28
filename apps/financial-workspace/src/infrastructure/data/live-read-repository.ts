import type { VerifiedDelegatedSession } from "./identity-check";
import { FinanceAuthorizationUnavailable } from "./financial-repository";
import { createDelegatedDataverseTransport } from "./dataverse-transport";
import {
  loadDraftForEdit,
  conditionalDraftSave,
  type DraftVersion,
  type DraftChange,
} from "./conditional-draft";

export type LiveCollection =
  | "expenses"
  | "budgets"
  | "allocations"
  | "activity"
  | "categories"
  | "branches"
  | "accounts";
export type LiveRow = Readonly<Record<string, unknown>>;
export type LivePage = Readonly<{ rows: LiveRow[]; next: string | null }>;

const TABLES: Record<LiveCollection, { set: string; select: string; order: string; id: string }> = {
  expenses: {
    set: "fmg_financialtransactions",
    select:
      "fmg_financialtransactionid,fmg_name,fmg_status,fmg_amount,fmg_transactiondate,fmg_scope,fmg_flowclass,fmg_description,_fmg_branch_value,_fmg_cashaccount_value,_fmg_category_value,modifiedon",
    order: "modifiedon desc,fmg_financialtransactionid asc",
    id: "fmg_financialtransactionid",
  },
  budgets: {
    set: "fmg_budgets",
    select:
      "fmg_budgetid,fmg_name,fmg_year,fmg_scope,fmg_version,fmg_status,fmg_effectivefrommonth,_fmg_branch_value,modifiedon",
    order: "modifiedon desc,fmg_budgetid asc",
    id: "fmg_budgetid",
  },
  allocations: {
    set: "fmg_budgetallocations",
    select:
      "fmg_budgetallocationid,fmg_name,fmg_amount,fmg_month,_fmg_budget_value,_fmg_category_value,modifiedon",
    order: "modifiedon desc,fmg_budgetallocationid asc",
    id: "fmg_budgetallocationid",
  },
  categories: {
    set: "fmg_categories",
    select: "fmg_categoryid,fmg_name,fmg_code,fmg_isactive,fmg_isgroup,_fmg_parentcategory_value",
    order: "fmg_name asc,fmg_categoryid asc",
    id: "fmg_categoryid",
  },
  branches: {
    set: "fmg_branchs",
    select: "fmg_branchid,fmg_name,fmg_isactive",
    order: "fmg_name asc,fmg_branchid asc",
    id: "fmg_branchid",
  },
  accounts: {
    set: "fmg_cashaccounts",
    select: "fmg_cashaccountid,fmg_name,fmg_isactive,_fmg_branch_value",
    order: "fmg_name asc,fmg_cashaccountid asc",
    id: "fmg_cashaccountid",
  },
  activity: {
    set: "fmg_activityevents",
    select:
      "fmg_activityeventid,fmg_name,fmg_eventtype,fmg_eventtime,fmg_recordtype,fmg_recordid,fmg_summary,_fmg_actor_value",
    order: "fmg_eventtime desc,fmg_activityeventid asc",
    id: "fmg_activityeventid",
  },
};

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export class DelegatedLiveRepository {
  constructor(
    private readonly session: VerifiedDelegatedSession,
    // Keep native browser fetch bound to Window. Calling an extracted fetch
    // as this.http(...) changes its receiver and throws 'Illegal invocation'.
    // An injected HTTP function is still supported for deterministic tests.
    private readonly http: typeof fetch = (input, init) => fetch(input, init),
  ) {}

  /** WhoAmI verifies the authenticated user+organization before EVERY data call. */
  private async trustedToken(): Promise<string> {
    const token = await this.session.acquireDelegatedToken();
    if (!token) throw new FinanceAuthorizationUnavailable();
    const who = await this.http(this.session.organizationUrl + "api/data/v9.2/WhoAmI", {
      method: "GET",
      headers: { Authorization: "Bearer " + token, Accept: "application/json" },
      credentials: "omit",
      cache: "no-store",
      redirect: "error",
    });
    if (who.status !== 200) throw new FinanceAuthorizationUnavailable();
    const identity: unknown = await who.json();
    if (!identity || typeof identity !== "object") throw new FinanceAuthorizationUnavailable();
    const actor = identity as Record<string, unknown>;
    if (
      typeof actor.UserId !== "string" ||
      actor.UserId.toLowerCase() !== this.session.principalId.toLowerCase() ||
      typeof actor.OrganizationId !== "string" ||
      actor.OrganizationId.toLowerCase() !== this.session.organizationId.toLowerCase()
    )
      throw new FinanceAuthorizationUnavailable();
    return token;
  }

  /** A genuine Draft, never a Posted/Void transaction. User explicitly initiates creation. */
  async createDraft(name: string): Promise<string> {
    const cleaned = name.trim();
    if (cleaned.length < 4 || cleaned.length > 100 || /[<>]/.test(cleaned))
      throw new Error("El nombre del borrador debe tener entre 4 y 100 caracteres.");
    const token = await this.trustedToken();
    const response = await this.http(
      this.session.organizationUrl + "api/data/v9.2/fmg_financialtransactions",
      {
        method: "POST",
        headers: {
          Authorization: "Bearer " + token,
          "Content-Type": "application/json",
          Accept: "application/json",
          Prefer: "return=representation",
        },
        body: JSON.stringify({ fmg_name: cleaned, fmg_status: 797280000 }),
        credentials: "omit",
        cache: "no-store",
        redirect: "error",
      },
    );
    if (response.status === 401 || response.status === 403)
      throw new FinanceAuthorizationUnavailable();
    if (response.status !== 201) {
      throw new Error(
        "No se confirmó la creación del Draft (HTTP " +
          response.status +
          "). No reintentes automáticamente: comprueba en Power Apps si ya existe.",
      );
    }
    const raw: unknown = await response.json();
    if (!raw || typeof raw !== "object")
      throw new Error("Borrador creado, sin relectura verificable.");
    const row = raw as Record<string, unknown>;
    if (
      !GUID.test(String(row.fmg_financialtransactionid ?? "")) ||
      row.fmg_status !== 797280000 ||
      row.fmg_name !== cleaned
    )
      throw new Error(
        "Borrador creado; la respuesta no coincide. Compruébalo en Power Apps antes de reintentar.",
      );
    return String(row.fmg_financialtransactionid);
  }

  private draftTransport() {
    return createDelegatedDataverseTransport({
      organizationUrl: this.session.organizationUrl,
      organizationId: this.session.organizationId,
      expenseEntitySet: "fmg_financialtransactions",
      acquireDelegatedToken: this.session.acquireDelegatedToken,
      http: this.http,
    });
  }

  async loadDraft(id: string) {
    return loadDraftForEdit(
      this.draftTransport(),
      {
        principalId: this.session.principalId,
        authenticated: true,
      },
      id,
    );
  }

  async saveDraft(expected: DraftVersion, changes: DraftChange) {
    return conditionalDraftSave(
      this.draftTransport(),
      {
        principalId: this.session.principalId,
        authenticated: true,
      },
      expected,
      changes,
    );
  }

  async deleteDraft(expected: DraftVersion): Promise<void> {
    if (
      !GUID.test(expected.id) ||
      !/^W\/"[^"]+"$/.test(expected.etag) ||
      expected.status !== "Draft"
    )
      throw new FinanceAuthorizationUnavailable();
    const latest = await this.loadDraft(expected.id);
    if (latest.version.etag !== expected.etag)
      throw new Error("El borrador cambió. Recarga antes de eliminar.");
    const token = await this.trustedToken();
    const url =
      this.session.organizationUrl + "api/data/v9.2/fmg_financialtransactions(" + expected.id + ")";
    const response = await this.http(url, {
      method: "DELETE",
      headers: { Authorization: "Bearer " + token, "If-Match": expected.etag },
      credentials: "omit",
      cache: "no-store",
      redirect: "error",
    });
    if (response.status === 401 || response.status === 403)
      throw new FinanceAuthorizationUnavailable();
    if (response.status === 412 || response.status === 409)
      throw new Error("Conflicto de versión: no se eliminó el borrador.");
    if (response.status !== 204)
      throw new Error("No se confirmó la eliminación. Comprueba el registro en Power Apps.");
    const check = await this.http(url, {
      method: "GET",
      headers: {
        Authorization: "Bearer " + (await this.trustedToken()),
        Accept: "application/json",
      },
      credentials: "omit",
      cache: "no-store",
      redirect: "error",
    });
    if (check.status !== 404)
      throw new Error("No se verificó la ausencia del borrador tras eliminarlo.");
  }

  async readPage(collection: LiveCollection, next?: string): Promise<LivePage> {
    const table = TABLES[collection];
    if (!table) throw new FinanceAuthorizationUnavailable();
    const base = this.session.organizationUrl + "api/data/v9.2/";
    const url = next
      ? new URL(next)
      : new URL(
          table.set + "?$select=" + table.select + "&$orderby=" + encodeURIComponent(table.order),
          base,
        );
    // Next-page URLs must be minted by Dataverse for the SAME whitelisted collection.
    if (
      url.origin !== new URL(base).origin ||
      url.pathname !== new URL(base + table.set).pathname ||
      url.username ||
      url.password ||
      url.hash ||
      (next && !url.searchParams.has("$skiptoken") && !url.searchParams.has("$skip"))
    )
      throw new FinanceAuthorizationUnavailable();
    const token = await this.trustedToken();
    const result = await this.http(url.toString(), {
      method: "GET",
      headers: {
        Authorization: "Bearer " + token,
        Accept: "application/json",
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0",
        Prefer: "odata.maxpagesize=25",
      },
      credentials: "omit",
      cache: "no-store",
      redirect: "error",
    });
    if (result.status === 401 || result.status === 403) throw new FinanceAuthorizationUnavailable();
    if (result.status !== 200)
      throw new Error("Dataverse no permitió leer " + collection + ": HTTP " + result.status);
    const body: unknown = await result.json();
    if (!body || typeof body !== "object") throw new Error("Respuesta de Dataverse inválida.");
    const data = body as Record<string, unknown>;
    if (
      !Array.isArray(data.value) ||
      data.value.length > 25 ||
      data.value.some(
        (row) =>
          !row ||
          typeof row !== "object" ||
          !GUID.test(((row as Record<string, unknown>)[table.id] as string) ?? ""),
      )
    )
      throw new Error("El conjunto de resultados no corresponde al contrato del servidor.");
    const nextUrl = data["@odata.nextLink"];
    if (nextUrl !== undefined && nextUrl !== null && typeof nextUrl !== "string")
      throw new Error("La paginación de Dataverse devolvió un vínculo inválido.");
    return Object.freeze({
      rows: (data.value as LiveRow[]).map((row) => Object.freeze({ ...row })),
      next: nextUrl ?? null,
    });
  }
}
