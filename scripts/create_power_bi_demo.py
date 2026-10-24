"""Create an OFFLINE, disposable PBIP demo copy with deterministic invented data.

Does not connect to or mutate Dataverse, does not write samples into Git.
Usage: python scripts/create_power_bi_demo.py
"""
from __future__ import annotations
import datetime as dt
import pathlib
import random
import shutil
import tempfile
import uuid

ROOT=pathlib.Path(__file__).resolve().parents[1]
SOURCE=ROOT/"power-bi"/"FinancialManager"
MODEL_PART="FinancialManager.SemanticModel/definition/tables"
NS=uuid.UUID("d0a7d1f0-8b95-48c2-a03b-917011013013")

def ident(group,code):
    return str(uuid.uuid5(NS,f"SYNTHETIC-{group}-{code}"))
def escaped(s):
    return '"'+str(s).replace('"','""')+'"'
def mvalue(value):
    if value is None:
        return "null"
    if isinstance(value,bool):
        return str(value).lower()
    if isinstance(value,dt.date):
        return f"#date({value.year},{value.month},{value.day})"
    if isinstance(value,(float,int)):
        return str(value)
    return escaped(value)
def make_m(rows,typing):
    assert rows
    columns=list(rows[0])
    for r in rows:
        assert set(r)==set(columns)
    data=",\n".join(
        "                ["+", ".join(f"{key}={mvalue(record[key])}" for key in columns)+"]"
        for record in rows
    )
    types=", ".join("{"+escaped(c)+", "+t+"}" for c,t in typing.items())
    return (
        "let\n"
        "    SyntheticRows = {\n"+data+"\n    },\n"
        "    Data = Table.FromRecords(SyntheticRows),\n"
        f"    Typed = Table.TransformColumnTypes(Data, {{{types}}})\n"
        "in\n"
        "    Typed"
    )
def override(dst,table,source):
    path=dst/MODEL_PART/(table+".tmdl")
    old=path.read_text(encoding="utf-8")
    needle="\t\tsource ="
    assert old.count(needle)==1,(table,old.count(needle))
    before=old.split(needle,1)[0]
    indented="\n".join("\t\t\t"+line for line in source.splitlines())
    path.write_text(before+needle+"\n"+indented+"\n",encoding="utf8")
