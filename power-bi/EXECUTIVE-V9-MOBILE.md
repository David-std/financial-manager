# Executive v9 — Mobile UX audit

25 October 2026 · **Mobile-only iteration** based on `develop@2b49402`.

## Scope

Preserve the approved desktop report, all 31 DAX measures, Power Apps, Dataverse and budget-approval safeguards. All authored layout changes in this iteration reside in `mobile.json` beside each PBIR `visual.json`. The mobile pass is repeatable by running `python scripts/polish_power_bi_mobile_v9.py`. Re-running `python scripts/build_power_bi_v8.py` also invokes the v9 mobile pass automatically.

The native Power BI Mobile layout is selected automatically in portrait orientation in the mobile apps, not when viewing in a phone browser. Landscape shows the desktop report. [Microsoft mobile-optimized reports](https://learn.microsoft.com/en-us/power-bi/create-reports/desktop-create-phone-report).

## Findings from v8 and fixes

| Finding | Correction |
|---|---|
| Five slicers occupy three rows, with the fifth orphaned at half width | Two paired rows, then one full-width selector |
| KPI values and long descriptions cramped in two 158-pt cards | Main financial KPI at full 323 pt; remaining KPIs have responsive font-size overrides and more vertical space |
| Desktop chart titles too long for mobile | Shortened **only in mobile layout**, enabled wrapping for visual titles |
| *Tendencias y estacionalidad* heading scrolls inside its narrow phone textbox | Original heading text preserved, but mobile-only font lowered from 21 pt to 16 pt; no desktop typography change |
| Dense bar charts show too few rows | Expanded the 9-category rankings to 345–362 pt; extended monthly composite plots |
| 9-column year/month matrices unreadable on portrait phone | Omitted from portrait; accessible by rotating to the full desktop/landscape report; supporting charts retained |
| Color-only legend or unexplained chart | Mobile legend at bottom for 100%-stacked and area mix; altText for mobile visual contexts |
| Small fonts and inconsistent padding | Mobile-only font-size, column widths, axis label and padding overrides |
| Rebuilding v8 loses refined phone arrangements | v8 builder invokes the v9 mobile polish as its final step |

## Layout inventory

| Page | Mobile objects | Critical sequence |
|---|---:|---|
| 01 Panorama de gastos | 19 | Main KPI → filters → supporting KPIs → spending trend → category rank → shared spending → payment detail |
| 02 Sedes y movimientos | 16 | Main KPI → filters → remaining KPIs → branches/accounts → monthly seasonality → operational entries |
| 03 Análisis de categorías | 15 | Main KPI → filters → comparisons → category ranking → evolution → ticket and payment frequency |
| 04 Tendencias de pagos | 17 | Main KPI → filters → annual/monthly series → composition → scope → flow-class comparisons |

**Note about dense transaction tables:** The two detailed operational tables remain at the end of their phone pages; they require horizontal scrolling for complete fields. Critical analytic amounts are available in preceding mobile charts.

## Tests and limitations

1. `powerbi-report-author validate power-bi/FinancialManager/FinancialManager.pbip` — schema/structure and semantic bindings.
2. `python -m unittest discover -s tests -q` — mobile non-overlap, tap-target-sized visual heights, full-width leading KPI and fifth slicer, four phone layouts, alt text and mobile-specific formatting, retained measures, no source visual mutation.
3. `python scripts/check_repository.py` — synthetic data and security guards.
4. Open an **isolated** 310-record demo via `python scripts/create_power_bi_demo.py` and refresh in Power BI Desktop. Inspect phone layout using the phone icon at the bottom-left or **View → Mobile layout**. Check every scroll range and apply year/category/scope slicers before accepting behavior.
5. Test on the Microsoft Power BI mobile app for touch and screen-reader accessibility before public distribution. Schema validation alone does not demonstrate full touch functionality. Mobile app has limited tooltip behavior in authoring preview.

There are no Dataverse writes, no real financial data copied to Git, no approvals exposed, and no changes to `main`. Desktop page, chart, and semantic definitions must remain byte-for-byte identical to the prior v8 commit.

This is a **mobile UX improvement** rather than a new financial functionality release.
