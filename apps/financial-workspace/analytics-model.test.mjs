import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { build } from "esbuild";
const dir = path
  .dirname(new URL(import.meta.url).pathname)
  .replace(/^\/(?:[A-Za-z]:)/, (s) => s.slice(1));
const source = path.join(dir, "src", "analytics-model.ts");
const output = await build({
  entryPoints: [source],
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
});
const encoded = Buffer.from(output.outputFiles[0].contents).toString("base64");
const { buildAnalysis, groupBy, total } = await import("data:text/javascript;base64," + encoded);
const fixture = JSON.parse(fs.readFileSync(path.join(dir, "src", "data", "demo.json"), "utf8"));
const records = fixture.records;
const near = (a, b) => assert.ok(Math.abs(a - b) < 0.00001, `${a} != ${b}`);
const filter = (year, month = 0, scope = "Todos", category = "Todas") => ({
  year,
  month,
  scope,
  category,
});
const current = buildAnalysis(records, filter(2026));
const priorMatched = records.filter(
  (r) => Number(r.date.slice(0, 4)) === 2025 && Number(r.date.slice(5, 7)) <= current.coverageEnd,
);
near(current.currentTotal, total(current.current));
near(current.previousTotal, total(priorMatched));
near(current.previousTotal + (current.delta ?? 0), current.currentTotal);
near(
  current.drivers.reduce((s, x) => s + x.delta, 0),
  current.delta,
);
near(
  current.byCategory.reduce((s, x) => s + x.amount, 0),
  current.currentTotal,
);
near(
  current.byAccount.reduce((s, x) => s + x.amount, 0),
  current.currentTotal,
);
near(
  current.byBranch.reduce((s, x) => s + x.amount, 0),
  current.currentTotal,
);
near(
  current.byFlow.reduce((s, x) => s + x.amount, 0),
  current.currentTotal,
);
assert.ok(current.topThreeShare >= 0 && current.topThreeShare <= 100);
assert.ok(current.monthly.every((p) => p.index <= current.coverageEnd));
assert.ok(
  current.heatmap
    .find((x) => x.year === 2026)
    .months.slice(current.coverageEnd)
    .every((x) => x.value === null),
);
const baseline2024 = buildAnalysis(records, filter(2024));
assert.equal(baseline2024.eligiblePrior, false);
assert.equal(baseline2024.delta, null);
assert.equal(baseline2024.deltaPercent, null);
const feb = buildAnalysis(records, filter(2026, 2));
assert.equal(feb.monthly.length, 1);
assert.equal(feb.monthly[0].index, 2);
assert.equal(feb.months[0], 2);
const shared = buildAnalysis(records, filter(2026, 0, "Compartido"));
assert.ok(shared.byBranch.every((x) => x.name === "Compartido"));
near(shared.sharedShare, shared.currentTotal ? 100 : 0);
const one = buildAnalysis(records, filter(2026, 0, "Todos", current.byCategory[0].name));
assert.equal(one.byCategory.length, 1);
assert.equal(one.byCategory[0].name, current.byCategory[0].name);
near(one.currentTotal, one.byCategory[0].amount);
assert.deepEqual(
  groupBy([], (r) => r.category),
  [],
);
assert.equal(total([]), 0);
const noData = buildAnalysis(records, filter(2026, 11));
assert.equal(noData.currentTotal, 0);
assert.equal(noData.current.length, 0);
console.log("FINANCIAL_ANALYTICS_MODEL_PASS");
