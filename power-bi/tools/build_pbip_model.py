"""Regenerate a Power BI TMDL semantic model from the reviewed Power Query sources.

The PBIR report shell is created with Microsoft's powerbi-report-author CLI.
Do not commit an environment host, user credential, cached row or access token.
"""
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "FinancialManager"
MODEL = PROJECT / "FinancialManager.SemanticModel"
DEF = MODEL / "definition"
COLS = {
    "FactPostedExpense": [
        ("ExpenseId","string",1),("ExpenseReference","string",0),
        ("TransactionDate","dateTime",0),("AmountPEN","decimal",0),
        ("StatusCode","int64",1),("FlowClassCode","int64",0),
        ("ScopeCode","int64",0),("BranchId","string",1),
        ("CategoryId","string",1),("CashAccountId","string",1),
        ("CurrencyId","string",1)],
    "DimCategory": [
        ("CategoryId","string",1),("CategoryCode","string",0),
        ("CategoryName","string",0),("ParentCategoryId","string",1),
        ("IsActive","boolean",1),("IsGroup","boolean",1)],
    "DimBranch": [
        ("BranchId","string",1),("BranchCode","string",0),
        ("BranchName","string",0),("IsActive","boolean",1)],
    "DimCashAccount": [
        ("CashAccountId","string",1),("CashAccountName","string",0),
        ("IsActive","boolean",1),("BranchId","string",1)],
    "DimDate": [
        ("Date","dateTime",0),("Year","int64",0),("Month","int64",0),
        ("YearMonth","string",0),("Quarter","int64",0)]
}
MEASURES = {
    "Paid Spending": ("SUM ( FactPostedExpense[AmountPEN] )","#,##0.00"),
    "Payment Count": ("COUNTROWS ( FactPostedExpense )","#,##0"),
    "Average Payment": ("DIVIDE ( [Paid Spending], [Payment Count] )","#,##0.00"),
    "Operating Spending": ("CALCULATE ( [Paid Spending], FactPostedExpense[FlowClassCode] = 797280000 )","#,##0.00"),
    "Capital Spending": ("CALCULATE ( [Paid Spending], FactPostedExpense[FlowClassCode] = 797280001 )","#,##0.00"),
    "Tax Spending": ("CALCULATE ( [Paid Spending], FactPostedExpense[FlowClassCode] = 797280002 )","#,##0.00"),
    "Other Spending": ("CALCULATE ( [Paid Spending], FactPostedExpense[FlowClassCode] = 797280003 )","#,##0.00"),
    "Shared Spending": ("CALCULATE ( [Paid Spending], FactPostedExpense[ScopeCode] = 797280001 )","#,##0.00"),
    "Branch Spending": ("CALCULATE ( [Paid Spending], FactPostedExpense[ScopeCode] = 797280000 )","#,##0.00"),
    "Prior Year Spending": ("CALCULATE ( [Paid Spending], SAMEPERIODLASTYEAR ( DimDate[Date] ) )","#,##0.00"),
    "Year-over-Year Change": ("IF ( ISBLANK ( [Prior Year Spending] ), BLANK (), [Paid Spending] - [Prior Year Spending] )","#,##0.00"),
    "Year-over-Year Change %": ("DIVIDE ( [Year-over-Year Change], [Prior Year Spending] )","0.0%")
}
def save(path, content):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(content.rstrip()+"\n",encoding="utf-8",newline="\n")

