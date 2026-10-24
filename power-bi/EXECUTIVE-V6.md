# Financial Manager · Executive v6

**Punto de partida:** `develop@2310975`. Implementación de acabado visual y legibilidad sobre el PBIP existente, sin alterar la fuente Posted/PEN ni el flujo de aprobaciones.

## Alcance aplicado

La iteración aborda problemas concretos de v5: tablas con poca altura útil, matriz mensual prácticamente de una fila, encabezados técnicos sin traducción, exceso de superficie libre y ausencia de indicadores de estado sustentados en datos.

| Página | Cambios aplicados |
| --- | --- |
| **Panorama de gastos** | Resumen ejecutivo integrado, volumen mensual y categórico; panel lateral con estado dependiente de filtros y medidor auténtico de proporción compartida; detalle transaccional ampliado a 390 px; comparativo anual de importe y volumen en gráficos separados |
| **Sedes y movimientos** | Comparativos más amplios; panel lateral con disponibilidad y medidor compartido; registro operativo ampliado a 629 px para mostrar varias filas, con estado por movimiento y encabezados amigables |
| **Análisis de categorías** | Ranking y treemap amplios, árbol de descomposición de 424 × 288 px y matriz comparativa de 1240 × 482 px, con jerarquía y encabezados formateados |
| **Tendencias** | Serie y comparativo anual de gran formato; banda de indicadores integrada; gráficos mixtos; matriz de importe por **AñoMes × Categoría** con 433 px de altura; medidor compartido en panel complementario |

Lienzo **1280 × 1630** para facilitar lectura vertical. La altura de las tablas operativas permite más filas; no equivale automáticamente a verificar qué registros se ven a cada resolución.

### Reglas de formato implementadas

- Encabezados y alias de consultas en español para tablas: *Referencia, Fecha de pago, Sede, Cuenta origen, Categoría, Tipo de gasto, Ámbito, Estado, Monto PEN*.
- Filas alternadas y encabezados con fondo azul grisáceo suave, tamaño de texto explícito, interlineado de 7 px, ajuste de encabezado y líneas horizontales discretas.
- Las tarjetas de importe y pago mantienen sus medidas anteriores; no se agregan bordes multicolor a cada tarjeta ni medidores de avance ficticios.
- Las indicaciones `✓ Contabilizado` se calculan exclusivamente sobre registros del hecho `FactPostedExpense`, que **filtra transacciones Posted en PEN desde el origen**.
- El indicador `Estado de registros` indica *Sin movimientos* cuando la selección no contiene pagos y *✓ Contabilizados* cuando los hay.
- El gauge de `Participación compartida` usa escala **0–100 %**, con medida independiente de máximo igual a 1. **No es un KPI de cumplimiento ni una meta de presupuesto**.
- La matriz AñoMes × Categoría es una cuadrícula analítica con formato de filas alternadas, **no** un heatmap condicional ni un cronograma Gantt. No se inventan actividades, geolocalizaciones, ingresos o presupuestos aprobados.

### Medidas añadidas al modelo

- `Escala porcentual = 1` para el máximo del gauge.
- `Estado de registros` basado en `Payment Count`.
- `Estado por movimiento` con `HASONEVALUE(FactPostedExpense[ExpenseId])` para cada fila.

El modelo conserva las 5 tablas, 4 relaciones y 26 medidas originales; ahora suma **29 medidas**. Las tres añadidas son de presentación: no modifican importes, filtros de aprobación ni reglas de seguridad.

### Reproducción y seguridad

- Fuente: `power-bi/FinancialManager/FinancialManager.pbip`
- Generación: `python scripts/build_power_bi_v6.py`
- Respaldo automático de la definición previa: `%TEMP%\FM-before-v6-*`
- Demo desconectada de Dataverse: `python scripts/create_power_bi_demo.py`. Crea exclusivamente un PBIP temporal con **310 pagos ficticios** repartidos entre 2024 y 2026, 2 sedes, 3 cuentas y 9 categorías. No se comprometen estos datos a Git.
- Validación: `powerbi-report-author validate power-bi/FinancialManager/FinancialManager.pbip`, `python -m unittest discover -s tests -q`, `python scripts/check_repository.py`.

### Evidencia y límites de aceptación

La definición PBIR se verificó con **0 errores y 0 advertencias**, y las pruebas estáticas validan altura mínima de tablas, alias de columnas, semántica de estados, roles del gauge, dimensiones de la matriz y límites del lienzo. El Desktop local reconoce las medidas nuevas; en la comprobación de la instancia abierta, `FactPostedExpense` permaneció sin registros cargados.

**Pendiente, no afirmar como terminado:** actualización positiva y renderizado final de las 4 páginas con la copia sintética, inspección de leyendas, overflow real, cambios de filtros, comportamiento de iconos por fila, rendimiento de la matriz y accesibilidad. La aplicación de formatos en JSON y la validación de la estructura no sustituyen a estas pruebas. Tampoco se ha publicado el informe ni se han realizado pruebas RLS de usuarios independientes.

No publicar la demo ni usar cifras sintéticas como hechos operativos. No modificar `main`, no escribir movimientos financieros en Dataverse y no levantar el bloqueo de aprobación presupuestaria (`BGV009`).
