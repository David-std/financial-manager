import {
  FinanceAuthorizationUnavailable,
  requireDelegatedContext,
  type DelegatedContext,
} from "./financial-repository";
import type { AuthenticatedDataverseTransport, ConditionalResponse } from "./conditional-draft";

/**
 * Explicit opt-in Dataverse transport, not wired into the offline app.
 * Acquire access tokens only from a caller-owned Entra delegated flow, in memory.
 */
export interface DataverseConnectionConfig {
  readonly organizationUrl: string;
  readonly organizationId: string;
  readonly expenseEntitySet: string;
  readonly acquireDelegatedToken: () => Promise<string>;
  readonly http?: typeof fetch;
}

const guid = /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/i;
function trustedApiRoot(value: string): string {
  const url = new URL(value);
  if (
    url.protocol !== "https:" ||
    !/^[a-z0-9-]+\.crm\d*\.dynamics\.com$/i.test(url.hostname) ||
    url.username ||
    url.password ||
    url.port ||
    url.pathname !== "/" ||
    url.search ||
    url.hash
  ) {
    throw new Error("La URL del entorno Dataverse no es válida o no fue autorizada.");
  }
  return url.origin + "/api/data/v9.2/";
}

function responseData<T>(body: string): T | undefined {
  if (!body) return undefined;
  try {
    return JSON.parse(body) as T;
  } catch {
    throw new Error("Dataverse devolvió un formato de respuesta no válido.");
  }
}

export function createDelegatedDataverseTransport(
  config: DataverseConnectionConfig,
): AuthenticatedDataverseTransport {
  const root = trustedApiRoot(config.organizationUrl);
  if (!guid.test(config.organizationId) || !/^fmg_[a-z0-9_]+$/.test(config.expenseEntitySet)) {
    throw new Error("La configuración autorizada de Dataverse está incompleta.");
  }
  const http = config.http ?? fetch;
  const fetchResult = async <T>(
    relative: string,
    accessToken: string,
    method: "GET" | "PATCH",
    extraHeaders: Readonly<Record<string, string>>,
    body?: object,
  ): Promise<ConditionalResponse<T>> => {
    if (!/^(?:WhoAmI|fmg_[a-z0-9_]+\([a-f\d-]{36}\))$/i.test(relative)) {
      throw new FinanceAuthorizationUnavailable();
    }
    const response = await http(root + relative, {
      method,
      headers: {
        Authorization: "Bearer " + accessToken,
        Accept: "application/json",
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0",
        ...extraHeaders,
      },
      body: body === undefined ? undefined : JSON.stringify(body),
      cache: "no-store",
      credentials: "omit",
      redirect: "error",
    });
    const text = response.status === 204 ? "" : await response.text();
    return { status: response.status, data: responseData<T>(text) };
  };
  return Object.freeze({
    expenseEntitySet: config.expenseEntitySet,
    async request<T = unknown>(request: {
      method: "GET" | "PATCH";
      relativeUrl: string;
      headers: Readonly<Record<string, string>>;
      body?: object;
      context: DelegatedContext;
    }): Promise<ConditionalResponse<T>> {
      requireDelegatedContext(request.context);
      if (Object.keys(request.headers).some((key) => key.toLowerCase() === "authorization")) {
        throw new FinanceAuthorizationUnavailable();
      }
      const prefix = config.expenseEntitySet + "(";
      if (!request.relativeUrl.startsWith(prefix) || !request.relativeUrl.endsWith(")")) {
        throw new FinanceAuthorizationUnavailable();
      }
      const token = await config.acquireDelegatedToken();
      if (!token || typeof token !== "string") throw new FinanceAuthorizationUnavailable();
      const identity = await fetchResult<{ UserId: string; OrganizationId: string }>(
        "WhoAmI",
        token,
        "GET",
        {},
      );
      if (
        identity.status !== 200 ||
        !identity.data ||
        !guid.test(identity.data.UserId ?? "") ||
        identity.data.UserId.toLowerCase() !== request.context.principalId.toLowerCase() ||
        identity.data.OrganizationId?.toLowerCase() !== config.organizationId.toLowerCase()
      ) {
        throw new FinanceAuthorizationUnavailable();
      }
      return fetchResult<T>(
        request.relativeUrl,
        token,
        request.method,
        request.headers,
        request.body,
      );
    },
  });
}
