import assert from "node:assert/strict";
import { build } from "esbuild";
const compiled = await build({
  entryPoints: ["src/infrastructure/data/conditional-draft.ts"],
  platform: "node",
  format: "esm",
  bundle: true,
  write: false,
});
const url =
  "data:text/javascript;base64," + Buffer.from(compiled.outputFiles[0].contents).toString("base64");
const { conditionalDraftSave, DraftWriteConflict, FinanceAuthorizationUnavailable } = await import(
  url
);

const id = "12345678-1234-4234-9234-123456789012";
const version = { id, etag: 'W/"27"', status: "Draft" };
const auth = { principalId: "delegated-user-id", authenticated: true };
const update = { fmg_description: "Revised detail" };
const attempted = [];
const transport = {
  expenseEntitySet: "fmg_financialtransactions",
  async request(request) {
    attempted.push(request);
    if (request.method === "PATCH") return { status: 204 };
    return {
      status: 200,
      data: {
        fmg_financialtransactionid: id,
        fmg_status: 797280000,
        "@odata.etag": 'W/"28"',
      },
    };
  },
};
await assert.rejects(
  conditionalDraftSave(transport, null, version, update),
  FinanceAuthorizationUnavailable,
);
await assert.rejects(
  conditionalDraftSave(transport, auth, { ...version, etag: "" }, update),
  /versión válida/,
);
await assert.rejects(
  conditionalDraftSave(transport, auth, version, { fmg_status: 797280001 }),
  /campos de un borrador/,
);
assert.equal(attempted.length, 0, "No request without a valid delegated context and version");
const result = await conditionalDraftSave(transport, auth, version, update);
assert.equal(result.fmg_status, 797280000);
assert.equal(result["@odata.etag"], 'W/"28"');
assert.equal(attempted.length, 2);
assert.equal(attempted[0].headers["If-Match"], version.etag);
assert.equal(attempted[0].method, "PATCH");
assert.equal(attempted[1].method, "GET");

const stale = {
  expenseEntitySet: "fmg_financialtransactions",
  async request(request) {
    if (request.method === "PATCH") return { status: 412 };
    throw new Error("Read must not occur after conflict");
  },
};
await assert.rejects(conditionalDraftSave(stale, auth, version, update), DraftWriteConflict);
const denied = {
  expenseEntitySet: "fmg_financialtransactions",
  request: async () => ({ status: 403 }),
};
await assert.rejects(
  conditionalDraftSave(denied, auth, version, update),
  FinanceAuthorizationUnavailable,
);
const badState = {
  expenseEntitySet: "fmg_financialtransactions",
  async request(request) {
    return request.method === "PATCH"
      ? { status: 204 }
      : { status: 200, data: { fmg_financialtransactionid: id, fmg_status: 797280001 } };
  },
};
await assert.rejects(
  conditionalDraftSave(badState, auth, version, update),
  /No se confirmó el estado Draft/,
);
const immutable = await build({
  entryPoints: ["src/infrastructure/data/financial-repository.ts"],
  platform: "node",
  format: "esm",
  bundle: true,
  write: false,
});
const dataUrl =
  "data:text/javascript;base64," +
  Buffer.from(immutable.outputFiles[0].contents).toString("base64");
const { demoExpenses } = await import(dataUrl);
assert.equal(demoExpenses.mode, "offline-preview");
assert.equal(demoExpenses.listPosted().length, 310);
assert.ok(demoExpenses.listPosted().every((r) => r.status === "Contabilizado"));
assert.ok(Object.isFrozen(demoExpenses.listPosted()));
console.log("FINANCIAL_ACCESS_CONTRACTS_PASS");
