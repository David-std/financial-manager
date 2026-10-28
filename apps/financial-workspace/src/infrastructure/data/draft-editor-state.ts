import type { DataverseDraftResult } from "./conditional-draft";
import type { LiveCollection, LivePage } from "./live-read-repository";
import { formatPenOnBlur } from "./expense-draft-rules";

export function hydrateDraftFields(record: DataverseDraftResult) {
  const id = (key: string) => {
    const value = record[key];
    return typeof value === "string" ? value : "";
  };
  return {
    amount: record.fmg_amount == null ? "" : formatPenOnBlur(String(record.fmg_amount)),
    date: id("fmg_transactiondate").slice(0, 10),
    description: id("fmg_description"),
    reference: id("fmg_documentreference"),
    scopeCode: String(record.fmg_scope ?? 797280000),
    flowCode: String(record.fmg_flowclass ?? 797280000),
    categoryId: id("_fmg_category_value"),
    branchId: id("_fmg_branch_value"),
    accountId: id("_fmg_cashaccount_value"),
  };
}

export async function loadCompleteReferenceCatalog(
  key: LiveCollection,
  reader: Pick<import("./live-read-repository").DelegatedLiveRepository, "readPage">,
): Promise<LivePage> {
  let page = await reader.readPage(key);
  if (!["categories", "branches", "accounts"].includes(key)) return page;
  const rows = [...page.rows];
  const seen = new Set<string>();
  for (let i = 0; page.next && i < 19; i++) {
    if (seen.has(page.next)) throw new Error("La paginación del catálogo se repitió.");
    seen.add(page.next);
    page = await reader.readPage(key, page.next);
    rows.push(...page.rows);
  }
  if (page.next)
    throw new Error(
      "Catálogo demasiado grande para este editor. No guardes selecciones incompletas.",
    );
  return { rows, next: null };
}
