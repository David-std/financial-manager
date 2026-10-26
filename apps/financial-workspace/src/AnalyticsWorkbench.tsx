import { useMemo, useState } from "react";
import {
  ArrowRight,
  CalendarDays,
  ChevronRight,
  CircleHelp,
  Grid3X3,
  ListFilter,
  ReceiptText,
  TrendingUp,
} from "lucide-react";
import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { ANALYTIC_MONTHS, buildAnalysis } from "./analytics-model";
import type { AnalyticPayment, Driver } from "./analytics-model";
import "./analytics-workbench.css";

const money = new Intl.NumberFormat("es-PE", {
  style: "currency",
  currency: "PEN",
  minimumFractionDigits: 2,
});
const compact = (n: number) =>
  Math.abs(n) >= 1000
    ? `${n < 0 ? "−" : ""}S/ ${(Math.abs(n) / 1000).toLocaleString("es-PE", { maximumFractionDigits: 1, minimumFractionDigits: 1 })} mil`
    : money.format(n);
const percent = (n: number) =>
  n.toLocaleString("es-PE", { maximumFractionDigits: 1, minimumFractionDigits: 1 }) + " %";
const monthsLong = [
  "enero",
  "febrero",
  "marzo",
  "abril",
  "mayo",
  "junio",
  "julio",
  "agosto",
  "septiembre",
  "octubre",
  "noviembre",
  "diciembre",
];
function MoneyTooltip({
  active,
  payload,
  label,
}: {
  active?: boolean;
  payload?: Array<{ name?: string; value?: number }>;
  label?: string;
}) {
  if (!active || !payload?.length) return null;
  return (
    <div className="analysis-tooltip">
      <strong>{label}</strong>
      {payload
        .filter((p) => p.value !== null && p.value !== undefined)
        .map((p, i) => (
          <div key={i}>
            {p.name}: {money.format(p.value ?? 0)}
          </div>
        ))}
    </div>
  );
}
function Metric({
  label,
  value,
  detail,
  emphasis,
}: {
  label: string;
  value: string;
  detail: string;
  emphasis?: boolean;
}) {
  return (
    <div className={`analysis-metric ${emphasis ? "analysis-metric-main" : ""}`}>
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </div>
  );
}
function Insight({
  index,
  title,
  description,
  action,
  onClick,
}: {
  index: string;
  title: string;
  description: string;
  action: string;
  onClick: () => void;
}) {
  return (
    <button className="analysis-insight" onClick={onClick}>
      <span>{index}</span>
      <div>
        <strong>{title}</strong>
        <p>{description}</p>
        <small>
          {action} <ArrowRight size={13} />
        </small>
      </div>
    </button>
  );
}
function Rank({
  title,
  groups,
  total,
  max = 5,
}: {
  title: string;
  groups: Array<{ name: string; amount: number; count: number }>;
  total: number;
  max?: number;
}) {
  return (
    <div className="analysis-rank">
      <h4>{title}</h4>
      {groups.length ? (
        groups.slice(0, max).map((g, i) => (
          <div className="analysis-rank-row" key={g.name}>
            <span className="analysis-rank-name">{g.name}</span>
            <strong>{compact(g.amount)}</strong>
            <div className="analysis-rank-track">
              <span style={{ width: `${total ? Math.max(0.5, (100 * g.amount) / total) : 0}%` }} />
            </div>
            <small>
              {percent(total ? (g.amount / total) * 100 : 0)} · {g.count} pagos
            </small>
            <span className="analysis-rank-index">{String(i + 1).padStart(2, "0")}</span>
          </div>
        ))
      ) : (
        <div className="analysis-empty-inline">Sin movimientos en este filtro.</div>
      )}
    </div>
  );
}
function DeltaBridge({
  drivers,
  previous,
  current,
  enabled,
}: {
  drivers: Driver[];
  previous: number;
  current: number;
  enabled: boolean;
}) {
  if (!enabled)
    return (
      <div className="analysis-no-base">
        <CircleHelp size={22} />
        <strong>Sin año de referencia</strong>
        <p>
          Las variaciones por categoría se muestran a partir de 2025; no se imputan registros de
          2023.
        </p>
      </div>
    );
  const top = drivers.slice(0, 6);
  const rest = drivers.slice(6);
  const steps = [
    ...top.map((x) => ({ name: x.name, delta: x.delta })),
    ...(rest.length
      ? [{ name: "Otras categorías", delta: rest.reduce((sum, x) => sum + x.delta, 0) }]
      : []),
  ];
  const max = Math.max(1, ...steps.map((s) => Math.abs(s.delta)));
  return (
    <div className="analysis-variation">
      <div className="analysis-variation-total">
        <div>
          <small>PERÍODO ANTERIOR</small>
          <strong>{compact(previous)}</strong>
        </div>
        <ArrowRight size={18} />
        <div>
          <small>PERÍODO ACTUAL</small>
          <strong>{compact(current)}</strong>
        </div>
      </div>
      <div className="analysis-variation-headers">
        <span>Categoría</span>
        <span>Disminución / incremento</span>
        <span>Diferencia PEN</span>
      </div>
      {steps.map((stage) => {
        const grow = stage.delta > 0;
        const mag = (Math.abs(stage.delta) / max) * 50;
        return (
          <div className="analysis-variation-row" key={stage.name}>
            <span title={stage.name}>{stage.name}</span>
            <div className="analysis-variation-track">
              <span className="analysis-variation-zero" />
              <span
                className={`analysis-bridge-bar ${grow ? "increase" : "decrease"}`}
                style={{
                  left: grow ? "50%" : `${50 - mag}%`,
                  width: `${mag}%`,
                }}
                title={`${stage.name}: ${money.format(stage.delta)}`}
              />
            </div>
            <strong className={grow ? "variation-increase" : "variation-decrease"}>
              {stage.delta > 0 ? "+" : ""}
              {compact(stage.delta)}
            </strong>
          </div>
        );
      })}
    </div>
  );
}

