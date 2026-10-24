# Financial Manager · Power BI Executive v2

## Alcance y decisiones vigentes

**Punto de continuación:** `develop`; modelo Import en PBIP/PBIR/TMDL, con conexión parametrizada a Dataverse. Este documento es la referencia de mantenimiento y aceptación de la versión ejecutiva del informe, no un entregable de marketing. Preservar el repositorio como fuente de verdad.

**Finalidad:** análisis gerencial de gastos **contabilizados (Posted) y denominados en PEN**. No presentar ingresos, rentabilidad, saldo bancario ni ejecución de presupuestos no aprobados. No crear movimientos reales para alimentar visuales ni modificar el servidor durante el diseño.

**Generación:** `python scripts/build_power_bi_v2.py`. El script crea respaldo local temporal de la definición previa, impide sobrescribir visuales desconocidos y reconstituye deterministicamente los visuales nativos sin publicar ni enviar datos. El parámetro `DataverseHost` debe conservarse como `SET_DATAVERSE_HOST` en Git. Para pruebas de Desktop, abrir una copia temporal y establecer allí el host de desarrollo.

## Sistema visual

- Lienzo 1280 × 720, fondo claro `#F3F7FB`; superficie blanca `#FFFFFF`, títulos azul petróleo `#143451`, texto `#152940`, divisores `#DCE6F1`.
- Paleta de gráficos: azul `#2878B8`, verde azulado `#129D98`, ámbar `#E3A948` y acentos moderados.
- Franja superior de identidad y descripción, cuatro filtros desplegables por página, tarjetas ejecutivas, gráficos complementarios y registro de detalle donde agregue valor.
- Etiquetas visibles en español, sin códigos de Choice en las tablas. Contraste, títulos legibles y elementos dentro del lienzo. No depender solamente del color para expresar una señal financiera.
- Las métricas de volumen/monto sin transacciones se muestran como 0; la variación interanual sin periodo anterior **no debe** tratarse como 0 % medido: el texto indica `Sin base anterior`.

## Páginas implementadas

| Página | Segmentadores | KPI principales | Investigación y detalle |
| --- | --- | --- | --- |
| Resumen de gastos | Año, sede, categoría, ámbito | Total en PEN, cantidad, compartidos, ticket promedio | Serie mensual, distribución por categoría, estado de la fuente, movimientos |
| Sedes y movimientos | Año, sede, cuenta, ámbito | Total, monto por sede, compartidos, participación compartida | Comparación por sede/cuenta; registro filtrable de transacciones |
| Análisis de categorías | Año, categoría, sede, tipo | Total, periodo anterior, lectura interanual, categorías con pagos | Tabla de crecimiento y participación; concentración por categoría |
| Tendencias de pagos | Año, mes, clase de flujo, ámbito | Total, comparación interanual, acumulado, promedio mensual | Evolución mensual, comparación por años y descomposición por tipo |

## Contrato de datos, filtros y límites

- El hecho `FactPostedExpense` sigue filtrando en origen solo `fmg_status = 797280001` y `currency.isocurrencycode = 'PEN'`. Sin filas elegibles, el informe informa ausencia de datos; nunca se sustituye una transacción por un valor ficticio.
- Conserva dimensiones `DimDate`, `DimCategory`, `DimBranch`, `DimCashAccount` y las cuatro relaciones unidireccionales.
- `FlowClassLabel` y `ScopeLabel` traducen valores de elección y se derivan dentro de Power Query, no crean nuevas entidades de Dataverse.
- `Pagos confirmados KPI`, `Cantidad de pagos KPI`, `Ticket promedio KPI`, `Gasto compartido KPI`, etc., preservan medidas base y añaden visualización sin blancos. Las diferencias interanuales solo son interpretables cuando existe un año comparable.
- Los gastos compartidos carecen de sede asignada en el hecho; **no se distribuyen ni se duplican** arbitrariamente entre sedes. El valor `Gasto por sede` es distinto del consolidado.
- El porcentaje de categoría respeta filtros explícitos mediante `ALLSELECTED`; se calcula sobre el total de categorías visibles y debe aceptarse con datos positivos.
- Los nombres técnicos DAX se conservan solo cuando sean necesarios para compatibilidad de modelo; las etiquetas de visualización se traducen mediante `nativeQueryRef`.
- La interactividad natural de gráficos y segmentadores nativos de Power BI requiere revisión final de interacciones y accesibilidad desde Desktop.

## Validaciones obligatorias

1. `python -m unittest discover -s tests -q` y `powerbi-report-author validate power-bi/FinancialManager/FinancialManager.pbip`, sin errores ni advertencias.
2. Abrir copia local con parámetro privado y actualizar todas las tablas; comprobar cantidades, fechas y DAX en la instancia local de Analysis Services.
3. Ejecutar `dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -c Release -- reporting-inspect` en entorno de desarrollo correctamente autenticado; reconciliar categorías, sedes, cuentas, importes y recuentos Posted/PEN.
4. Capturar las cuatro páginas en Desktop; revisar títulos, visuales sin datos, recorte de textos, scroll, filtros desplegables, orden cronológico, tablas y ausencia de solapamientos.
5. Aceptación funcional con **fixture sintético aislado** y reversible: gastos Posted/PEN en más de un año, al menos dos sedes, compartidos y distintos tipos/categorías. No contaminar datos operativos; no usar identidades reales de aprobadores para falsear pruebas.
6. Aceptación de acceso/compartición, funciones de drill-through, accesibilidad, RLS y publicación según licencias cuando corresponda.

## Pendiente para iteraciones posteriores

- Ajustar el diseño con capturas reales y corregir cualquier incompatibilidad PBIR/Power BI Desktop que no detecte el validador.
- Probar comparativos interanuales con dataset positivo y acotar periodos para no presentar tasas sin base.
- Añadir drill-through enriquecido, tooltips e interacción de navegación solamente tras aceptación funcional.
- El presupuesto frente a ejecución solo puede habilitarse tras aprobar de forma independiente y transaccional el flujo protegido de presupuestos.

**Reglas de seguridad:** no publicar ni incorporar endpoint/credenciales/token/caché/PBIX/ABF a Git; no ejecutar `fmg_ApproveBudget` ni editar registros financieros para crear demos. Mantener `main` sin cambios hasta revisión de `develop`.
