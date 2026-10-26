import assert from "node:assert/strict";
import { build } from "esbuild";

const output = await build({
  entryPoints: ["src/infrastructure/data/dataverse-transport.ts"],
  platform: "node",
  format: "esm",
  bundle: true,
  write: false,
});
const moduleUrl =
  "data:text/javascript;base64," + Buffer.from(output.outputFiles[0].contents).toString("base64");
const { createDelegatedDataverseTransport } = await import(moduleUrl);
const user = "12345678-1234-4234-9234-123456789012";
const organization = "abcdef12-1234-4234-9234-abcdef123456";
const record = "22222222-1234-4234-9234-123456789012";
const calls = [];
const cfg = {
  organizationUrl: "https://orgdemo.crm3.dynamics.com/",
  organizationId: organization,
  expenseEntitySet: "fmg_financialtransactions",
  acquireDelegatedToken: async () => "sample-memory-token",
  http: async (url, init) => {
    calls.push({ url, init });
    if (url.endsWith("/WhoAmI")) {
      return new Response(JSON.stringify({ UserId: user, OrganizationId: organization }), {
        status: 200,
      });
    }
    return new Response(JSON.stringify({ fmg_financialtransactionid: record }), {
      status: 200,
    });
  },
};
assert.throws(
  () => createDelegatedDataverseTransport({ ...cfg, organizationUrl: "http://evil.example/" }),
  /URL del entorno/,
);
assert.throws(
  () =>
    createDelegatedDataverseTransport({ ...cfg, organizationUrl: "https://evil.dynamics.com/" }),
  /URL del entorno/,
);
const transport = createDelegatedDataverseTransport(cfg);
const request = {
  method: "GET",
  relativeUrl: "fmg_financialtransactions(" + record + ")",
  headers: { Accept: "application/json" },
  context: { principalId: user, authenticated: true },
};
await assert.rejects(
  transport.request({ ...request, context: { principalId: "", authenticated: true } }),
  (error) => error?.name === "FinanceAuthorizationUnavailable",
);
await assert.rejects(
  transport.request({ ...request, headers: { Authorization: "injected" } }),
  (error) => error?.name === "FinanceAuthorizationUnavailable",
);
await assert.rejects(
  transport.request({ ...request, relativeUrl: "https://evil.example/other" }),
  (error) => error?.name === "FinanceAuthorizationUnavailable",
);
assert.equal(calls.length, 0, "No token or network before local authorization checks");
const response = await transport.request(request);
assert.equal(response.status, 200);
assert.equal(response.data.fmg_financialtransactionid, record);
assert.equal(calls.length, 2, "Delegated WhoAmI verified immediately before request");
assert.ok(calls.every((call) => call.url.startsWith(cfg.organizationUrl)));
assert.ok(calls.every((call) => call.init.headers.Authorization === "Bearer sample-memory-token"));
assert.ok(
  calls.every((call) => call.init.credentials === "omit" && call.init.redirect === "error"),
);
const otherUser = "aaaaaaaa-1234-4234-9234-aaaaaaaaaaaa";
await assert.rejects(
  transport.request({ ...request, context: { principalId: otherUser, authenticated: true } }),
  (error) => error?.name === "FinanceAuthorizationUnavailable",
);
assert.equal(calls.length, 3, "An identity mismatch must never reach the financial record");
console.log("DELEGATED_TRANSPORT_BOUNDARY_PASS");
