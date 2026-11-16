import { useState } from "react";
import { financialTone } from "../../../utils/financialTone.js";
import { LineChart, Line, XAxis, YAxis, Tooltip, ReferenceLine, ResponsiveContainer } from "recharts";
import { formatCurrency } from "../../../utils/financeMath.js";
import ObservedInvestmentChart from "./ObservedInvestmentChart.jsx";

function YieldTooltip({ active, payload }) {
  if (!active || !payload?.length) return null;
  return (
    <div className="inv-yield__tooltip">
      <strong>{payload[0].payload.period}</strong>
      <span>Projeção</span>
      {payload.map((entry) => (
        <div key={entry.dataKey}>
          <span>{entry.name}</span>
          <strong>{formatCurrency(entry.value)}</strong>
        </div>
      ))}
    </div>
  );
}

// Apenas projeções: o saldo atual não é usado para reconstruir rendimentos passados.
export default function InvestmentYieldChart(props) {
  if (props.kind === "historical") return <ObservedInvestmentChart points={props.points} idPrefix={props.idPrefix} />;
  return <ProjectedYieldChart {...props} />;
}
function ProjectedYieldChart({ data, series, view, onViewChange, idPrefix, narrow }) {
  const [selectedIndex, setSelectedIndex] = useState(0);
  const index = Math.min(selectedIndex, data.length - 1);
  const selected = data[index];
  const primary = series[series.length - 1];
  const isYield = view === "yield";

  return (
    <div className="inv-yield">
      <div className="inv-yield__head">
        <h5 className="inv-detail__title">{isYield ? "Rendimento mês a mês" : "Evolução do saldo"}</h5>
        <div className="investimentos__segmented" role="group" aria-label="Visualização do gráfico">
          {[["yield", "Rendimento"], ["balance", "Saldo"]].map(([key, label]) => (
            <button key={key} type="button" aria-pressed={view === key}
              className={`investimentos__segment${view === key ? " investimentos__segment--active" : ""}`}
              onClick={() => onViewChange(key)}>{label}</button>
          ))}
        </div>
      </div>
      <div className="inv-yield__value">
        <strong className={isYield ? `inv-financial--${financialTone(selected[primary.key])}` : undefined}>{formatCurrency(selected[primary.key])}</strong>
        <span>{isYield ? "Rendimento bruto estimado" : "Saldo estimado"} · {selected.period}</span>
      </div>
      <div className="inv-yield__legend">
        {series.map((entry, seriesIndex) => (
          <span key={entry.key}><i style={{ borderColor: entry.color, borderTopStyle: seriesIndex === 0 ? "dashed" : "dotted" }} aria-hidden="true" />{entry.label}</span>
        ))}
        <span className="inv-yield__projection-label">Linhas tracejadas: projeções</span>
      </div>
      <ResponsiveContainer width="100%" height={narrow ? 210 : 250}>
        <LineChart data={data} margin={{ top: 20, right: 18, left: 18, bottom: 8 }}
          onMouseMove={(state) => {
            const next = state?.activeTooltipIndex;
            if (next != null && Number.isInteger(Number(next)) && Number(next) >= 0 && Number(next) < data.length) {
              setSelectedIndex(Number(next));
            }
          }}>
          <XAxis dataKey="month" axisLine={false} tickLine={false} minTickGap={narrow ? 45 : 65}
            tick={{ fill: "var(--color-text-tertiary)", fontSize: 11 }} tickMargin={16} />
          <YAxis hide domain={["auto", "auto"]} />
          <Tooltip content={<YieldTooltip />} cursor={{ stroke: "var(--color-border-strong)", strokeDasharray: "3 4" }} />
          <ReferenceLine x={selected.month} stroke="var(--color-text-tertiary)" strokeDasharray="3 5" />
          {series.map((entry, seriesIndex) => (
            <Line key={entry.key} type="monotone" dataKey={entry.key} name={entry.label}
              stroke={entry.color} strokeWidth={2.5} strokeDasharray={seriesIndex === 0 ? "5 5" : "2 4"}
              dot={(point) => point.index === index
                ? <circle key={point.index} cx={point.cx} cy={point.cy} r={4} fill={entry.color} stroke="var(--color-surface)" strokeWidth={2} />
                : <g key={point.index} />}
              activeDot={{ r: 5, stroke: "var(--color-surface-alt)", strokeWidth: 3 }}
              isAnimationActive={false} />
          ))}
        </LineChart>
      </ResponsiveContainer>
      <label className="inv-yield__scrubber" htmlFor={`${idPrefix}-month`}>
        <span>Explorar mês <strong>{selected.period}</strong></span>
        <input id={`${idPrefix}-month`} type="range" min={0} max={data.length - 1} value={index}
          aria-valuetext={`${selected.period}: ${formatCurrency(selected[primary.key])}, estimado`}
          onChange={(event) => setSelectedIndex(Number(event.target.value))} />
      </label>
      <p className="inv-yield__caption">
        {isYield
          ? "Rendimento de cada mês, antes de impostos. Aportes não são contabilizados como rendimento."
          : "Saldo com o rendimento futuro e o IR estimado, quando calculável. O saldo inicial é o informado pelo banco."}
        {" "}Não há dados históricos neste gráfico.
      </p>
    </div>
  );
}
