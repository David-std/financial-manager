# Power BI Executive v3 · Financial Manager

## Estado (24 de octubre de 2026)

La definición PBIP v3 conserva las **cuatro páginas y el modelo semántico con cinco tablas, cuatro relaciones y veintiséis medidas**. Su nueva disposición de 1280 × 944 px ofrece más espacio vertical para los análisis y permite desplazamiento dentro del informe en vez de comprimir todos los gráficos.

Las cuatro páginas tienen distribuciones diferentes:

| Página | Diseño y funciones |
| --- | --- |
| Resumen de gastos | Cabecera ejecutiva, 5 filtros desplegables, 5 KPI con acentos individuales, área mensual, barras por categoría, dona compartido/sede, barras por clasificación y tabla de detalle |
| Sedes y movimientos | Rail lateral de navegación **visual** (no botones de acción), filtros en el contenido, 4 KPI, comparativos por sede y cuenta, árbol nativo de descomposición y tabla de movimientos |
| Análisis de categorías | Rail lateral, 4 KPI, treemap, ranking de barras y matriz comparativa actual/previo/variación/participación |
| Tendencias de pagos | Área mensual principal, KPI apilados, columnas por año, dona de composición y columnas apiladas al 100 % para ámbito |

Hay **153 elementos PBIR**, de los cuales 103 son elementos decorativos o informativos como títulos, bandas y acentos; los restantes incluyen 16 KPI, 18 filtros y 16 gráficos/tablas de análisis. El recuento de elementos no debe confundirse con cantidad de gráficos: hay 16 elementos de análisis excluyendo tarjetas y filtros, distribuidos en las cuatro páginas.

Nota: la navegación lateral es decorativa y solo indica la ubicación del análisis; la navegación funcional continúa mediante pestañas nativas de Power BI. No se simulan interacciones inexistentes. Los objetos de gráfico y los filtros son nativos y se espera que usen interacciones cruzadas estándar, cuya aceptación interactiva sigue pendiente.

### Fuente y seguridad

- Proyecto original: `power-bi/FinancialManager/FinancialManager.pbip`. Conserva `DataverseHost = SET_DATAVERSE_HOST` en Git, las consultas Dataverse con elegibilidad **Posted + PEN**, las cuatro relaciones originales y las medidas de gastos. **No** se agregaron pagos ni cuentas falsas en el entorno operativo.
- Regeneración reproducible: `python scripts/build_power_bi_v3.py`. Respaldará la definición anterior en una carpeta temporal fuera de Git. Asegúrate de no ejecutarlo sobre un informe que contenga cambios visuales manuales sin integrar en el generador.
- Datos para diseño: `python scripts/create_power_bi_demo.py`. Crea una copia PBIP **local y desconectada de Dataverse** bajo `%TEMP%/FM-EXCLUSIVAMENTE-DEMO-SINTETICA-*/FinancialManager`; nunca altera el PBIP del repositorio. Contiene 310 registros deterministas inventados, 2 sedes, 3 cuentas, 9 categorías y tres años (2024-2026) con importe ficticio total S/ 509.565,12. Un archivo `SOLO_DEMO_NO_PUBLICAR.txt` dentro de la carpeta advierte que nunca debe publicarse como información real.
- Esta copia demo reemplaza las particiones M de las dimensiones y el hecho por `Table.FromRecords` **solo dentro de esa copia temporal**. `DimDate` continúa siendo un calendario calculado local, para probar las mismas relaciones.
- No se guardan en el repositorio datos, credenciales, extractos de Dataverse, tokens, archivos PBIX/ABF ni URL del tenant.

### Validación alcanzada

- `powerbi-report-author validate`: definición v3 estructuralmente correcta, **0 errores / 0 advertencias**.
- La copia demo se abrió en Power BI Desktop; su motor Analysis Services contó **310 gastos**, con `Paid Spending = 509565.12` y **255 fechas distintas**.
- Las medidas por categoría devolvieron importes no nulos, lo que comprueba que la relación y la agregación funcionan con los registros ficticios.
- **La aceptación visual final todavía requiere capturar/revisar las cuatro páginas completas de Desktop y verificar filtros, matrices, árbol de descomposición, barras, dona, orden temporal, tooltips, recorte, scroll, accesibilidad y drill-through.** El validador PBIR y un DAX sin errores no demuestran por sí solos que cada visual se haya renderizado correctamente.

### Pendientes reales antes de darlo por finalizado

1. Validación visual completa con la demo abierta y refrescada. Corregir cualquier incompatibilidad visible de los nuevos tipos de gráfico o elementos que se solapen.
2. Confirmar interacciones, criterios de comparación de períodos parciales, orden cronológico, totales por categoría y sede y tratamiento del ámbito compartido.
3. Agregar navegación funcional lateral y estados vacíos contextuales más expresivos si se desea, sin convertir textos o formas decorativas en botones inertes.
4. Ejecutar permisos y aceptación final de Power Apps, presupuesto con identidad independiente, RLS de Power BI y publicación en el workspace autorizado.

**Prohibido:** publicar el modelo demo, utilizar los importes ficticios como cifras empresariales, crear filas financieras en Dataverse para llenar gráficos, habilitar aprobaciones mediante eludir `BGV009` o modificar `main` sin revisión.
