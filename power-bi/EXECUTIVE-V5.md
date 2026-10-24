# Financial Manager · Executive v5

## Base de continuación

- Rama: `develop`. Proyecto fuente: `power-bi/FinancialManager/FinancialManager.pbip`.
- Modelo: cinco tablas, cuatro relaciones, 26 medidas DAX. Conservar los filtros reales **Posted + PEN**, `DataverseHost = SET_DATAVERSE_HOST` en Git y las protecciones presupuestarias del servidor.
- Estilo inspirado en las referencias académicas/operativas compartidas el 24 de octubre: paneles analíticos densos, una columna contextual útil en ciertas vistas, resumen horizontal integrado, matriz y comparaciones con personalidad. Evitar adornos típicos de dashboard generado: bordes cromáticos en cada KPI, iconos de relleno, navegación fingida, encabezados gigantes y exceso de tarjetas uniformes.
- **Reproducción:** `python scripts/build_power_bi_v5.py`; hace copia previa de la definición PBIR en `%TEMP%\FM-PBIR-before-v5-*`, nunca cambia Dataverse.

## Sistema visual

Paleta profesional pastel y moderada: azul grisáceo `#557E9A`, verde salvia `#82A795`, coral `#C79891`, lavanda `#A59AC3`, arena `#C2AB86`. Base cálida neutra `#F6F6F3`, texto `#303B45`. Los colores distinguen segmentos y acentos por página, sin reemplazar texto o gráficos como portadores de significado.

Lienzo: 1280 × 1160 para recorrido vertical. Todos los filtros permanecen como **slicers nativos y operables**. Las pestañas inferiores son las páginas nativas de Power BI; no se inventan botones estáticos para imitar controles de navegación.

## Composiciones y funciones

| Pantalla | Composición diferenciada | Análisis funcional |
| --- | --- | --- |
| Panorama de gastos | Banda horizontal de 5 indicadores integrados; columna izquierda contextual con 5 filtros; zona central asimétrica | Gráfico combinado mensual, ranking de categorías, dona por ámbito, mezcla temporal de clase de flujo, cuentas de origen, tabla de movimientos y comparación anual |
| Sedes y movimientos | Panel lateral de filtros y semántica financiera, resumen integrado a su derecha; comparativos amplios y tabla principal inferior | Sedes excluyendo compartidos sin asignación, cuentas internas, gasto por ámbito, sede por mes, categorías y detalle transaccional |
| Categorías | Banda de KPI compacta, filtros horizontales secundarios, superficie dominante de ranking y treemap, árbol de descomposición | Comparación y concentración de gasto por categoría; explicación por categoría/sede/clase/ámbito; matriz de medidas |
| Tendencias y estacionalidad | Gráfico principal de gran formato, comparativo anual al lado; indicadores integrados debajo; análisis de evolución y matriz temporal | Serie mensual, columnas anuales, acumulado, composición al 100 %, dona por ámbito y matriz año × mes. Matriz temporal **no equivale todavía a un heatmap con formato condicional** |

**Exclusiones justificadas:** sin mapa geográfico (el modelo actual no aporta coordenadas precisas válidas de sedes), sin Gantt ficticio (el hecho actual registra fechas de pagos, no actividades con inicio/fin), sin ingresos, saldo, ganancias ni presupuesto aprobado sin autorización. El árbol analítico sí se incorporó donde las dimensiones existentes lo permiten.

## Datos sintéticos y seguridad

Para visualización y pruebas, ejecutar `python scripts/create_power_bi_demo.py`. El comando crea un **PBIP aislado** en `%TEMP%\FM-EXCLUSIVAMENTE-DEMO-SINTETICA-*`, con 310 movimientos inventados de 2024–2026, en las dimensiones que ya existen. No contiene datos operativos ni debe publicarse. Los importes ficticios son solo para probar agregaciones y aspecto visual.

No insertar muestras financieras en `financial-manager-dev`, no alterar `main`, no habilitar aprobaciones, no eliminar `BGV009`, no guardar tokens ni datos de Power BI Desktop en Git.

## Puertas de calidad

1. `powerbi-report-author validate power-bi/FinancialManager/FinancialManager.pbip`: 0 errores y 0 advertencias.
2. `python -m unittest discover -s tests -q`; `python scripts/check_repository.py`.
3. Actualización del PBIP **de demostración**, DAX que confirme conteos e importes sin acceso a Dataverse.
4. Revisión visual de las cuatro páginas, controles, leyendas, tamaños de matriz y ausencia de contenido recortado; repetir con una pantalla de portátil además de monitor amplio si procede.
5. Pruebas funcionales de filtros cruzados, interacciones, criterios de año y seguridad; solo después decidir publicación.

**Estado de aceptación:** el diseño puede validar estructuralmente sin que cada gráfico y cada interacción estén aprobados. Conservar explícita esa distinción en el próximo handoff. No atribuir a v5 funciones aún no comprobadas como navegación personalizada, heatmap de celdas, RLS o publicación.
