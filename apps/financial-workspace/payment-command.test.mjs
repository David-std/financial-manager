import assert from "node:assert/strict";
import { build } from "esbuild";

const built = await build({
  entryPoints: ["src/infrastructure/data/live-read-repository.ts"],
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
});
const { DelegatedLiveRepository } = await import(
  "data:text/javascript;base64," + Buffer.from(built.outputFiles[0].contents).toString("base64")
);
const principal = "11111111-1111-4111-8111-111111111111";
const org = "22222222-2222-4222-8222-222222222222";
const id = "33333333-3333-4333-8333-333333333333";
const base = "https://f4fixture.crm3.dynamics.com/";
let status = 797280000,
  version = 51,
  denied = false;
const calls = [];
async function mock(url, req) {
  calls.push({ url, method: req.method, body: req.body });
  if (url.endsWith("/WhoAmI"))
    return new Response(
      JSON.stringify({
        UserId: principal,
        OrganizationId: org,
      }),
      { status: 200 },
    );
  if (req.method === "GET" && url.includes("fmg_financialtransactions("))
    return new Response(
      JSON.stringify({
        fmg_status: status,
        versionnumber: version,
        fmg_voidreason: status === 797280002 ? "Motivo de control de prueba" : null,
      }),
      { status: 200 },
    );
  if (req.method === "POST" && url.includes("FinancialTransaction")) {
    if (denied) return new Response("Acceso denegado", { status: 403 });
    const payload = JSON.parse(req.body);
    assert.equal(payload.RecordId, id);
    assert.equal(payload.ExpectedRowVersion, String(version));
    assert.equal(
      payload.Reason === undefined || payload.Reason === "Motivo de control de prueba",
      true,
    );
    const next = url.endsWith("fmg_PostFinancialTransaction") ? 797280001 : 797280002;
    status = next;
    version++;
    return new Response(JSON.stringify({ Completed: true }), { status: 200 });
  }
  throw new Error("Unexpected network call: " + req.method + " " + url);
}
const repo = new DelegatedLiveRepository(
  {
    principalId: principal,
    organizationId: org,
    organizationUrl: base,
    acquireDelegatedToken: async () => "test-only",
  },
  mock,
);
await repo.executePaymentCommand("post", id, 51);
assert.equal(status, 797280001);
const post = calls.find((call) => call.method === "POST");
assert.ok(post.url.endsWith("fmg_PostFinancialTransaction"));
assert.ok(
  calls.every((call) => call.method !== "PATCH"),
  "No direct status PATCH",
);
const initialCalls = calls.length;
await assert.rejects(repo.executePaymentCommand("post", id, 51), /versi.n|estado/i);
assert.equal(calls.length, initialCalls + 2, "Obsolete request cannot issue another POST");
await assert.rejects(repo.executePaymentCommand("void", id, 52, "short"), /motivo/);
const prior = calls.length;
assert.equal(calls.length, prior);
denied = true;
await assert.rejects(
  repo.executePaymentCommand("void", id, 52, "Motivo de control de prueba"),
  (error) => error.name === "FinanceAuthorizationUnavailable",
);
assert.equal(status, 797280001, "403 has no state transition");
denied = false;
await repo.executePaymentCommand("void", id, 52, "Motivo de control de prueba");
assert.equal(status, 797280002);
assert.ok(
  calls.some((call) => call.method === "POST" && call.url.endsWith("fmg_VoidFinancialTransaction")),
);
assert.equal(calls.filter((call) => call.method === "PATCH").length, 0);
console.log("F4_PAYMENT_POST_VOID_API_CONTRACTS_PASS");
