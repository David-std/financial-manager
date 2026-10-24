// DimDate - paste into a separate Power BI Desktop blank query
(separate query; inclusive, without private source data)
let
    Start = #date(2020, 1, 1),
    Finish = #date(2035, 12, 31),
    Days = Duration.Days(Finish - Start) + 1,
    AsList = List.Dates(Start, Days, #duration(1, 0, 0, 0)),
    AsTable = Table.FromList(AsList, Splitter.SplitByNothing(), {"Date"}),
    Typed = Table.TransformColumnTypes(AsTable, {{"Date", type date}}),
    Year = Table.AddColumn(Typed, "Year", each Date.Year([Date]), Int64.Type),
    Month = Table.AddColumn(Year, "Month", each Date.Month([Date]), Int64.Type),
    YearMonth = Table.AddColumn(
        Month, "YearMonth", each Date.ToText([Date], "yyyy-MM"), type text
    ),
    Quarter = Table.AddColumn(
        YearMonth, "Quarter", each Date.QuarterOfYear([Date]), Int64.Type
    )
in
    Quarter
