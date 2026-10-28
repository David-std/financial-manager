/** Lightweight UI preflight only. Dataverse remains final authority. */
export function formatPenOnBlur(raw: string): string {
  const value = raw.trim().replace(",", ".");
  if (!/^(?:0|[1-9]\d*)(?:\.\d{0,2})?$/.test(value)) return raw;
  const amount = Number(value);
  if (!Number.isFinite(amount) || amount > 999999999999) return raw;
  return amount.toFixed(2);
}

export function missingPostingFields(row: Readonly<Record<string, unknown>>): string[] {
  const missing: string[] = [];
  const amount = row.fmg_amount;
  if (
    typeof amount !== "number" ||
    !Number.isFinite(amount) ||
    amount <= 0 ||
    Math.abs(amount * 100 - Math.round(amount * 100)) > 1e-7
  )
    missing.push("importe positivo en PEN");
  if (
    typeof row.fmg_transactiondate !== "string" ||
    !/^\d{4}-\d{2}-\d{2}/.test(row.fmg_transactiondate)
  )
    missing.push("fecha");
  if (![797280000, 797280001, 797280002, 797280003].includes(Number(row.fmg_flowclass)))
    missing.push("clase de gasto");
  if (typeof row._fmg_category_value !== "string" || !row._fmg_category_value)
    missing.push("categoría");
  if (typeof row._fmg_cashaccount_value !== "string" || !row._fmg_cashaccount_value)
    missing.push("cuenta");
  if (typeof row._transactioncurrencyid_value !== "string" || !row._transactioncurrencyid_value)
    missing.push("moneda PEN");
  const scope = row.fmg_scope;
  const branch = row._fmg_branch_value;
  if (scope === 797280000 && (typeof branch !== "string" || !branch)) missing.push("sede");
  else if (scope === 797280001 && branch) missing.push("eliminar la sede del gasto compartido");
  else if (scope !== 797280000 && scope !== 797280001) missing.push("ámbito");
  return missing;
}
