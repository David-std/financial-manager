import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { build } from "esbuild";

const compiled = await build({
  entryPoints: ["src/infrastructure/data/live-read-repository.ts"],
  platform: "node",
  format: "esm",
  bundle: true,
  write: false,
});
const { DelegatedLiveRepository } = await import(
  "data:text/javascript;base64," + Buffer.from(compiled.outputFiles[0].contents).toString("base64")
);
const org = "abcdef12-1234-4234-9234-abcdef123456";
const actor = "12345678-1234-4234-9234-123456789012";
const origin = "https://f5-test.crm3.dynamics.com/";
const id = "22222222-1234-4234-9234-123456789012";
const specs = [
  ["cashReceipts", "fmg_cashreceipts", "fmg_cashreceiptid"],
  ["cashTransfers", "fmg_cashtransfers", "fmg_cashtransferid"],
  ["cashPostings", "fmg_cashpostings", "fmg_cashpostingid"],
  ["cashOpenings", "fmg_accountopenings", "fmg_accountopeningid"],
  ["bankStatements", "fmg_bankstatements", "fmg_bankstatementid"],
  ["bankReconciliations", "fmg_bankreconciliations", "fmg_bankreconciliationid"],
];
const calls = [];
let forbidden = false;
const repo = new DelegatedLiveRepository(
  {
    principalId: actor,
    organizationId: org,
    organizationUrl: origin,
    acquireDelegatedToken: async () => "test-delegated",
  },
  async (url, opts) => {
    calls.push({ url, opts });
    assert.equal(opts.method, "GET", "F5 read-only service must never mutate");
    assert.equal(opts.headers.Authorization, "Bearer test-delegated");
    assert.ok(url.startsWith(origin));
    if (url.endsWith("/WhoAmI"))
      return new Response(JSON.stringify({ UserId: actor, OrganizationId: org }), { status: 200 });
    if (forbidden) return new Response("Forbidden", { status: 403 });
    const matched = specs.find((s) => url.includes("/" + s[1] + "?"));
    assert.ok(matched, "Only verified entity sets permitted");
    return new Response(
      JSON.stringify({ value: [{ [matched[2]]: id, fmg_name: "Resultado de lectura" }] }),
      { status: 200 },
    );
  },
);
for (const [name, set] of specs) {
  const result = await repo.readPage(name);
  assert.equal(result.rows.length, 1);
  assert.equal(result.rows[0].fmg_name, "Resultado de lectura");
  assert.ok(calls.at(-1).url.includes(set));
}
await assert.rejects(
  repo.readPage("cashReceipts", "https://outside.invalid/?$skiptoken=2"),
  (error) => error?.name === "FinanceAuthorizationUnavailable",
);
await assert.rejects(
  repo.readPage("cashTransfers", origin + "api/data/v9.2/fmg_cashpostings?$skiptoken=2"),
  (error) => error?.name === "FinanceAuthorizationUnavailable",
);
forbidden = true;
await assert.rejects(
  repo.readPage("cashReceipts"),
  (error) => error?.name === "FinanceAuthorizationUnavailable",
);
const panel = readFileSync("src/CashReadOnlyPanel.tsx", "utf8");
assert.ok(panel.includes("No se asume saldo cero"));
assert.ok(panel.includes("No se permiten cobros"));
assert.ok(!panel.includes("onClick={() => void post"));
assert.ok(!panel.includes("fetch("), "Read-only panel must use delegated repository");
console.log("F5_LIVE_CASH_READONLY_SIX_VERIFIED_COLLECTIONS_PASS");
