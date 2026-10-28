import assert from "node:assert/strict";
import { build } from "esbuild";
import { readFileSync } from "node:fs";
const bundled = await build({
  entryPoints: ["src/infrastructure/data/expense-draft-rules.ts"],
  bundle: true,
  format: "esm",
  platform: "node",
  write: false,
});
const {
  formatPenOnBlur,
  missingPostingFields,
  validDraftPenAmount,
  validDraftDate,
  draftPenValue,
} = await import(
  "data:text/javascript;base64," + Buffer.from(bundled.outputFiles[0].contents).toString("base64")
);
for (const [value, expected] of [
  ["12", "12.00"],
  ["12.5", "12.50"],
  ["0", "0.00"],
  ["12,3", "12.30"],
  ["12.34", "12.34"],
  ["bad", "bad"],
  ["12.345", "12.345"],
])
  assert.equal(formatPenOnBlur(value), expected);
for (const validAmount of ["1", "1.23", "12", "12.00", "12.5", "12,5", "12,50", "999.99"]) {
  assert.equal(validDraftPenAmount(validAmount), true, "Must accept " + validAmount);
  assert.ok(draftPenValue(validAmount) > 0);
}
for (const bad of ["-1", "0", "0.00", "12.345", "abc", "1,234", "1.2.3", "Infinity"])
  assert.equal(validDraftPenAmount(bad), false, "Must reject " + bad);
assert.equal(validDraftPenAmount(""), true, "Drafts may be incomplete");
assert.equal(draftPenValue(""), null);
assert.equal(draftPenValue("12,50"), 12.5);
assert.equal(validDraftDate("2026-10-28"), true);
assert.equal(validDraftDate("2028-02-29"), true);
assert.equal(validDraftDate("2027-02-29"), false);
assert.equal(validDraftDate("2026-02-31"), false);
assert.equal(validDraftDate(""), true);
const editor = readFileSync("src/LiveWorkspace.tsx", "utf8");
assert.ok(
  editor.includes("validDraftPenAmount(amount)") &&
    editor.includes("validDraftDate(date)") &&
    editor.includes("draftPenValue(amount)"),
  "LIVE editor must use tested validation",
);

const valid = {
  fmg_status: 797280000,
  fmg_amount: 12.34,
  fmg_transactiondate: "2026-10-28T12:00:00Z",
  fmg_flowclass: 797280000,
  fmg_scope: 797280001,
  _fmg_branch_value: null,
  _fmg_category_value: "aaaaaaaa-1234-4234-9234-aaaaaaaaaaaa",
  _fmg_cashaccount_value: "bbbbbbbb-1234-4234-9234-bbbbbbbbbbbb",
  _transactioncurrencyid_value: "cccccccc-1234-4234-9234-cccccccccccc",
};
assert.deepEqual(missingPostingFields(valid), []);
assert.ok(missingPostingFields({ ...valid, fmg_amount: null }).some((x) => x.includes("importe")));
assert.ok(
  missingPostingFields({ ...valid, _transactioncurrencyid_value: null }).some((x) =>
    x.includes("PEN"),
  ),
);
assert.ok(missingPostingFields({ ...valid, fmg_scope: 797280000 }).some((x) => x.includes("sede")));
assert.ok(
  missingPostingFields({
    ...valid,
    fmg_scope: 797280000,
    _fmg_branch_value: "dddddddd-1234-4234-9234-dddddddddddd",
  }).length === 0,
);
console.log("F4_PEN_FORMAT_AND_PREPOST_COMPLETENESS_PASS");