def create():
    rng=random.Random(31072026)
    branches=[
        dict(BranchId=ident("branch","LAM"),BranchCode="LAM",BranchName="Sucursal Norte · DEMO",IsActive=True),
        dict(BranchId=ident("branch","RAM"),BranchCode="RAM",BranchName="Sucursal Sur · DEMO",IsActive=True),
    ]
    accounts=[
        dict(CashAccountId=ident("acct","CAJA1"),CashAccountName="Caja principal · DEMO",IsActive=True,BranchId=branches[0]["BranchId"]),
        dict(CashAccountId=ident("acct","CAJA2"),CashAccountName="Caja secundaria · DEMO",IsActive=True,BranchId=branches[1]["BranchId"]),
        dict(CashAccountId=ident("acct","BCO"),CashAccountName="Banco operativo · DEMO",IsActive=True,BranchId=None),
    ]
    labels=[
        ("PERSONAL","Personal",1.4),
        ("ALQUILER","Alquiler de locales",1.3),
        ("SERV","Servicios básicos",.8),
        ("MOV","Movilidad y transporte",.65),
        ("EQUIP","Equipamiento",1.1),
        ("TEC","Tecnología",.7),
        ("MANT","Mantenimiento",.7),
        ("IMP","Tributos",.85),
        ("OTROS","Otros gastos",.45)
    ]
    categories=[]
    for code,label,weight in labels:
        categories.append(dict(CategoryId=ident("category",code),
                               CategoryCode=code,CategoryName=label,
                               ParentCategoryId=None,IsActive=True,IsGroup=False))
    rows=[]
    for idx in range(310):
        year=rng.choices([2024,2025,2026],[20,47,33])[0]
        month=rng.randint(1,12 if year!=2026 else 10)
        day=rng.randint(1,25)
        when=dt.date(year,month,day)
        scope=rng.choices([797280000,797280001],[0.79,0.21])[0]
        branch=rng.choice(branches)["BranchId"] if scope==797280000 else None
        account=rng.choice(accounts)["CashAccountId"]
        weights=[k[2] for k in labels]
        catindex=rng.choices(range(len(labels)),weights)[0]
        category=categories[catindex]
        flow=797280002 if labels[catindex][0]=="IMP" else (797280001 if labels[catindex][0] in ("EQUIP","TEC") else 797280000)
        amount=round(rng.uniform(220,2000)*labels[catindex][2]*rng.choice([.7,1,1.2,1.5,2.5]),2)
        rows.append(dict(
            ExpenseId=ident("expense",f"{idx:05}"),
            ExpenseReference=f"DEMO-EXP-{idx+1:04}",
            TransactionDate=when,
            AmountPEN=amount,
            StatusCode=797280001,
            FlowClassCode=flow,
            ScopeCode=scope,
            BranchId=branch,CategoryId=category["CategoryId"],
            CashAccountId=account,CurrencyId=ident("currency","PEN"),
            FlowClassLabel={797280000:"Operativo",797280001:"Capital",797280002:"Impuestos"}[flow],
            ScopeLabel="Compartido" if scope==797280001 else "Por sede"
        ))
    target=pathlib.Path(tempfile.mkdtemp(prefix="FM-EXCLUSIVAMENTE-DEMO-SINTETICA-"))
    shutil.copytree(SOURCE,target/"FinancialManager")
    dst=target/"FinancialManager"
    override(dst,"DimBranch",make_m(branches,{"BranchId":"type text","BranchCode":"type text","BranchName":"type text","IsActive":"type logical"}))
    override(dst,"DimCashAccount",make_m(accounts,{"CashAccountId":"type text","CashAccountName":"type text","IsActive":"type logical","BranchId":"type text"}))
    override(dst,"DimCategory",make_m(categories,{"CategoryId":"type text","CategoryCode":"type text","CategoryName":"type text","ParentCategoryId":"type text","IsActive":"type logical","IsGroup":"type logical"}))
    override(dst,"FactPostedExpense",make_m(rows,{
        "ExpenseId":"type text","ExpenseReference":"type text",
        "TransactionDate":"type date","AmountPEN":"Currency.Type",
        "StatusCode":"Int64.Type","FlowClassCode":"Int64.Type",
        "ScopeCode":"Int64.Type","BranchId":"type text",
        "CategoryId":"type text","CashAccountId":"type text",
        "CurrencyId":"type text","FlowClassLabel":"type text","ScopeLabel":"type text"}))
    (target/"SOLO_DEMO_NO_PUBLICAR.txt").write_text(
        "FINANCIAL MANAGER / SÓLO DEMOSTRACIÓN.\n"
        "Datos 100% inventados y generados por algoritmo. No constituyen gastos reales.\n"
        "NO publicar en workspace financiero, NO copiar sobre el PBIP original, NO registrar en Dataverse.\n"
        "No compartir capturas como importes operativos.\n",encoding="utf8")
    print("OFFLINE_DEMO_PBIP="+str(dst/"FinancialManager.pbip"))
    print("DEMO_RECORD_COUNT="+str(len(rows)))
    print("DEMO_AMOUNT_TOTAL="+str(round(sum(r["AmountPEN"] for r in rows),2)))
    print("DEMO_YEAR_COUNTS="+str({y:sum(r["TransactionDate"].year==y for r in rows) for y in [2024,2025,2026]}))
    print("NO_DATA_WRITTEN_TO_DATAVERSE")
    return dst

if __name__=="__main__":
    create()