def msource(table):
    path = ROOT / ("posted-expenses.pq" if table == "FactPostedExpense" else "queries/"+table+".pq")
    text = path.read_text(encoding="utf-8-sig")
    text = re.sub(r"(?m)^\(separate query[^\n]*\)\s*$","",text)
    match = re.search(r"(?m)^let\s*$",text)
    if not match: raise ValueError("M missing let: "+str(path))
    result=text[match.start():].strip()
    if not re.search(r"(?m)^in\s*$", result): raise ValueError("M missing in")
    if table=="DimDate": return result
    upstream = "NamedColumns" if table=="FactPostedExpense" else "Renamed"
    keys = {
        "FactPostedExpense":["ExpenseId","BranchId","CategoryId","CashAccountId","CurrencyId"],
        "DimCategory":["CategoryId","ParentCategoryId"],
        "DimBranch":["BranchId"],
        "DimCashAccount":["CashAccountId","BranchId"]
    }[table]
    types = {
        "FactPostedExpense":[("ExpenseReference","type text"),("TransactionDate","type date"),("AmountPEN","Currency.Type"),("StatusCode","Int64.Type"),("FlowClassCode","Int64.Type"),("ScopeCode","Int64.Type")],
        "DimCategory":[("CategoryCode","type text"),("CategoryName","type text"),("IsActive","type logical"),("IsGroup","type logical")],
        "DimBranch":[("BranchCode","type text"),("BranchName","type text"),("IsActive","type logical")],
        "DimCashAccount":[("CashAccountName","type text"),("IsActive","type logical")]
    }[table]
    conversions=",\n".join('            {"'+name+'", each if _ = null then null else Text.Lower(Text.From(_)), type text}' for name in keys)
    type_pairs=",\n".join('            {"'+name+'", '+dtype+'}' for name,dtype in types)
    follow = ("\n    KeysAsText = Table.TransformColumns(\n        "+upstream+",\n        {\n"+conversions+"\n        }\n    ),"
              "\n    Typed = Table.TransformColumnTypes(\n        KeysAsText,\n        {\n"+type_pairs+"\n        }\n    )")
    marker="    )\nin\n    "+upstream
    if result.count(marker)!=1: raise ValueError("Unexpected M end: "+table)
    return result.replace(marker,"    ),"+follow+"\nin\n    Typed")

def build():
    if not (PROJECT/"FinancialManager.pbip").exists():
        raise ValueError("Scaffold report with the official authoring CLI first")
    save(MODEL/"definition.pbism",json.dumps({
        "$schema":"https://developer.microsoft.com/json-schemas/fabric/item/semanticModel/definitionProperties/1.0.0/schema.json",
        "version":"4.2","settings":{"qnaEnabled":False}},indent=2))
    save(DEF/"database.tmdl","database FinancialManager\n\tcompatibilityLevel: 1702\n\tcompatibilityMode: powerBI\n\tlanguage: 3082")
    refs="\n".join("ref table "+name for name in COLS)
    save(DEF/"model.tmdl","model Model\n\tculture: es-ES\n\tsourceQueryCulture: es-ES\n\tdefaultPowerBIDataSourceVersion: powerBI_V3\n\n"+refs+
         "\n\n"+'expression DataverseHost = "SET_DATAVERSE_HOST" meta [IsParameterQuery=true, Type="Text", IsParameterQueryRequired=true]')
    joins=[("Date","TransactionDate","DimDate","Date"),("Category","CategoryId","DimCategory","CategoryId"),
           ("Branch","BranchId","DimBranch","BranchId"),("Cash Account","CashAccountId","DimCashAccount","CashAccountId")]
    save(DEF/"relationships.tmdl","\n\n".join(
         "relationship 'Expense "+label+"'\n\tfromColumn: FactPostedExpense."+foreign+
         "\n\ttoColumn: "+dim+"."+primary+"\n\tcrossFilteringBehavior: oneDirection"
         for label,foreign,dim,primary in joins))
    for table,fields in COLS.items():
        lines=["table "+table]
        if table=="DimDate":lines.append("\tdataCategory: Time")
        if table=="FactPostedExpense":
            for name,(dax,fmt) in MEASURES.items():
                lines.append("\n\tmeasure '"+name+"' = "+dax+"\n\t\tformatString: "+fmt)
        for name,dtype,hidden in fields:
            lines.append("\n\tcolumn "+name+"\n\t\tdataType: "+dtype+"\n"+
                         ("\t\tisHidden\n" if hidden else "")+
                         "\t\tsourceColumn: "+name+"\n\t\tsummarizeBy: none")
        source=msource(table)
        lines.append("\n\tpartition "+table+" = m\n\t\tmode: import\n\t\tsource =\n"+
                     "\n".join("\t\t\t"+row for row in source.splitlines()))
        save(DEF/"tables"/(table+".tmdl"),"\n".join(lines))
    print("GENERATED_TMDL_TABLES="+str(len(COLS)))
    print("GENERATED_MEASURES="+str(len(MEASURES)))
    print("MODEL_PATH="+str(MODEL))
if __name__=="__main__":build()
