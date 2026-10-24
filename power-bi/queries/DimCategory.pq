// DimCategory - paste into a separate Power BI Desktop blank query
let
    Source = CommonDataService.Database(
        DataverseHost,
        [CreateNavigationProperties = false]
    ),
    Categories = Value.NativeQuery(
        Source,
        "
        SELECT fmg_categoryid, fmg_code, fmg_name,
               fmg_parentcategory, fmg_isactive, fmg_isgroup
        FROM fmg_category
        ",
        null,
        [EnableFolding = true]
    ),
    Renamed = Table.RenameColumns(
        Categories,
        {
            {"fmg_categoryid", "CategoryId"},
            {"fmg_code", "CategoryCode"},
            {"fmg_name", "CategoryName"},
            {"fmg_parentcategory", "ParentCategoryId"},
            {"fmg_isactive", "IsActive"},
            {"fmg_isgroup", "IsGroup"}
        }
    )
in
    Renamed