export default function AnalyticsWorkbench({
  payments,
  year,
  month,
  scope,
  category,
  onOpenCategory,
  onOpenTransaction,
}: {
  payments: AnalyticPayment[];
  year: number;
  month: number;
  scope: string;
  category: string;
  onOpenCategory: (name: string) => void;
  onOpenTransaction: (record: AnalyticPayment) => void;
}) {
  const [rankMode, setRankMode] = useState<"amount" | "count" | "ticket">("amount");
  const data = useMemo(
    () => buildAnalysis(payments, { year, month, scope, category }),
    [payments, year, month, scope, category],
  );
  const period = month
    ? `${monthsLong[month - 1]} de ${year}`
    : `enero–${monthsLong[data.coverageEnd - 1] ?? "diciembre"} de ${year}`;
  const baseline = month
    ? `${monthsLong[month - 1]} de ${year - 1}`
    : `enero–${monthsLong[data.coverageEnd - 1] ?? "diciembre"} de ${year - 1}`;
  const ranked = [...data.byCategory].sort((a, b) => {
    const va =
      rankMode === "amount" ? a.amount : rankMode === "count" ? a.count : a.amount / a.count;
    const vb =
      rankMode === "amount" ? b.amount : rankMode === "count" ? b.count : b.amount / b.count;
    return vb - va;
  });
  const heatMax = Math.max(1, ...data.heatmap.flatMap((y) => y.months.map((m) => m.value ?? 0)));
  const largestMonths = data.monthly.filter((m) => m.count).length;
  const mainCategory = data.byCategory[0];
  const mainAccount = data.byAccount[0];
  return (
    <section className="analysis-workspace" aria-label="Análisis financiero de pagos confirmados">
      <div className="analysis-intro">
        <div>
          <span className="eyebrow">INTELIGENCIA DEL GASTO</span>
          <h2>Comprende el gasto. Decide con contexto.</h2>
          <p>
            Comparaciones homogéneas, factores de cambio, concentración y movimientos que sustentan
            cada lectura.
          </p>
        </div>
        <div className="analysis-window">
          <CalendarDays size={18} />
          <div>
            <span>VENTANA ANALIZADA</span>
            <strong>{period}</strong>
            <small>
              {data.eligiblePrior ? `Comparación: ${baseline}` : "Sin período anterior disponible"}
            </small>
          </div>
        </div>
      </div>
      <div className="analysis-measures" aria-label="Resumen analítico">
        <Metric
          emphasis
          label="Gasto confirmado"
          value={compact(data.currentTotal)}
          detail={`${data.current.length} movimientos contabilizados`}
        />
        <Metric
          label="Cambio interanual"
          value={data.deltaPercent !== null ? percent(data.deltaPercent) : "Sin base"}
          detail={
            data.delta !== null
              ? `${data.delta >= 0 ? "+" : ""}${compact(data.delta)} frente a ${year - 1}`
              : "No hay registros del año anterior"
          }
        />
        <Metric label="Ticket promedio" value={compact(data.ticket)} detail="Por pago confirmado" />
        <Metric
          label="Peso de las 3 principales"
          value={percent(data.topThreeShare)}
          detail="Concentración en categorías"
        />
        <Metric
          label="Gasto compartido"
          value={percent(data.sharedShare)}
          detail="Sobre el gasto seleccionado"
        />
      </div>

      <div className="analysis-section-label">
        <span>01 / MOVIMIENTO</span>
        <h3>Qué cambió y dónde</h3>
      </div>
      <div className="analysis-lead-grid">
        <article className="analysis-panel analysis-bridge-panel">
          <div className="analysis-panel-title">
            <div>
              <h4>Factores de variación · categorías</h4>
              <p>
                {data.eligiblePrior
                  ? `${baseline} → ${period} · cambios en PEN`
                  : "La comparación requiere un ejercicio anterior en el conjunto"}
              </p>
            </div>
            <span className="analysis-soft-mark">
              <TrendingUp size={20} />
            </span>
          </div>
          <DeltaBridge
            drivers={data.drivers}
            previous={data.previousTotal}
            current={data.currentTotal}
            enabled={data.eligiblePrior}
          />
          <div className="analysis-bridge-legend">
            <span>
              <i className="baseline" /> Gasto total
            </span>
            <span>
              <i className="up" /> Mayor gasto
            </span>
            <span>
              <i className="down" /> Menor gasto
            </span>
          </div>
          {data.eligiblePrior && (
            <div className="analysis-reconcile">
              Control: {money.format(data.previousTotal)} + {money.format(data.delta ?? 0)} ={" "}
              <strong>{money.format(data.currentTotal)}</strong>
            </div>
          )}
        </article>
        <aside className="analysis-editorial">
          <span className="eyebrow">LECTURAS DEL PERÍODO</span>
          {mainCategory ? (
            <Insight
              index="01"
              title={mainCategory.name}
              description={`Mayor categoría: ${compact(mainCategory.amount)}, ${percent((mainCategory.amount / data.currentTotal) * 100)} del total.`}
              action="Explorar categoría"
              onClick={() => onOpenCategory(mainCategory.name)}
            />
          ) : (
            <div className="analysis-empty-inline">
              No hay registros confirmados en este filtro.
            </div>
          )}
          {data.peak && (
            <Insight
              index="02"
              title={`Pico en ${data.peak.name}`}
              description={`Máximo mensual: ${compact(data.peak.current)} en ${data.peak.count} movimientos. Un pico no implica irregularidad.`}
              action="Revisar períodos"
              onClick={() =>
                document.getElementById("analysis-time")?.scrollIntoView({ behavior: "smooth" })
              }
            />
          )}
          {mainAccount && (
            <Insight
              index="03"
              title={mainAccount.name}
              description={`Cuenta con más salidas: ${compact(mainAccount.amount)}. La cifra no representa el saldo bancario.`}
              action="Explorar origen"
              onClick={() =>
                document.getElementById("analysis-origin")?.scrollIntoView({ behavior: "smooth" })
              }
            />
          )}
        </aside>
      </div>

      <div className="analysis-section-label" id="analysis-time">
        <span>02 / TIEMPO</span>
        <h3>Ritmo y estacionalidad</h3>
      </div>
      <div className="analysis-time-grid">
        <article className="analysis-panel analysis-trend">
          <div className="analysis-panel-title">
            <div>
              <h4>Gasto mensual y período comparable</h4>
              <p>
                Mes calendario · importes PEN ·{" "}
                {data.monthly.length === 1 ? "mes elegido" : "mismos meses en ambos ejercicios"}
              </p>
            </div>
          </div>
          <div
            className="analysis-time-chart"
            role="img"
            aria-label="Gasto mensual actual y gasto del año anterior en la misma ventana"
          >
            {data.monthly.length ? (
              <ResponsiveContainer width="100%" height="100%">
                <ComposedChart
                  data={data.monthly}
                  margin={{ top: 12, right: 12, bottom: 6, left: -9 }}
                >
                  <CartesianGrid vertical={false} stroke="var(--grid)" />
                  <XAxis
                    dataKey="name"
                    tick={{ fill: "var(--muted)", fontSize: 11 }}
                    axisLine={false}
                    tickLine={false}
                  />
                  <YAxis
                    tickFormatter={(v) => `${Math.round(v / 1000)} mil`}
                    tick={{ fill: "var(--muted)", fontSize: 10 }}
                    axisLine={false}
                    tickLine={false}
                  />
                  <Tooltip content={<MoneyTooltip />} />
                  <Bar
                    dataKey="current"
                    name={`Pagado ${year}`}
                    fill="#5a8491"
                    radius={[3, 3, 0, 0]}
                    maxBarSize={27}
                    isAnimationActive={false}
                  />
                  {data.eligiblePrior && (
                    <Line
                      dataKey="prior"
                      name={`Pagado ${year - 1}`}
                      type="linear"
                      stroke="#9cae91"
                      strokeWidth={2.5}
                      dot={{ r: 2 }}
                      isAnimationActive={false}
                    />
                  )}
                </ComposedChart>
              </ResponsiveContainer>
            ) : (
              <div className="analysis-empty-inline">Sin meses con registros.</div>
            )}
          </div>
          <div className="analysis-bridge-legend">
            <span>
              <i className="baseline" /> Pagado {year}
            </span>
            {data.eligiblePrior && (
              <span>
                <i className="prior" /> Pagado {year - 1}
              </span>
            )}
            <span>Períodos equivalentes</span>
          </div>
        </article>
        <article className="analysis-panel analysis-season">
          <div className="analysis-panel-title">
            <div>
              <h4>Mapa temporal · 2024 a 2026</h4>
              <p>Consulta histórica por mes · mismo ámbito y categoría</p>
            </div>
            <Grid3X3 size={20} />
          </div>
          <div
            className="analysis-heatmap-scroller"
            tabIndex={0}
            role="region"
            aria-label="Tabla de intensidad mensual de pagos, desplazable horizontalmente"
          >
            <div className="analysis-heatmap">
              <div className="analysis-heat-label">Año</div>
              {ANALYTIC_MONTHS.map((m) => (
                <span className="analysis-heat-label" key={m}>
                  {m}
                </span>
              ))}
              {data.heatmap.map((row) => (
                <div className="analysis-heat-row" key={row.year}>
                  <strong>{row.year}</strong>
                  {row.months.map((point) => {
                    const value = point.value;
                    const opacity =
                      value !== null && value > 0 ? 0.1 + 0.76 * Math.sqrt(value / heatMax) : 0;
                    return (
                      <div
                        className={`analysis-heat-cell ${value === null ? "future" : ""}`}
                        key={point.name}
                        style={
                          value !== null
                            ? {
                                background:
                                  value > 0 ? `rgba(63,121,133,${opacity})` : "var(--surface-soft)",
                              }
                            : {}
                        }
                        title={
                          value === null
                            ? `${row.year} ${point.name}: fuera del período de prueba`
                            : `${row.year} ${point.name}: ${money.format(value)}, ${point.count} pagos`
                        }
                        aria-label={
                          value === null
                            ? `${row.year} ${point.name}: aún no disponible`
                            : `${row.year} ${point.name}: ${money.format(value)}`
                        }
                      >
                        {value === null ? "—" : ""}
                      </div>
                    );
                  })}
                </div>
              ))}
            </div>
          </div>
          <div className="analysis-heat-scale">
            <span>Menor importe</span>
            <span className="analysis-heat-gradient" />
            <span>Mayor importe</span>
          </div>
          <p className="analysis-note">
            Una celda tenue puede significar cero pagos registrados; «—» significa período futuro.
            No se estiman meses faltantes.
          </p>
          <div className="analysis-season-foot">
            <strong>{largestMonths}</strong>
            <span>meses con pagos en la selección actual</span>
          </div>
        </article>
      </div>

      <div className="analysis-section-label" id="analysis-origin">
        <span>03 / CONCENTRACIÓN</span>
        <h3>Origen y composición</h3>
      </div>
      <div className="analysis-origin-grid">
        <article className="analysis-panel analysis-origin-main">
          <div className="analysis-panel-title">
            <div>
              <h4>Contribución por sede y cuenta</h4>
              <p>Importe de pagos, no saldo disponible</p>
            </div>
          </div>
          <div className="analysis-rank-grid">
            <Rank title="Sedes y compartidos" groups={data.byBranch} total={data.currentTotal} />
            <Rank title="Cuentas de origen" groups={data.byAccount} total={data.currentTotal} />
          </div>
        </article>
        <article className="analysis-panel analysis-flow">
          <div className="analysis-panel-title">
            <div>
              <h4>Clasificación del gasto</h4>
              <p>Operativo, capital e impuestos</p>
            </div>
          </div>
          <div className="analysis-flow-track">
            {data.byFlow.map((x, i) => (
              <span
                key={x.name}
                style={{
                  width: `${data.currentTotal ? (x.amount / data.currentTotal) * 100 : 0}%`,
                  background: ["#5a8491", "#8daea2", "#c4ab8a"][i % 3],
                }}
              />
            ))}
          </div>
          {data.byFlow.length ? (
            data.byFlow.map((x, i) => (
              <div className="analysis-flow-row" key={x.name}>
                <span style={{ background: ["#5a8491", "#8daea2", "#c4ab8a"][i % 3] }} />
                <strong>{x.name}</strong>
                <small>
                  {percent(data.currentTotal ? (x.amount / data.currentTotal) * 100 : 0)}
                </small>
                <b>{compact(x.amount)}</b>
              </div>
            ))
          ) : (
            <div className="analysis-empty-inline">No hay movimientos para clasificar.</div>
          )}
          <p className="analysis-note">
            Esta distribución describe pagos confirmados, no ingresos ni rentabilidad.
          </p>
        </article>
      </div>

      <div className="analysis-section-label">
        <span>04 / DETALLE ACCIONABLE</span>
        <h3>De la cifra al movimiento</h3>
      </div>
      <div className="analysis-action-grid">
        <article className="analysis-panel analysis-category-detail">
          <div className="analysis-panel-title">
            <div>
              <h4>Ranking de categorías</h4>
              <p>Elige importe, frecuencia o ticket medio para ordenar la tabla</p>
            </div>
            <div className="analysis-rank-switch" role="group" aria-label="Ordenar categorías">
              {(["amount", "count", "ticket"] as const).map((x) => (
                <button key={x} aria-pressed={rankMode === x} onClick={() => setRankMode(x)}>
                  {x === "amount" ? "Importe" : x === "count" ? "Pagos" : "Ticket"}
                </button>
              ))}
            </div>
          </div>
          <div
            className="analysis-table-scroll"
            role="region"
            aria-label="Ranking de categorías, desplazable horizontalmente"
            tabIndex={0}
          >
            <table className="analysis-table">
              <thead>
                <tr>
                  <th>Categoría</th>
                  <th>Pagos</th>
                  <th>Total PEN</th>
                  <th>Ticket PEN</th>
                  <th>Participación</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {ranked.map((x) => (
                  <tr key={x.name}>
                    <td>{x.name}</td>
                    <td>{x.count}</td>
                    <td>{money.format(x.amount)}</td>
                    <td>{money.format(x.amount / x.count)}</td>
                    <td>{percent(data.currentTotal ? (x.amount / data.currentTotal) * 100 : 0)}</td>
                    <td>
                      <button
                        onClick={() => onOpenCategory(x.name)}
                        aria-label={"Ver movimientos de " + x.name}
                      >
                        Ver <ChevronRight size={14} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {!ranked.length && (
            <div className="analysis-empty-inline">
              No hay categorías para esta combinación de filtros.
            </div>
          )}
        </article>
        <aside className="analysis-panel analysis-important">
          <div className="analysis-panel-title">
            <div>
              <h4>Pagos de mayor importe</h4>
              <p>Primeros cinco de la selección</p>
            </div>
            <ListFilter size={17} />
          </div>
          {data.largest.slice(0, 5).map((r, i) => (
            <button className="analysis-payment" key={r.id} onClick={() => onOpenTransaction(r)}>
              <span className="analysis-payment-index">{String(i + 1).padStart(2, "0")}</span>
              <span>
                <strong>{r.category}</strong>
                <small>
                  {r.id} · {r.date}
                </small>
              </span>
              <b>{compact(r.amount)}</b>
              <ChevronRight size={15} />
            </button>
          ))}
          {!data.largest.length && (
            <div className="analysis-empty-inline">Sin movimientos en el período.</div>
          )}
          <div className="analysis-bottom-caption">
            <ReceiptText size={15} /> Seleccionar un pago abre su detalle; no permite editar uno
            contabilizado.
          </div>
        </aside>
      </div>
      <div className="analysis-trust">
        <CircleHelp size={18} />
        <p>
          <strong>Alcance analítico.</strong> Solo pagos contabilizados en PEN. Las variaciones
          comparan los mismos meses de calendario y respetan los filtros de categoría y ámbito. Los
          gastos compartidos se cuentan una vez. No se calculan presupuestos aprobados, cobros,
          ganancias, saldos bancarios ni alertas de fraude.
        </p>
      </div>
    </section>
  );
}
