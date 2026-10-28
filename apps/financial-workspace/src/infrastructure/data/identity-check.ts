import { BrowserCacheLocation, PublicClientApplication } from "@azure/msal-browser";

/**
 * Read-only, opt-in DEV proof. This does NOT make the workspace a LIVE
 * repository. Tokens stay in memory and only WhoAmI is queried.
 */
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
  if (window.location.hostname !== "localhost") {
    throw new Error("La comprobación DEV requiere http://localhost:5178/.");
  }
  return { clientId, tenantId, organizationId, organizationUrl: uri.origin + "/" };
}

export async function verifyDelegatedDataverseIdentity(): Promise<void> {
  const config = checkedConfig();
  const msal = new PublicClientApplication({
    auth: {
      clientId: config.clientId,
      authority: "https://login.microsoftonline.com/" + config.tenantId,
      redirectUri: "http://localhost:5178/",
      postLogoutRedirectUri: "http://localhost:5178/",
    },
    cache: { cacheLocation: BrowserCacheLocation.MemoryStorage },
  });
  await msal.initialize();
  const scopes = [config.organizationUrl.replace(/\/$/, "") + "/user_impersonation"];
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
}
