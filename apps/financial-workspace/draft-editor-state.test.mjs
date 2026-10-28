import assert from "node:assert/strict";
import { build } from "esbuild";
const compiled = await build({
  entryPoints: ["src/infrastructure/data/draft-editor-state.ts"],
  platform: "node",
  format: "esm",
  bundle: true,
  write: false,
});
const { hydrateDraftFields, loadCompleteReferenceCatalog } = await import(
  "data:text/javascript;base64," + Buffer.from(compiled.outputFiles[0].contents).toString("base64")
);
const record = {
  fmg_financialtransactionid: "11111111-1111-4111-8111-111111111111",
  fmg_status: 797280000,
  fmg_amount: 12,
  fmg_transactiondate: "2026-10-28T12:00:00.000Z",
  fmg_description: "Descripción persistida",
  fmg_documentreference: "FAC-001",
  fmg_scope: 797280001,
  fmg_flowclass: 797280002,
  _fmg_category_value: "22222222-2222-4222-8222-222222222222",
  _fmg_branch_value: null,
  _fmg_cashaccount_value: "33333333-3333-4333-8333-333333333333",
};
assert.deepEqual(hydrateDraftFields(record), {
  amount: "12.00",
  date: "2026-10-28",
  description: "Descripción persistida",
  reference: "FAC-001",
  scopeCode: "797280001",
  flowCode: "797280002",
  categoryId: "22222222-2222-4222-8222-222222222222",
  branchId: "",
  accountId: "33333333-3333-4333-8333-333333333333",
});
assert.equal(
  hydrateDraftFields({
    fmg_financialtransactionid: record.fmg_financialtransactionid,
    fmg_status: 797280000,
  }).amount,
  "",
);
const requested = [];
const reader = {
  async readPage(key, next) {
    requested.push([key, next]);
    if (key === "categories" && !next)
      return {
        rows: Array.from({ length: 25 }, (_, i) => ({ fmg_name: "Categoría " + i })),
        next: "next25",
      };
    if (key === "categories" && next === "next25")
      return {
        rows: Array.from({ length: 16 }, (_, i) => ({ fmg_name: "Categoría " + (25 + i) })),
        next: null,
      };
    if (key === "expenses") return { rows: [{ fmg_name: "Gasto" }], next: "ledger_page_2" };
    throw new Error("Unexpected request");
  },
};
const catalogs = await loadCompleteReferenceCatalog("categories", reader);
assert.equal(catalogs.rows.length, 41);
assert.equal(catalogs.rows[40].fmg_name, "Categoría 40");
assert.equal(catalogs.next, null);
assert.equal(requested.length, 2);
const ledger = await loadCompleteReferenceCatalog("expenses", reader);
assert.equal(ledger.next, "ledger_page_2", "Financial ledger remains paginated");
await assert.rejects(
  loadCompleteReferenceCatalog("branches", {
    async readPage() {
      return { rows: [], next: "repeated" };
    },
  }),
  /repitió/,
);
console.log("F4_DRAFT_REOPEN_AND_REFERENCE_CATALOG_PAGINATION_PASS");
