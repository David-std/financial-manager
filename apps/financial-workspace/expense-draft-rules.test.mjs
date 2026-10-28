import assert from "node:assert/strict";
import { build } from "esbuild";
const bundled = await build({
  entryPoints: ["src/infrastructure/data/expense-draft-rules.ts"],
  bundle: true,
  format: "esm",
  platform: "node",
  write: false,
});
const { formatPenOnBlur, missingPostingFields } = await import(
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
