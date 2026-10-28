import assert from "node:assert/strict";
import { chromium } from "playwright-core";

const browser = await chromium.launch({
  executablePath: "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
  headless: true,
  args: ["--disable-gpu"],
});
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
const errors = [];
page.on("pageerror", (e) => errors.push(e.message));
let authorize = null;
await page.context().route("https://login.microsoftonline.com/**", async (route) => {
  const url = new URL(route.request().url());
  if (url.pathname.endsWith("/oauth2/v2.0/authorize")) {
    authorize = {
      redirect: url.searchParams.get("redirect_uri"),
      responseType: url.searchParams.get("response_type"),
      scope: url.searchParams.get("scope"),
      hasState: Boolean(url.searchParams.get("state")),
      hasChallenge: Boolean(url.searchParams.get("code_challenge")),
    };
    await route.fulfill({
      status: 200,
      contentType: "text/html",
      body: "<!doctype html><html><body>Inicio de Microsoft interceptado por la prueba; no se introducen credenciales.</body></html>",
    });
  } else {
    await route.continue();
  }
});
try {
  await page.goto("http://localhost:5178/?mode=live", { waitUntil: "domcontentloaded" });
  const button = page.getByRole("button", { name: "Iniciar sesión Entra" });
  await button.waitFor({ state: "visible", timeout: 15000 }).catch(async () => {
    throw new Error(
      "La pantalla LIVE no mostró el botón. Texto: " +
        (await page.locator("body").innerText()).slice(0, 1000) +
        "; errores: " +
        errors.slice(0, 4).join(" | "),
    );
  });
  assert.equal(await button.isEnabled(), true);
  const popupPromise = page.waitForEvent("popup", { timeout: 15000 });
  await button.click();
  const popup = await popupPromise;
  await popup.waitForURL((url) => url.hostname === "login.microsoftonline.com", { timeout: 18000 });
  await popup.waitForLoadState("domcontentloaded", { timeout: 18000 });
  assert.ok(authorize, "OAuth authorization request should start after a real click");
  assert.equal(authorize.redirect, "http://localhost:5178/redirect.html");
  assert.equal(authorize.responseType, "code");
  assert.ok(authorize.hasState && authorize.hasChallenge, "OAuth must include state and PKCE");
  assert.match(authorize.scope, /user_impersonation/);
  assert.deepEqual(errors, [], "No errors before OAuth return");
  console.log("F4_ENTRA_POPUP_PKCE_REDIRECT_BRIDGE_PASS");
} finally {
  await browser.close();
}
