import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
const component = readFileSync("src/LiveWorkspace.tsx", "utf8");
const css = readFileSync("src/live-workspace.css", "utf8");
for (const field of ["amount", "date", "category", "branch", "account"]) {
  assert.ok(
    component.includes('id="live-draft-' + field + '-error"'),
    field + " needs inline error",
  );
  assert.ok(component.includes("draftIssues?." + field), field + " uses field validation");
}
assert.ok(component.includes('className="live-field-question"'));
assert.ok(component.includes("aria-invalid={Boolean(draftIssues?.amount)}"));
assert.ok(component.includes("aria-invalid={Boolean(draftIssues?.category)}"));
assert.ok(
  !component.includes('className="live-post-hint"'),
  "No inline missing-field list in movements",
);
assert.ok(!component.includes("missingDraftFields.join"), "No summary duplicating field hints");
assert.ok(
  !component.includes("missingPostingFields(row).join"),
  "Table must not list missing fields",
);
assert.ok(
  css.includes(".live-cash-access {") && css.includes("margin: 16px auto 22px;"),
  "Cash launcher must align with centered content",
);
assert.ok(css.includes(".live-draft-editor .live-field-error") && css.includes("color: #a62d37;"));
assert.ok(css.includes('.live-draft-editor [aria-invalid="true"]'));
console.log("F5_CASH_ALIGNMENT_AND_DRAFT_INLINE_GUIDANCE_PASS");
