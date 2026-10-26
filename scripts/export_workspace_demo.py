"""Export *the existing deterministic OFFLINE Power BI demo* for a local UI prototype.

This compiles only the data-generation prefix of the trusted Python demo
function, stopping before any PBIP filesystem / Dataverse operations.
No credentials, API requests, production exports or side effects.
"""
import ast
import inspect
import json
import runpy
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
source = ROOT / "scripts" / "create_power_bi_demo.py"
mod = runpy.run_path(str(source), run_name="__offline_seed_only__")
tree = ast.parse(inspect.getsource(mod["create"]))
fn = tree.body[0]
cut = next(i for i, n in enumerate(fn.body)
           if isinstance(n, ast.Assign) and any(
               isinstance(t, ast.Name) and t.id == "target" for t in n.targets))
fn.name = "generate_rows"
fn.body = fn.body[:cut] + [ast.Return(ast.Tuple(
    elts=[ast.Name(id=s, ctx=ast.Load()) for s in
          ("branches", "accounts", "categories", "rows")], ctx=ast.Load()))]
ast.fix_missing_locations(tree)
scope = dict(mod)
exec(compile(tree, str(source), "exec"), scope)
branches, accounts, categories, rows = scope["generate_rows"]()
assert len(rows) == 310
assert round(sum(r["AmountPEN"] for r in rows), 2) == 509565.12
category_by_id = {c["CategoryId"]: c["CategoryName"] for c in categories}
category_override = {"SERV": "Servicios básicos", "TEC": "Tecnología"}
code_by_id = {c["CategoryId"]: c["CategoryCode"] for c in categories}
account_by_id = {a["CashAccountId"]: a["CashAccountName"] for a in accounts}
branch_by_id = {b["BranchId"]: b["BranchName"] for b in branches}
def clean(s):
    return s.replace(" �� DEMO", " · DEMO").replace(" � DEMO", " · DEMO")
records = []
for r in rows:
    code = code_by_id[r["CategoryId"]]
    records.append({
        "id": r["ExpenseReference"],
        "date": r["TransactionDate"].isoformat(),
        "amount": r["AmountPEN"],
        "category": category_override.get(code, category_by_id[r["CategoryId"]]),
        "categoryCode": code,
        "branch": clean(branch_by_id.get(r["BranchId"], "Compartido")),
        "scope": r["ScopeLabel"],
        "account": clean(account_by_id[r["CashAccountId"]]),
        "flow": r["FlowClassLabel"],
        "status": "Contabilizado",
    })
dest = ROOT / "apps" / "financial-workspace" / "src" / "data" / "demo.json"
dest.parent.mkdir(parents=True, exist_ok=True)
dest.write_text(json.dumps({"demo": True, "currency": "PEN", "count":len(records),
                            "totalPEN": round(sum(r["amount"] for r in records),2),
                            "records":records},ensure_ascii=False,indent=2)+"\n",
                encoding="utf-8")
print("SYNTHETIC_ONLY_COUNT="+str(len(records)))
print("SYNTHETIC_ONLY_TOTAL="+str(round(sum(r["amount"] for r in records),2)))
print("FIXTURE="+str(dest))
