# Financial Manager — Executive v8: Desktop grid and phone layout

Fecha: 2026-10-25. Continuación de Executive v7 sobre `develop`.

## Alcance y seguridad

El generador `python scripts/build_power_bi_v8.py` recompone las cuatro páginas PBIR editables y guarda un `mobile.json` por visual colocado en los cuatro diseños telefónicos. La interfaz móvil es independiente de escritorio; las vistas móviles **no son copias a escala** de la disposición desktop. No escribe en Dataverse, no modifica la aprobación presupuestaria, no mezcla datos reales con la demostración y no publica informes. El único PBIP versionado sigue teniendo su parámetro de entorno para Dataverse.

## Cambios en escritorio

- **Resumen de gastos (01):** se conserva el diseño v7 aprobado; solo añade diseño móvil.
- **Sedes y movimientos (02):** en la barra izquierda, `CONTROL DE MOVIMIENTOS` termina antes de `Pagos en la selección`; la tabla de ámbito y el indicador final respetan espacios separados. El comparativo por mes y las categorías secundarias comparten exactamente el mismo inicio y final. El título de registro tiene espacio libre hasta la tabla detallada.
- **Análisis de categorías (03):** los tres paneles de `Evolución y composición / Gasto por tipo / Ticket promedio` coinciden en borde superior e inferior. La tabla estrecha de tres movimientos en un contenedor alto fue sustituida por **tres columnas nativas de importe por tipo de gasto**, manteniendo su origen y medida DAX. La matriz inferior y el ranking de pagos quedan a la misma altura, sin hueco de 20 px introducido por el constructor de visuales.
- **Tendencias (04):** series cronológicas con menos etiquetas amontonadas mediante `preferredCategoryWidth`. Los tres paneles de la franja mensual comparten el mismo tamaño vertical. El recuadro de ámbito, antes una tabla pequeña flotando en un contenedor alto, es ahora un gráfico de importe por ámbito con etiquetas. Abajo, la matriz ocupa la izquierda; a su derecha, el importe por clase y el **número de pagos por clase** son dos paneles no redundantes y apilados con 12 px de separación, terminando junto a la matriz.

## Vistas telefónicas

Usan archivos nativos PBIR `definition/pages/<id>/visuals/<id>/mobile.json` con posiciones independientes, ancho estándar **323 pt**, grilla de dos KPIs y filtros por fila, separación de 7–10 pt y grandes paneles de visualización de ancho completo. Lectura: título → filtros operativos → KPI → gráficos → tablas. Se excluyen gráficos decorativos, fondos de la página y las notas inferiores menos relevantes. Están incluidos todos los gráficos y matrices principales de cada página.

| Página | Visuales en phone layout | Intención |
|---|---:|---|
| 01 Resumen | 19 | KPI, filtros, evolución, categorías y movimientos |
| 02 Sedes | 16 | Reparto, cuentas, estacionalidad, detalle de operaciones |
| 03 Categorías | 17 | Ranking, composición, ticket, matriz y frecuencia |
| 04 Tendencias | 18 | Evolución, comparaciones, mezcla, matriz y flujo |

Nota: Los diseños móviles no están pensados para visualizar la tabla de 8 columnas como varias columnas comprimidas; el visual conserva scroll horizontal del propio Power BI para explorar todos los campos. En escritorio el detalle permanece con todas sus columnas.

## QA verificable

- `powerbi-report-author validate power-bi/FinancialManager/FinancialManager.pbip` — validación PBIR (incluidos `mobile.json`).
- `python -m unittest discover -s tests -q` — test `test_power_bi_executive_v8.py` comprueba: cuatro páginas móviles, 70 posiciones, sin intersecciones en mobile, ancho de 323 pt, tabla de elementos y bordes alineados de las secciones 02–04.
- `python scripts/check_repository.py` — evita contaminación de datos sintéticos o accesos peligrosos.
- `python scripts/create_power_bi_demo.py` — solo demo ficticia en `%TEMP%`. Actualizar la copia en Desktop con los 310 pagos simulados, sin publicar.
- Antes de aceptación definitiva en Power BI Desktop, revisar cada página en vista móvil con despliegue de scroll vertical y sin recortes de filtros y tarjetas. La validación de esquema/posiciones no reemplaza una auditoría manual de todos los campos ni el test de accesibilidad por teclado y lector de pantalla.

Las páginas conservan las medidas DAX y reglas de `Posted`/PEN del modelo original. No se incorporaron presupuestos ficticiamente aprobados.
