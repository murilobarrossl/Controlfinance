import { useMemo, useState } from "react";
import { AreaChart, Area, CartesianGrid, XAxis, YAxis, Tooltip, ReferenceLine, ResponsiveContainer } from "recharts";
import { TOOLTIP_STYLE } from "../../../components/charts/chartTheme.js";
import { chartCoordinates, formatMoney } from "../../../utils/importedInvestments.js";
import useIsNarrow from "./useIsNarrow.js";

const AREA_COLOR = "#4ECDC4";
const tickDate = (time) => new Date(time).toLocaleDateString("pt-BR", { timeZone: "UTC", day: "2-digit", month: "short" });

function ObservationTooltip({ active, payload }) {
  const row = payload?.[0]?.payload;
  if (!active || !row) return null;
  return <div className="inv-area__tooltip" style={TOOLTIP_STYLE}>
    <span>{row.period}</span>
    <strong>{formatMoney(row.money)}</strong>
    <span>{row.collected ? "Saldo registrado pelo Control Finance" : "Posição informada pela instituição"}</span>
  </div>;
}
export default function ObservedInvestmentChart({ points, idPrefix }) {
  const [selected, setSelected] = useState(null);
  const [chartRef, narrow] = useIsNarrow();
  const data = useMemo(() => chartCoordinates(points), [points]);
  const index = Math.min(selected ?? data.length - 1, Math.max(0, data.length - 1));
  const row = data[index];
  const gradientId = `inv-area-${String(idPrefix).replace(/[^a-zA-Z0-9_-]/g, "")}`;
  if (data.filter((p) => p.money).length < 2) return <p className="investimentos__hint">{data.length === 1
    ? "Há uma posição registrada. A evolução será exibida quando houver outras referências comparáveis."
    : "Não há posições com datas de referência e valores comparáveis para desenhar a evolução."}</p>;
  return <div className="inv-area" ref={chartRef}>
    <div className="inv-area__value"><strong>{formatMoney(row?.money)}</strong><span>{row?.collected ? "saldo registrado" : "posição observada"} em {row?.period}</span></div>
    <ResponsiveContainer width="100%" height={narrow ? 200 : 240}>
      <AreaChart data={data} margin={{ top: 8, right: 16, left: 4, bottom: 8 }} onMouseMove={(event) => {
        if (event?.activeTooltipIndex == null) return;
        const next = Number(event.activeTooltipIndex);
        if (Number.isInteger(next) && next >= 0 && next < data.length) setSelected(next);
      }}>
        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="5%" stopColor={AREA_COLOR} stopOpacity={0.35} />
            <stop offset="95%" stopColor={AREA_COLOR} stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid stroke="#2a2a2a" strokeDasharray="3 3" vertical={false} />
        <XAxis dataKey="time" type="number" scale="time" domain={["dataMin", "dataMax"]} stroke="var(--color-text-tertiary)"
          tick={{ fontSize: 12 }} tickLine={false} minTickGap={narrow ? 40 : 60} tickFormatter={tickDate} padding={{ right: 12 }} />
        <YAxis hide domain={[0, 1000000]} />
        <Tooltip content={<ObservationTooltip />} cursor={{ stroke: "#3a3a3a", strokeWidth: 1 }} />
        <ReferenceLine x={row?.time} stroke="var(--color-text-tertiary)" strokeDasharray="3 4" />
        <Area type="monotone" dataKey="coordinate" stroke={AREA_COLOR} strokeWidth={2} fill={`url(#${gradientId})`} connectNulls={false}
          dot={false} activeDot={{ r: 4 }} isAnimationActive={false} />
      </AreaChart>
    </ResponsiveContainer>
    <label className="inv-yield__scrubber" htmlFor={`${idPrefix}-observation`}>
      <span>Explorar posição <strong>{row?.period}</strong></span>
      <input id={`${idPrefix}-observation`} type="range" min={0} max={data.length - 1} value={index}
        aria-valuetext={`${row?.period}: ${formatMoney(row?.money)}`} onChange={(event) => setSelected(Number(event.target.value))} />
    </label>
    <p className="inv-yield__caption">Cada ponto corresponde a uma referência recebida. Intervalos sem observações não são preenchidos. A variação do saldo pode incluir aportes, resgates, impostos e alterações de preço.</p>
  </div>;
}
