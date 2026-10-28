import assert from "node:assert/strict";
import { build } from "esbuild";

const output = await build({
  entryPoints: ["src/infrastructure/data/live-read-repository.ts"],
  platform: "node",
  format: "esm",
  bundle: true,
  write: false,
});
const moduleUrl =
  "data:text/javascript;base64," + Buffer.from(output.outputFiles[0].contents).toString("base64");
const { DelegatedLiveRepository } = await import(moduleUrl);

const actor = "12345678-1234-4234-9234-123456789012";
const organization = "abcdef12-1234-4234-9234-abcdef123456";
const expense = "22222222-1234-4234-9234-123456789012";
const origin = "https://orgdemo.crm3.dynamics.com/";
const calls = [];
let identity = actor;
let failure = false;
const session = {
  principalId: actor,
  organizationId: organization,
  organizationUrl: origin,
  acquireDelegatedToken: async () => "mock-access-token",
};
function mock(url, init) {
  calls.push({ url, init });
  if (url.endsWith("/WhoAmI"))
    return Promise.resolve(
      new Response(
        JSON.stringify({
          UserId: identity,
          OrganizationId: organization,
        }),
        { status: 200 },
      ),
    );
  if (failure) return Promise.resolve(new Response("No autorizado", { status: 403 }));
  if (init.method === "POST")
    return Promise.resolve(
      new Response(
        JSON.stringify({
          fmg_financialtransactionid: expense,
          fmg_name: "Prueba F4",
          fmg_status: 797280000,
        }),
        { status: 201 },
      ),
    );
  if (init.method === "GET" && url.includes("fmg_financialtransactions("))
    return Promise.resolve(
      new Response(
        JSON.stringify({
          fmg_financialtransactionid: expense,
          fmg_status: 797280000,
          "@odata.etag": 'W/"11"',
          fmg_amount: 7.75,
        }),
        { status: 200 },
      ),
    );
  if (init.method === "PATCH") return Promise.resolve(new Response(null, { status: 204 }));
  if (init.method === "DELETE") return Promise.resolve(new Response(null, { status: 204 }));
  if (init.method === "GET" && url.includes("fmg_financialtransactions?"))
    return Promise.resolve(
      new Response(
        JSON.stringify({
          value: [
            { fmg_financialtransactionid: expense, fmg_name: "Prueba F4", fmg_status: 797280000 },
          ],
        }),
        { status: 200 },
      ),
    );
  if (init.method === "GET" && url.includes("fmg_budgets?"))
    return Promise.resolve(new Response(JSON.stringify({ value: [] }), { status: 200 }));
  return Promise.resolve(new Response(JSON.stringify({ value: [] }), { status: 200 }));
}
const repo = new DelegatedLiveRepository(session, mock);
const initial = await repo.readPage("expenses");
assert.equal(initial.rows.length, 1);
assert.equal(initial.rows[0].fmg_financialtransactionid, expense);
assert.equal(initial.next, null);
assert.equal(calls.length, 2);
assert.equal(calls[1].init.method, "GET");
assert.match(calls[1].url, /fmg_financialtransactions/);
assert.match(calls[1].init.headers.Prefer, /odata.maxpagesize=25/);
assert.ok(calls.every((c) => c.url.startsWith(origin)));
assert.ok(calls.every((c) => c.init.headers.Authorization === "Bearer mock-access-token"));
const budgets = await repo.readPage("budgets");
assert.deepEqual(budgets.rows, []);

await assert.rejects(
  repo.readPage("expenses", "https://attacker.example/page"),
  (e) => e?.name === "FinanceAuthorizationUnavailable",
);
await assert.rejects(
  repo.readPage("expenses", origin + "api/data/v9.2/fmg_budgets?$skiptoken=0"),
  (e) => e?.name === "FinanceAuthorizationUnavailable",
);
const beforeMismatch = calls.length;
identity = "aaaaaaaa-1234-4234-9234-aaaaaaaaaaaa";
await assert.rejects(
  repo.readPage("expenses"),
  (e) => e?.name === "FinanceAuthorizationUnavailable",
);
assert.equal(calls.length, beforeMismatch + 1, "Identity mismatch must not read financial rows");
identity = actor;
failure = true;
await assert.rejects(
  repo.readPage("expenses"),
  (e) => e?.name === "FinanceAuthorizationUnavailable",
);
failure = false;
await assert.rejects(repo.createDraft("<bad>"), /nombre del borrador/);
const created = await repo.createDraft("Prueba F4");
assert.equal(created, expense);
const post = calls.find((c) => c.init.method === "POST");
assert.deepEqual(JSON.parse(post.init.body), { fmg_name: "Prueba F4", fmg_status: 797280000 });
assert.ok(!("fmg_amount" in JSON.parse(post.init.body)), "Create must be Draft only");
const draft = await repo.loadDraft(expense);
assert.equal(draft.version.etag, 'W/"11"');
assert.equal(draft.version.status, "Draft");
console.log("F4_DELEGATED_LIVE_READ_DRAFT_SECURITY_PASS");
