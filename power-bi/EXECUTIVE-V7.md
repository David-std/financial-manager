# Financial Manager — Executive v7 · Accesibilidad y comprensión

## Continuación sobre v6

La versión v7 se concentra en los problemas señalados en las capturas de las cuatro páginas: iconos pequeños y desalineados, gráficos circulares con leyendas cortadas, ejes monetarios confusos (`0.0 / 0.1 / 0.2 mill.`), barras sobredimensionadas con dos registros, componentes laterales truncados, matrices parcialmente vacías y visuales analíticos que no mostraban información al abrir.

**Fuente operativa:** `power-bi/FinancialManager/FinancialManager.pbip`. La solución Dataverse y los plugins financieros no se modificaron. Para regenerar: `python scripts/build_power_bi_v7.py`. Se crea respaldo de definiciones en `%TEMP%/FM-PBIR-pre-v7-*`. Para generar otra demo sin Dataverse: `python scripts/create_power_bi_demo.py` (se almacena solo bajo `%TEMP%`).

## Cambios por página

### 01 · Panorama de gastos

- Iconos junto al borde derecho superior del KPI, ampliados y sin afectar el valor central.
- En el panel lateral, el recuadro anteriormente truncado `Contabilizados...` se convierte en una cifra de `Pagos en la selección` vinculada a `Payment Count`. El control de estado `✓ Solo contabilizados` se conserva como aclaración del alcance Posted/PEN, no como certificación de calidad de datos.
- El donut de **ámbito** se sustituye por una tabla de solo dos categorías, con **importe PEN** y **porcentaje real sobre el ámbito seleccionado**. La información se lee sin adivinar colores ni leyendas cortadas.
- La barra horizontal estrecha de **cuentas de origen** se convierte en tabla de cuentas e importes, evitando ejes ambiguos para tres filas.
- La distribución en áreas irregulares se convierte en columnas apiladas al 100 % por **mes calendario 1–12**, agregadas sobre los años filtrados. Su título indica el alcance. Esto es mezcla estacional, no tendencia cronológica interanual.
- Los gráficos anuales de importe y de cantidad de pagos están alineados y usan títulos con unidades.
- La tabla principal pierde la palabra «recientes» porque no se ha comprobado un orden descendente explícito.

### 02 · Sedes y movimientos

- Las dos sedes y las tres cuentas ya no ocupan gráficos altos innecesariamente: altura ajustada, valores directos para sedes/cuentas y escala explícita en miles de PEN.
- La comparación mensual por sede ocupa la mayor anchura, con las doce categorías de mes calendario (agregación de años seleccionados); el ranking de categorías queda a su derecha.
- Se elimina el donut redundante de ámbito, ya representado en la banda superior y en el resumen lateral.
- El estado lateral pasa a **número de pagos en la selección**, que no se corta al reducir el ancho.
- La tabla operativa conserva detalle por sede, cuenta, categoría, ámbito, estado e importe, en su zona ancha inferior.

### 03 · Categorías de gasto

- Se mantiene ranking + treemap, que ya transmitían bien la concentración.
- Se elimina el árbol de descomposición que aparecía casi vacío hasta expandirlo. Lo reemplaza un gráfico de **ticket medio por categoría**, utilizable desde la primera vista.
- El donut de flujo se reemplaza con importe directo por clase de gasto.
- La matriz comparativa ocupa 787 px y comparte fila con un análisis de **cantidad de pagos por categoría**; se elimina la gran zona derecha desaprovechada.
- Nueva medida **Año previo comparable**: devuelve valores solo al filtrar exactamente un año, evitando el error de mostrar el mismo gasto como «actual» y «anterior» cuando se mezclan años.

### 04 · Tendencias y estacionalidad

- El área temporal principal y la comparación anual mantienen su función.
- El gráfico combinado mensual usa eje 1–12, con título que explica **barras = monto**, **línea = frecuencia**, agregando los años filtrados.
- La mezcla porcentual mensual emplea un rango legible de doce meses.
- Se reemplaza el donut pequeño por una tabla con importe y porcentaje del ámbito.
- La matriz temporal conserva sus columnas de categorías y sus filas Año/Mes; en el costado derecho ahora hay importes por clase de flujo, en vez de otro medidor redundante.

## Modelo y semántica

Se mantienen 5 tablas, 4 relaciones y las protecciones de origen `Posted + PEN`. El modelo pasa de 29 a **31 medidas DAX**:

- `Participación por ámbito`: porcentaje de cada ámbito calculado con `ALLSELECTED(FactPostedExpense[ScopeLabel])`; respeta los demás filtros aplicados.
- `Año previo comparable`: utiliza `HASONEVALUE(DimDate[Year])`; si no se selecciona exactamente un año, deja en blanco la comparación.

No se añadieron ingresos, beneficios, presupuestos aprobados, geolocalizaciones o actividades para justificar gráficos que los datos no sostienen.

## Verificación y criterios de salida

- Confirmar el esquema PBIR sin errores o advertencias.
- Ejecutar `python -m unittest discover -s tests -q` y `python scripts/check_repository.py`.
- Actualizar el PBIP **sintético temporal** en Desktop, consultar al motor: 310 registros ficticios y S/ 509.565,12 en total; comprobar al menos una agrupación y las dos medidas nuevas.
- Inspeccionar visualmente las cuatro páginas completas con varias alturas de scroll: rótulos, tablas de ámbito, etiquetas de eje, barras laterales, filtros, tooltips e iconos.
- Comprobar la interacción real de los filtros de año y ámbito y verificar que la cuota se recalcule sin duplicaciones.
- Comprobar que los gráficos que usan `Month` suman meses de los años filtrados y no se interpretan como una línea temporal continua.
- Probar accesibilidad de contraste, navegación por teclado, texto alternativo y publicación en Power BI Service **antes de aceptar para producción**. Validar PBIR no constituye esas pruebas.

**Importante:** Los 310 movimientos son exclusivamente ficticios. No se publican, no se insertan en Dataverse y no se guardan extractos en Git. Los importes reales siguen sujetos a permisos, usuarios y estado de contabilización.

**Prohibido:** modificar `main`, aprobar presupuestos, tocar transacciones reales, eludir `BGV009`, habilitar permisos adicionales o publicar la demo.
