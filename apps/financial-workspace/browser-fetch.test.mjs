import assert from "node:assert/strict";
import { chromium } from "playwright-core";

const browser = await chromium.launch({
  executablePath: "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
  headless: true,
  args: ["--disable-gpu"],
});
const page = await browser.newPage();
try {
  await page.goto("http://localhost:5178/", { waitUntil: "domcontentloaded" });
  const outcome = await page.evaluate(async () => {
    const { DelegatedLiveRepository } = await import(
      "/src/infrastructure/data/live-read-repository.ts"
    );
    const native = window.fetch;
    const id = "12345678-1234-4234-9234-123456789012";
    const user = "22222222-1234-4234-9234-123456789012";
    const org = "33333333-1234-4234-9234-123456789012";
    const base = "https://f4fixture.crm3.dynamics.com/";
    const calls = [];
    let failed = false;
    window.fetch = function (input, init) {
      if (this !== window) {
        throw new TypeError("Failed to execute 'fetch' on 'Window': Illegal invocation");
      }
      const url = String(input);
      calls.push({ url, method: init.method, credentials: init.credentials });
      if (url === base + "api/data/v9.2/WhoAmI") {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              UserId: user,
              OrganizationId: org,
            }),
            { status: 200, headers: { "content-type": "application/json" } },
          ),
        );
      }
      if (failed) return Promise.resolve(new Response("", { status: 403 }));
      if (init.method === "POST" && url.endsWith("fmg_financialtransactions")) {
        const body = JSON.parse(init.body);
        if (body.fmg_status !== 797280000) throw new Error("Not a Draft");
        return Promise.resolve(
          new Response(
            JSON.stringify({
              fmg_financialtransactionid: id,
              fmg_status: 797280000,
              fmg_name: body.fmg_name,
            }),
            { status: 201, headers: { "content-type": "application/json" } },
          ),
        );
      }
      if (url.includes("fmg_financialtransactions(") && init.method === "GET") {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              fmg_financialtransactionid: id,
              fmg_status: 797280000,
              fmg_amount: 1,
              "@odata.etag": 'W/"100"',
            }),
            { status: 200, headers: { "content-type": "application/json" } },
          ),
        );
      }
      const collection = url.match(/api\/data\/v9\.2\/(fmg_[a-z]+)\?/i)?.[1];
      const keys = {
        fmg_financialtransactions: "fmg_financialtransactionid",
        fmg_budgets: "fmg_budgetid",
        fmg_budgetallocations: "fmg_budgetallocationid",
        fmg_activityevents: "fmg_activityeventid",
        fmg_categories: "fmg_categoryid",
        fmg_branchs: "fmg_branchid",
        fmg_cashaccounts: "fmg_cashaccountid",
      };
      if (collection in keys) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              value: [{ [keys[collection]]: id, fmg_name: "Fixture" }],
            }),
            { status: 200, headers: { "content-type": "application/json" } },
          ),
        );
      }
      throw new Error("Unexpected operation: " + init.method + " " + url);
    };
    try {
      const repo = new DelegatedLiveRepository({
        principalId: user,
        organizationId: org,
        organizationUrl: base,
        acquireDelegatedToken: async () => "test-token-only",
      });
      for (const collection of [
        "expenses",
        "budgets",
        "allocations",
        "activity",
        "categories",
        "branches",
        "accounts",
      ]) {
        const result = await repo.readPage(collection);
        if (result.rows.length !== 1) throw new Error("Failed " + collection);
      }
      const created = await repo.createDraft("PRUEBA F4 BROWSER");
      if (created !== id) throw new Error("Missing Draft response");
      const loaded = await repo.loadDraft(id);
      if (loaded.version.status !== "Draft" || loaded.version.etag !== 'W/"100"')
        throw new Error("Missing ETag");
      failed = true;
      let denied = false;
      try {
        await repo.readPage("expenses");
      } catch (e) {
        denied = e.name === "FinanceAuthorizationUnavailable";
      }
      if (!denied) throw new Error("403 must deny financial read");
      return {
        requests: calls.length,
        readCollections: 7,
        draft: true,
        etag: true,
        denies403: true,
      };
    } finally {
      window.fetch = native;
    }
  });
  assert.equal(outcome.readCollections, 7);
  assert.equal(outcome.draft, true);
  assert.equal(outcome.etag, true);
  assert.equal(outcome.denies403, true);
  console.log("F4_NATIVE_WINDOW_FETCH_BINDING_PASS", outcome);
} finally {
  await browser.close();
}
