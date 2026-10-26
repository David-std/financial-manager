/** Analytic definitions for *confirmed expense payments only*. No profit, income, balances or approved budgets. */
export type AnalyticPayment = {
  id: string;
  date: string;
  amount: number;
  category: string;
  branch: string;
  scope: string;
  account: string;
  flow: string;
  status: string;
};
export type AnalyticFilter = { year: number; month: number; scope: string; category: string };
export type AmountGroup = { name: string; amount: number; count: number };
export type MonthlyPoint = {
  index: number;
  name: string;
  current: number;
  prior: number | null;
  count: number;
};
export type Driver = { name: string; current: number; prior: number; delta: number };
const months = ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];
export function total(rows: AnalyticPayment[]): number {
  return rows.reduce((sum, r) => sum + r.amount, 0);
}
export function groupBy(
  rows: AnalyticPayment[],
  pick: (p: AnalyticPayment) => string,
): AmountGroup[] {
  const map = new Map<string, { amount: number; count: number }>();
  for (const row of rows) {
    const name = pick(row) || "Sin asignar";
    const x = map.get(name) ?? { amount: 0, count: 0 };
    map.set(name, { amount: x.amount + row.amount, count: x.count + 1 });
  }
  return [...map]
    .map(([name, v]) => ({ name, ...v }))
    .sort((a, b) => b.amount - a.amount || a.name.localeCompare(b.name, "es"));
}
function scoped(r: AnalyticPayment, filter: AnalyticFilter) {
  return (
    (filter.scope === "Todos" || r.scope === filter.scope) &&
    (filter.category === "Todas" || r.category === filter.category)
  );
}
function inPeriod(r: AnalyticPayment, year: number, monthsAllowed: number[]) {
  return Number(r.date.slice(0, 4)) === year && monthsAllowed.includes(Number(r.date.slice(5, 7)));
}
export function buildAnalysis(records: AnalyticPayment[], filter: AnalyticFilter) {
  const posted = records.filter((r) => r.status === "Contabilizado");
  const maxMonthInYear = Math.max(
    0,
    ...posted
      .filter((r) => Number(r.date.slice(0, 4)) === filter.year)
      .map((r) => Number(r.date.slice(5, 7))),
  );
  const relevantMonths = filter.month
    ? [filter.month]
    : Array.from({ length: maxMonthInYear }, (_, i) => i + 1);
  const current = posted.filter(
    (r) => scoped(r, filter) && inPeriod(r, filter.year, relevantMonths),
  );
  const eligiblePrior = filter.year > 2024;
  const prior = eligiblePrior
    ? posted.filter((r) => scoped(r, filter) && inPeriod(r, filter.year - 1, relevantMonths))
    : [];
  const currentTotal = total(current);
  const previousTotal = total(prior);
  const delta = eligiblePrior ? currentTotal - previousTotal : null;
  const deltaPercent =
    eligiblePrior && previousTotal > 0
      ? ((currentTotal - previousTotal) / previousTotal) * 100
      : null;
  const byCategory = groupBy(current, (r) => r.category);
  const byAccount = groupBy(current, (r) => r.account);
  const byBranch = groupBy(current, (r) => (r.scope === "Compartido" ? "Compartido" : r.branch));
  const byFlow = groupBy(current, (r) => r.flow);
  const previousCategory = new Map(
    groupBy(prior, (r) => r.category).map((x) => [x.name, x.amount]),
  );
  const drivers: Driver[] = [
    ...new Set([...byCategory.map((x) => x.name), ...previousCategory.keys()]),
  ]
    .map((name) => {
      const currentAmount = byCategory.find((x) => x.name === name)?.amount ?? 0;
      const priorAmount = previousCategory.get(name) ?? 0;
      return {
        name,
        current: currentAmount,
        prior: priorAmount,
        delta: currentAmount - priorAmount,
      };
    })
    .sort((a, b) => Math.abs(b.delta) - Math.abs(a.delta));
  const monthly: MonthlyPoint[] = relevantMonths.map((n) => {
    const currentRows = current.filter((r) => Number(r.date.slice(5, 7)) === n);
    const priorRows = prior.filter((r) => Number(r.date.slice(5, 7)) === n);
    return {
      index: n,
      name: months[n - 1],
      current: total(currentRows),
      prior: eligiblePrior ? total(priorRows) : null,
      count: currentRows.length,
    };
  });
  const coverageYears = [2024, 2025, 2026];
  const heatmap = coverageYears.map((year) => ({
    year,
    months: months.map((name, index) => {
      const eligible =
        year < 2026 ||
        index + 1 <=
          Math.max(
            0,
            ...posted
              .filter((r) => Number(r.date.slice(0, 4)) === 2026)
              .map((r) => Number(r.date.slice(5, 7))),
          );
      const rows = posted.filter((r) => scoped(r, filter) && inPeriod(r, year, [index + 1]));
      return {
        name,
        index: index + 1,
        value: eligible ? total(rows) : null,
        count: eligible ? rows.length : null,
      };
    }),
  }));
  const topThree = byCategory.slice(0, 3).reduce((sum, x) => sum + x.amount, 0);
  const shared = total(current.filter((r) => r.scope === "Compartido"));
  const high = [...current].sort((a, b) => b.amount - a.amount || a.id.localeCompare(b.id));
  const threshold = high.length
    ? (high[Math.max(0, Math.ceil(high.length * 0.1) - 1)]?.amount ?? 0)
    : 0;
  // Largest decile by *rank*, not a fraud/risk flag.
  const largest = high.slice(0, Math.max(1, Math.ceil(high.length * 0.1)));
  const peak = monthly.length ? [...monthly].sort((a, b) => b.current - a.current)[0] : null;
  return {
    current,
    prior,
    byCategory,
    byAccount,
    byBranch,
    byFlow,
    drivers,
    monthly,
    heatmap,
    currentTotal,
    previousTotal,
    delta,
    deltaPercent,
    eligiblePrior,
    months: relevantMonths,
    coverageEnd: relevantMonths.at(-1) ?? 0,
    topThreeShare: currentTotal ? (topThree / currentTotal) * 100 : 0,
    sharedShare: currentTotal ? (shared / currentTotal) * 100 : 0,
    ticket: current.length ? currentTotal / current.length : 0,
    peak,
    largest,
    threshold,
  };
}
export const ANALYTIC_MONTHS = months;
