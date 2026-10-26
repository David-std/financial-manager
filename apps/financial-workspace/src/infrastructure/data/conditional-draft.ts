import {
  FinanceAuthorizationUnavailable,
  requireDelegatedContext,
  type DelegatedContext,
} from "./financial-repository";

/**
 * Not wired into the offline workspace. A production caller must supply a
 * delegated, authorization-enforcing transport obtained after Entra sign-in.
 * The transport must never use a service account that erases actor identity.
 */
export type DraftVersion = Readonly<{
  id: string;
  etag: string;
  status: "Draft";
}>;

export type DraftChange = Readonly<Record<string, string | number | null>>;

export type ConditionalResponse<T> = Readonly<{
  status: number;
  data?: T;
}>;

export type DataverseDraftResult = {
  fmg_financialtransactionid: string;
  fmg_status: number;
  "@odata.etag"?: string;
  [field: string]: string | number | null | undefined;
};

export interface AuthenticatedDataverseTransport {
  /** Verified entity set name supplied by a trusted Dataverse metadata adapter. */
  readonly expenseEntitySet: string;
  request<T = unknown>(request: {
    method: "GET" | "PATCH";
    relativeUrl: string;
    headers: Readonly<Record<string, string>>;
    body?: object;
    context: DelegatedContext;
  }): Promise<ConditionalResponse<T>>;
}

export class DraftWriteConflict extends Error {
  constructor() {
    super("Otro usuario modificó el borrador. Recarga los valores antes de intentarlo nuevamente.");
    this.name = "DraftWriteConflict";
  }
}

function validGuid(id: string): boolean {
  return /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/i.test(id);
}

function validEtag(value: string): boolean {
  // Opaque weak ETag; never inspect or increment its apparent numeric value.
  return /^W\/"[^"]+"$/.test(value);
}

function assertPermission(status: number): void {
  if (status === 401 || status === 403) throw new FinanceAuthorizationUnavailable();
}
function persistedEquals(
  field: string,
  expected: string | number | null,
  actual: unknown,
): boolean {
  if (expected === null) return actual === null;
  if (field === "fmg_transactiondate") {
    return typeof actual === "string" && actual.slice(0, 10) === String(expected).slice(0, 10);
  }
  if (field === "fmg_amount") {
    const value =
      typeof actual === "object" && actual !== null && "Value" in actual
        ? (actual as { Value: unknown }).Value
        : actual;
    return value !== null && value !== undefined && Number(value) === Number(expected);
  }
  if (field === "fmg_flowclass" || field === "fmg_scope") {
    return Number(actual) === Number(expected);
  }
  return actual === expected;
}

/**
 * Reads the authoritative Draft ETag immediately before opening a connected
 * editor. Caller must preserve this exact opaque value for the later PATCH.
 */
export async function loadDraftForEdit(
  transport: AuthenticatedDataverseTransport,
  context: DelegatedContext | null,
  id: string,
): Promise<{ version: DraftVersion; record: DataverseDraftResult }> {
  requireDelegatedContext(context);
  if (!validGuid(id) || !/^fmg_[a-z0-9_]+$/.test(transport.expenseEntitySet)) {
    throw new Error("No se pudo verificar la identidad del borrador.");
  }
  const response = await transport.request<DataverseDraftResult>({
    method: "GET",
    relativeUrl: transport.expenseEntitySet + "(" + id + ")",
    headers: { Accept: "application/json", "Cache-Control": "no-cache" },
    context,
  });
  assertPermission(response.status);
  if (
    response.status !== 200 ||
    !response.data ||
    response.data.fmg_financialtransactionid.toLowerCase() !== id.toLowerCase() ||
    response.data.fmg_status !== 797280000 ||
    !validEtag(response.data["@odata.etag"] ?? "")
  ) {
    throw new Error("El registro no existe, no es un borrador editable o no tiene ETag.");
  }
  return {
    version: { id, etag: response.data["@odata.etag"]!, status: "Draft" },
    record: response.data,
  };
}

export async function conditionalDraftSave(
  transport: AuthenticatedDataverseTransport,
  context: DelegatedContext | null,
  expected: DraftVersion,
  changes: DraftChange,
): Promise<DataverseDraftResult> {
  requireDelegatedContext(context);
  if (!validGuid(expected.id) || !validEtag(expected.etag) || expected.status !== "Draft") {
    throw new Error(
      "No existe una versión válida del borrador para aplicar una escritura condicional.",
    );
  }
  const allowedFields = new Set([
    "fmg_transactiondate",
    "fmg_amount",
    "fmg_flowclass",
    "fmg_scope",
    "fmg_description",
    "fmg_documentreference",
    // Lookup relationship updates need verified OData navigation properties;
    // deliberately excluded until actual metadata and binding are available.
  ]);
  const fields = Object.entries(changes);
  if (!fields.length || fields.some(([name]) => !allowedFields.has(name))) {
    throw new Error(
      "Solo se admiten modificaciones de campos de un borrador mediante este contrato.",
    );
  }
  if (!/^fmg_[a-z0-9_]+$/.test(transport.expenseEntitySet)) {
    throw new Error("No se verificó el conjunto de entidades Dataverse.");
  }
  const relativeUrl = transport.expenseEntitySet + "(" + expected.id + ")";
  const write = await transport.request({
    method: "PATCH",
    relativeUrl,
    headers: {
      "If-Match": expected.etag,
      "Content-Type": "application/json",
      Accept: "application/json",
    },
    body: changes,
    context,
  });
  if (write.status === 412 || write.status === 409) throw new DraftWriteConflict();
  assertPermission(write.status);
  if (write.status !== 204 && write.status !== 200) {
    throw new Error("Dataverse rechazó la edición condicional del borrador.");
  }

  // Read the authoritative row again rather than trusting a successful HTTP response.
  const read = await transport.request<DataverseDraftResult>({
    method: "GET",
    relativeUrl,
    headers: { Accept: "application/json", "Cache-Control": "no-cache" },
    context,
  });
  assertPermission(read.status);
  if (
    read.status !== 200 ||
    !read.data ||
    read.data.fmg_financialtransactionid.toLowerCase() !== expected.id.toLowerCase() ||
    read.data.fmg_status !== 797280000 ||
    !validEtag(read.data["@odata.etag"] ?? "") ||
    read.data["@odata.etag"] === expected.etag
  ) {
    throw new Error("No se confirmó el estado Draft después de guardar.");
  }
  const mismatched = fields
    .filter(([field, expected]) => !persistedEquals(field, expected, read.data?.[field]))
    .map(([field]) => field);
  if (mismatched.length) {
    throw new Error(
      "Dataverse no conservó los valores enviados: " +
        mismatched.join(", ") +
        ". La operación requiere revisión antes de continuar.",
    );
  }
  return read.data;
}
