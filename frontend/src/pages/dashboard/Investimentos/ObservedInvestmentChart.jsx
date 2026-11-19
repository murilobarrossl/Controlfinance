import { useMemo } from "react";
import { AreaChart, Area, CartesianGrid, XAxis, YAxis, Tooltip, ResponsiveContainer } from "recharts";
import { CHART_COLORS, TOOLTIP_STYLE } from "../../../components/charts/chartTheme.js";
import { chartCoordinates, formatMoney } from "../../../utils/importedInvestments.js";
import useIsNarrow from "./useIsNarrow.js";

const tickDate = (time) => new Date(time).toLocaleDateString("pt-BR", { timeZone: "UTC", day: "2-digit", month: "short" });

function ObservationTooltip({ active, payload }) {
  const row = payload?.[0]?.payload;
  const point = row?.observation ?? row;
  if (!active || !point?.money) return null;
  return <div className="inv-area__tooltip" style={TOOLTIP_STYLE}>
    <span>{point.period}</span>
    <strong>{formatMoney(point.money)}</strong>
  </div>;
}
export default function ObservedInvestmentChart({ points, idPrefix }) {
  const [chartRef, narrow] = useIsNarrow();
  const data = useMemo(() => chartCoordinates(points), [points]);
  const observed = data.filter((point) => point.money);
  const gradientId = `inv-area-${String(idPrefix).replace(/[^a-zA-Z0-9_-]/g, "")}`;
  if (!observed.length) return <p className="record-detail__empty">Nenhuma posição com valor registrada até agora.</p>;
  const times = [...new Set(observed.map((point) => point.time))];
  const single = times.length === 1;
  // Um único saldo vira uma linha reta de borda a borda: as duas pontas são só coordenadas de desenho do
  // mesmo registro (tooltip e eixo mostram a data e o valor reais), não observações novas.
  const latest = observed[observed.length - 1];
  const plotted = single ? [{ x: 0, coordinate: latest.coordinate, observation: latest }, { x: 1, coordinate: latest.coordinate, observation: latest }]
    : data.map((point) => ({ ...point, x: point.time }));
  return <div className="inv-observed" ref={chartRef}>
    <ResponsiveContainer width="100%" height={narrow ? 170 : 200}>
      <AreaChart data={plotted} margin={{ top: 8, right: 16, left: 4, bottom: 8 }}>
        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="5%" stopColor={CHART_COLORS.neutral} stopOpacity={0.22} />
            <stop offset="95%" stopColor={CHART_COLORS.neutral} stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid stroke={CHART_COLORS.grid} vertical={false} />
        <XAxis dataKey="x" type="number" scale={single ? "linear" : "time"} domain={single ? [0, 1] : ["dataMin", "dataMax"]}
          ticks={single ? [0.5] : times.length <= 6 ? times : undefined} stroke="var(--color-text-tertiary)" axisLine={false} tickLine={false}
          tick={{ fontSize: 12, fill: "var(--color-text-tertiary)" }} minTickGap={narrow ? 40 : 60}
          tickFormatter={single ? () => tickDate(latest.time) : tickDate} padding={{ right: 12 }} />
        <YAxis hide domain={[0, 1000000]} tickCount={4} />
        <Tooltip content={<ObservationTooltip />} cursor={{ stroke: CHART_COLORS.cursor, strokeWidth: 1 }} />
        <Area type="monotone" dataKey="coordinate" stroke={CHART_COLORS.neutral} strokeWidth={2} fill={`url(#${gradientId})`} connectNulls={false}
          dot={false} activeDot={single ? false : { r: 4 }} isAnimationActive={false} />
      </AreaChart>
    </ResponsiveContainer>
    {single && <p className="inv-observed__note">Só há uma posição registrada; a variação aparece a partir da próxima.</p>}
  </div>;
}
