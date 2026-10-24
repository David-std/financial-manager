// Power Query M starter for the first posted-expense fact.
// Create the text parameter DataverseHost locally in Power BI Desktop.
// This source file has not yet been evaluated against the Dataverse TDS endpoint.
let
    Source = CommonDataService.Database(
        DataverseHost,
        [CreateNavigationProperties = false]
    ),
    PostedRows = Value.NativeQuery(
        Source,
        "
        SELECT
            fmg_financialtransactionid,
            fmg_name,
            fmg_transactiondate,
            fmg_amount,
            fmg_status,
            fmg_flowclass,
            fmg_scope,
            fmg_branch,
            fmg_category,
            fmg_cashaccount,
            expense.transactioncurrencyid
        FROM fmg_financialtransaction AS expense
        INNER JOIN transactioncurrency AS currency
            ON expense.transactioncurrencyid = currency.transactioncurrencyid
        WHERE expense.fmg_status = 797280001
          AND currency.isocurrencycode = 'PEN'
        ",
        null,
        [EnableFolding = true]
    ),
    NamedColumns = Table.RenameColumns(
        PostedRows,
        {
            {"fmg_financialtransactionid", "ExpenseId"},
            {"fmg_name", "ExpenseReference"},
            {"fmg_transactiondate", "TransactionDate"},
            {"fmg_amount", "AmountPEN"},
            {"fmg_status", "StatusCode"},
            {"fmg_flowclass", "FlowClassCode"},
            {"fmg_scope", "ScopeCode"},
            {"fmg_branch", "BranchId"},
            {"fmg_category", "CategoryId"},
            {"fmg_cashaccount", "CashAccountId"},
            {"transactioncurrencyid", "CurrencyId"}
        }
    )
in
    NamedColumns
