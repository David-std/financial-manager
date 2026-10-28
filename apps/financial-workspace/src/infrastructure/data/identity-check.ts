import { BrowserCacheLocation, PublicClientApplication } from "@azure/msal-browser";

/** MSAL v5, delegated browser session; one client and a dedicated OAuth response bridge. */
const GUID = /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/i;
type Configuration = {
  clientId: string;
  tenantId: string;
  organizationId: string;
  organizationUrl: string;
};

function checkedConfig(): Configuration {
  const clientId = import.meta.env.VITE_FM_ENTRA_CLIENT_ID || "";
  const tenantId = import.meta.env.VITE_FM_ENTRA_TENANT_ID || "";
  const organizationId = import.meta.env.VITE_FM_ORGANIZATION_ID || "";
  const organizationUrl = import.meta.env.VITE_FM_ORGANIZATION_URL || "";
  if (![clientId, tenantId, organizationId].every((value) => GUID.test(value))) {
    throw new Error("Faltan Client ID, Tenant ID u Organization ID válidos en .env.local.");
  }
  let uri: URL;
  try {
    uri = new URL(organizationUrl);
  } catch {
    throw new Error("La URL de Dataverse no es válida.");
  }
  if (
    uri.protocol !== "https:" ||
    !/^[a-z0-9-]+\.crm\d*\.dynamics\.com$/i.test(uri.hostname) ||
    uri.pathname !== "/" ||
    uri.search ||
    uri.hash ||
    uri.port ||
    uri.username ||
    uri.password
  ) {
    throw new Error("La URL de Dataverse debe ser la raíz HTTPS de una organización.");
  }
  if (
    window.location.hostname !== "localhost" ||
    window.location.port !== "5178" ||
    window.location.protocol !== "http:"
  ) {
    throw new Error("La comprobación DEV requiere http://localhost:5178/.");
  }
  return { clientId, tenantId, organizationId, organizationUrl: uri.origin + "/" };
}

export function explainDelegatedAuthError(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  const code =
    error && typeof error === "object" && "errorCode" in error
      ? String((error as { errorCode: unknown }).errorCode)
      : "";
  if (/AADSTS50011|redirect_uri_mismatch/i.test(message + code))
    return "La dirección SPA de retorno no coincide. Agrega http://localhost:5178/redirect.html en Entra, Autenticación, plataforma SPA.";
  if (/popup_window_error|popup_window_blocked|popup_blocked/i.test(message + code))
    return "El navegador bloqueó la ventana de Microsoft. Permite ventanas emergentes para http://localhost:5178 y vuelve a pulsar Iniciar sesión.";
  if (/user_cancelled|user_cancel/i.test(message + code))
    return "Se cerró la ventana de Microsoft antes de completar el acceso.";
  if (/consent_required|AADSTS65001/i.test(message + code))
    return "El usuario necesita consentimiento delegado de Dataverse (user_impersonation). Revisa los permisos del registro Entra.";
  if (/interaction_in_progress/i.test(message + code))
    return "Ya hay un inicio de sesión en curso. Cierra la ventana anterior antes de intentarlo otra vez.";
  return message || "No fue posible completar el inicio de sesión de Microsoft Entra.";
}

export type VerifiedDelegatedSession = Readonly<{
  principalId: string;
  organizationId: string;
  organizationUrl: string;
  acquireDelegatedToken: () => Promise<string>;
  close: () => Promise<void>;
}>;

type ReadyClient = {
  msal: PublicClientApplication;
  config: Configuration;
  scopes: string[];
  ready: boolean;
};
let sharedClient: ReadyClient | undefined;
let sharedInitialization: Promise<void> | undefined;

/** Initialize on page mount, BEFORE the click that opens the Microsoft window. */
export function prepareDelegatedDataverseIdentity(): Promise<void> {
  if (sharedInitialization) return sharedInitialization;
  const config = checkedConfig();
  const msal = new PublicClientApplication({
    auth: {
      clientId: config.clientId,
      authority: "https://login.microsoftonline.com/" + config.tenantId,
      redirectUri: "http://localhost:5178/redirect.html",
      postLogoutRedirectUri: "http://localhost:5178/",
    },
    cache: { cacheLocation: BrowserCacheLocation.MemoryStorage },
  });
  const scopes = [config.organizationUrl.replace(/\/$/, "") + "/user_impersonation"];
  const client: ReadyClient = { msal, config, scopes, ready: false };
  sharedClient = client;
  sharedInitialization = msal.initialize().then(
    () => {
      client.ready = true;
    },
    (error: unknown) => {
      sharedClient = undefined;
      sharedInitialization = undefined;
      throw error;
    },
  );
  return sharedInitialization;
}

export async function verifyDelegatedDataverseIdentity(): Promise<VerifiedDelegatedSession> {
  const client = sharedClient;
  if (!client?.ready)
    throw new Error("La autenticación todavía se está preparando. Vuelve a intentar.");
  const { msal, config, scopes } = client;
  // CRITICAL: call loginPopup in the click chain without any preceding await;
  // otherwise browsers can block its popup as not being user-initiated.
  const signedIn = await msal.loginPopup({ scopes, prompt: "select_account" });
  if (!signedIn.account) throw new Error("Entra no devolvió una cuenta verificable.");
  const token = await msal.acquireTokenSilent({ account: signedIn.account, scopes });
  if (!token.accessToken) throw new Error("No se obtuvo un token delegado.");
  const response = await fetch(config.organizationUrl + "api/data/v9.2/WhoAmI", {
    method: "GET",
    headers: {
      Authorization: "Bearer " + token.accessToken,
      Accept: "application/json",
      "OData-MaxVersion": "4.0",
      "OData-Version": "4.0",
    },
    cache: "no-store",
    credentials: "omit",
    redirect: "error",
  });
  if (response.status !== 200) {
    throw new Error(
      "WhoAmI devolvió HTTP " + response.status + ". Verifica permisos y consentimiento.",
    );
  }
  const result: unknown = await response.json();
  if (!result || typeof result !== "object") {
    throw new Error("Respuesta WhoAmI sin datos de identidad.");
  }
  const data = result as Record<string, unknown>;
  if (
    typeof data.UserId !== "string" ||
    !GUID.test(data.UserId) ||
    typeof data.OrganizationId !== "string" ||
    data.OrganizationId.toLowerCase() !== config.organizationId.toLowerCase()
  ) {
    throw new Error("Identidad Dataverse u organización distinta de la configurada.");
  }
  let closed = false;
  return Object.freeze({
    principalId: data.UserId,
    organizationId: config.organizationId,
    organizationUrl: config.organizationUrl,
    acquireDelegatedToken: async () => {
      if (closed) throw new Error("La sesión está cerrada. Inicia sesión otra vez.");
      const refreshed = await msal.acquireTokenSilent({ account: signedIn.account, scopes });
      if (
        !refreshed.accessToken ||
        refreshed.account?.homeAccountId !== signedIn.account.homeAccountId
      )
        throw new Error("La identidad delegada cambió. Vuelve a iniciar sesión.");
      return refreshed.accessToken;
    },
    close: async () => {
      closed = true;
      await msal.logoutPopup({ account: signedIn.account });
    },
  });
}
