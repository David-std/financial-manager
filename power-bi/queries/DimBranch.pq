// DimBranch - paste into a separate Power BI Desktop blank query
let
    Source = CommonDataService.Database(
        DataverseHost,
        [CreateNavigationProperties = false]
    ),
    Branches = Value.NativeQuery(
        Source,
        "
        SELECT fmg_branchid, fmg_code, fmg_name, fmg_isactive
        FROM fmg_branch
        ",
        null,
        [EnableFolding = true]
    ),
    Renamed = Table.RenameColumns(
        Branches,
        {
            {"fmg_branchid", "BranchId"},
            {"fmg_code", "BranchCode"},
            {"fmg_name", "BranchName"},
            {"fmg_isactive", "IsActive"}
        }
    )
in
    Renamed
