import assert from "node:assert/strict";
import { build } from "esbuild";

const built = await build({
  entryPoints: ["src/infrastructure/data/conditional-draft.ts"],
  platform: "node",
  bundle: true,
  format: "esm",
  write: false,
});
const { conditionalDraftSave } = await import(
  "data:text/javascript;base64," + Buffer.from(built.outputFiles[0].contents).toString("base64")
);
const recordId = "12345678-1234-4234-9234-123456789012";
const category = "aaaaaaaa-1234-4234-9234-aaaaaaaaaaaa";
const branch = "bbbbbbbb-1234-4234-9234-bbbbbbbbbbbb";
const account = "cccccccc-1234-4234-9234-cccccccccccc";
const context = { authenticated: true, principalId: "dddddddd-1234-4234-9234-dddddddddddd" };
let rev = 1;
const stored = {
  fmg_financialtransactionid: recordId,
  fmg_status: 797280000,
  _fmg_category_value: null,
  _fmg_branch_value: null,
  _fmg_cashaccount_value: null,
};
const patches = [];
const mapping = {
  "fmg_category@odata.bind": ["_fmg_category_value", "fmg_categories"],
  "fmg_branch@odata.bind": ["_fmg_branch_value", "fmg_branchs"],
  "fmg_cashaccount@odata.bind": ["_fmg_cashaccount_value", "fmg_cashaccounts"],
};
const transport = {
  expenseEntitySet: "fmg_financialtransactions",
  async request(request) {
    if (request.method === "GET")
      return { status: 200, data: { ...stored, "@odata.etag": `W/"${rev}"` } };
    patches.push(request);
    if (request.headers["If-Match"] !== `W/"${rev}"`) return { status: 412 };
    for (const [name, payload] of Object.entries(request.body)) {
      if (!mapping[name]) throw new Error("Unexpected update field");
      const [lookup, set] = mapping[name];
      stored[lookup] = payload === null ? null : payload.slice(set.length + 2, -1);
    }
    rev++;
    return { status: 204 };
  },
};
const version = { id: recordId, etag: 'W/"1"', status: "Draft" };
const saved = await conditionalDraftSave(transport, context, version, {
  "fmg_category@odata.bind": `/fmg_categories(${category})`,
  "fmg_branch@odata.bind": `/fmg_branchs(${branch})`,
  "fmg_cashaccount@odata.bind": `/fmg_cashaccounts(${account})`,
});
assert.equal(saved._fmg_category_value, category);
assert.equal(saved._fmg_branch_value, branch);
assert.equal(saved._fmg_cashaccount_value, account);
assert.equal(patches.length, 1);
const after = await conditionalDraftSave(
  transport,
  context,
  { id: recordId, etag: 'W/"2"', status: "Draft" },
  { "fmg_branch@odata.bind": null, "fmg_cashaccount@odata.bind": null },
);
assert.equal(after._fmg_branch_value, null);
assert.equal(after._fmg_cashaccount_value, null);
const before = patches.length;
await assert.rejects(
  conditionalDraftSave(
    transport,
    context,
    { id: recordId, etag: 'W/"3"', status: "Draft" },
    { "fmg_category@odata.bind": "/unknown(12345678-1234-4234-9234-123456789012)" },
  ),
  /relación/,
);
assert.equal(patches.length, before, "Invalid binding cannot reach PATCH");
await assert.rejects(
  conditionalDraftSave(
    transport,
    context,
    { id: recordId, etag: 'W/"3"', status: "Draft" },
    { fmg_status: 797280001 },
  ),
  /Solo se admiten/,
);
assert.equal(patches.length, before, "Status escalation is blocked");
console.log("F4_LOOKUP_BIND_ETAG_READBACK_PASS");
