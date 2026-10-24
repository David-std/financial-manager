# Financial Manager — Reporting v4: diseño sobrio y centrado en datos

**Estado:** versión implementada en PBIP, con pruebas estructurales y evaluación visual en Desktop sobre una copia sintética. No supone publicación del informe.

## Decisión editorial

La v3 tenía un problema de composición, no solo de color: acumulaba bandas, acentos, navegación falsa y KPI idénticos, a costa del espacio disponible para análisis. La v4 sustituye ese enfoque por un tablero corporativo sobrio similar a las referencias aportadas:

- Base neutral blanca y gris claro, tipografía Segoe UI y acentos limitados al contenido de los gráficos; no hay barras multicolor de KPI, grandes cabeceras oscuras ni navegación simulada.
- Cabecera breve, filtros en una banda baja y **más superficie para gráficos**. Los informes tienen 1280 × 1140 px y scroll vertical normal para evitar comprimir la información en una única pantalla.
- Las cuatro páginas tienen composiciones específicas, no una cuadrícula repetida:
  - **Visión general de gastos:** 5 KPI, columnas + línea mensual, ranking de categorías, dona por ámbito, evolución apilada, cuentas de origen, registro detallado y gráfico por ejercicio.
  - **Sedes y movimientos:** 4 KPI, comparativo de sedes (sin repartir compartidos), cuentas de origen, dona por ámbito, sede por mes, categorías y tabla de operaciones de ancho completo.
  - **Análisis de categorías:** 4 KPI, ranking horizontal amplio, treemap, tendencia apilada, composición de flujo, gasto anual y matriz comparativa.
  - **Tendencias y estacionalidad:** 4 KPI, área mensual principal, comparativo anual, gráfico combinado monto/cantidad, distribución mensual al 100 %, dona de ámbito, matriz año × mes y ranking por flujo.
- Los gráficos, las tablas, KPI y slicers son controles nativos de Power BI. No hay botones falsos. Se mantienen las interacciones nativas sin prometer bookmarks, drill-through o RLS que no hayan sido aceptados.

## Datos y rigor

- Se conserva el modelo de cinco tablas, cuatro relaciones, 26 medidas DAX y filtros originales **Posted + PEN**. El parámetro de conexión a Dataverse continúa siendo privado de cada entorno.
- Se corrigió la comparación interanual para **exigir exactamente un año seleccionado**. Sin ello se muestra «Selecciona un año» y el porcentaje base queda en blanco para no inventar una variación.
- El año anterior se muestra con prefijo monetario literal `S/ ` y no interpreta la barra del símbolo como separador de fecha.
- El acumulado anual toma como límite la fecha máxima de movimiento del contexto actual, no el final 2035 del calendario vacío.
- En gráficos por sede se usa `Branch Spending`, excluyendo el gasto compartido que no tiene sede de asignación. El total consolidado conserva los gastos compartidos sin repartirlos artificialmente.
- Las denominaciones de categoría son dimensiones de Dataverse; no se sustituyen importes vacíos por datos falsos.

## Reproducción

- **PBIP operativo**: `power-bi/FinancialManager/FinancialManager.pbip`.
- **Generador v4**: `python scripts/build_power_bi_v4.py`. Respalda localmente la definición previa antes de escribir y reconstruye identificadores determinísticos de los visuales.
- **Copia demostrativa completamente local**: `python scripts/create_power_bi_demo.py`. Genera 310 movimientos sintéticos en un PBIP separado bajo `%TEMP%`, con un archivo de advertencia. Nunca registra datos de prueba en Dataverse.
- **Validación de esquema**: `powerbi-report-author validate power-bi/FinancialManager/FinancialManager.pbip`.
- **Pruebas**: `python -m unittest discover -s tests -q`.

## Criterios de aceptación y límites

El resultado no se considera terminado sin verificar las cuatro páginas completas en Desktop y con el conjunto sintético aislado: cantidad y composición de los gráficos, cálculos condicionados por año, ejes, etiquetas, leyendas, tablas completas, ausencia de campos técnicos y comportamiento de filtros cruzados.

**Pendiente:** confirmar todos los filtros, tablas y gráficos en Desktop tras la última recarga; comprobar comparativos con selección de año, estados vacíos, navegación entre páginas, accesibilidad, drill-through y RLS; publicación únicamente en workspace autorizado. No marcar esta etapa como aceptada para usuarios productivos hasta completar esos controles.

**Restricciones:** sin publicación demo; sin importes sintéticos al entorno `financial-manager-dev`; sin tocar `main`, pagos ni presupuestos; no habilitar aprobaciones o eludir `BGV009`.
