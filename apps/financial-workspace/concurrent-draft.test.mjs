import assert from "node:assert/strict";
import { build } from "esbuild";

const compiled = await build({
  entryPoints: ["src/infrastructure/data/conditional-draft.ts"],
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
});
const source =
  "data:text/javascript;base64," + Buffer.from(compiled.outputFiles[0].contents).toString("base64");
const { loadDraftForEdit, conditionalDraftSave, DraftWriteConflict } = await import(source);

const id = "12345678-1234-4234-9234-123456789012";
const userA = { authenticated: true, principalId: "aaaaaaaa-1234-4234-9234-aaaaaaaaaaaa" };
const userB = { authenticated: true, principalId: "bbbbbbbb-1234-4234-9234-bbbbbbbbbbbb" };
const row = {
  fmg_financialtransactionid: id,
  fmg_status: 797280000,
  fmg_description: "Original",
  rev: 31,
};
const observed = [];
const transport = {
  expenseEntitySet: "fmg_financialtransactions",
  async request(req) {
    observed.push({
      method: req.method,
      actor: req.context.principalId,
      match: req.headers["If-Match"],
    });
    const etag = `W/"${row.rev}"`;
    if (req.method === "GET") {
      return {
        status: 200,
        data: {
          fmg_financialtransactionid: row.fmg_financialtransactionid,
          fmg_status: row.fmg_status,
          fmg_description: row.fmg_description,
          "@odata.etag": etag,
        },
      };
    }
    if (req.headers["If-Match"] !== etag) return { status: 412 };
    if ("fmg_status" in req.body) return { status: 403 };
    row.fmg_description = req.body.fmg_description;
    row.rev++;
    return { status: 204 };
  },
};
const editorA = await loadDraftForEdit(transport, userA, id);
const editorB = await loadDraftForEdit(transport, userB, id);
assert.equal(editorA.version.etag, editorB.version.etag);
const saved = await conditionalDraftSave(transport, userA, editorA.version, {
  fmg_description: "Primera edición",
});
assert.equal(saved.fmg_description, "Primera edición");
assert.equal(saved["@odata.etag"], 'W/"32"');
await assert.rejects(
  conditionalDraftSave(transport, userB, editorB.version, { fmg_description: "Perder cambio" }),
  DraftWriteConflict,
);
assert.equal(row.fmg_description, "Primera edición", "The stale second writer cannot overwrite");
assert.equal(row.rev, 32, "Only one PATCH can change the row");
assert.equal(observed.filter((x) => x.method === "PATCH").length, 2, "Second write reached 412");
const retry = await loadDraftForEdit(transport, userB, id);
assert.equal(retry.version.etag, 'W/"32"');
const retryResult = await conditionalDraftSave(transport, userB, retry.version, {
  fmg_description: "Edición tras recarga",
});
assert.equal(retryResult.fmg_description, "Edición tras recarga");
assert.equal(row.rev, 33);
await assert.rejects(loadDraftForEdit(transport, null, id), /no dispone de identidad/);
await assert.rejects(loadDraftForEdit(transport, userA, "not-a-guid"), /identidad del borrador/);
console.log("CONCURRENT_DRAFT_SIMULATION_PASS");
