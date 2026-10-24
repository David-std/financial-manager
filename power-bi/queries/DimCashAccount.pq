// DimCashAccount - paste into a separate Power BI Desktop blank query
let
    Source = CommonDataService.Database(
        DataverseHost,
        [CreateNavigationProperties = false]
    ),
    Accounts = Value.NativeQuery(
        Source,
        "
        SELECT fmg_cashaccountid, fmg_name, fmg_isactive, fmg_branch
        FROM fmg_cashaccount
        ",
        null,
        [EnableFolding = true]
    ),
    Renamed = Table.RenameColumns(
        Accounts,
        {
            {"fmg_cashaccountid", "CashAccountId"},
            {"fmg_name", "CashAccountName"},
            {"fmg_isactive", "IsActive"},
            {"fmg_branch", "BranchId"}
        }
    )
in
    Renamed
