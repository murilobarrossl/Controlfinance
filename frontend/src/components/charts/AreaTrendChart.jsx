import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { TOOLTIP_STYLE, TOOLTIP_ITEM_STYLE } from "./chartTheme.js";
import "./AreaTrendChart.css";

export default function AreaTrendChart({ data, xKey = "month", series, height = 260, formatValue, badge }) {
  return (
    <div className="area-trend-chart">
      {badge && (
        <div className="area-trend-chart__badge">
          <span className="area-trend-chart__badge-value">{badge.value}</span>
          <span className="area-trend-chart__badge-label">{badge.label}</span>
        </div>
      )}

      <ResponsiveContainer width="100%" height={height}>
        <AreaChart data={data} margin={{ top: 8, right: 16, left: 4, bottom: 8 }}>
          <defs>
            {series.map((s) => (
              <linearGradient key={s.key} id={`area-trend-${s.key}`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor={s.color} stopOpacity={0.35} />
                <stop offset="95%" stopColor={s.color} stopOpacity={0} />
              </linearGradient>
            ))}
          </defs>
          <CartesianGrid stroke="#2a2a2a" strokeDasharray="3 3" vertical={false} />
          <XAxis dataKey={xKey} stroke="#808080" tick={{ fontSize: 12 }} padding={{ right: 12 }} />
          <YAxis stroke="#808080" tick={{ fontSize: 12 }} tickFormatter={formatValue} width={76} />
          <Tooltip
            formatter={(value) => (formatValue ? formatValue(value) : value)}
            contentStyle={TOOLTIP_STYLE}
            labelStyle={{ color: "#b8b8b8" }}
            itemStyle={TOOLTIP_ITEM_STYLE}
            cursor={{ stroke: "#3a3a3a", strokeWidth: 1 }}
          />
          <Legend wrapperStyle={{ fontSize: 12, color: "#b8b8b8" }} />
          {series.map((s) => (
            <Area
              key={s.key}
              type="monotone"
              dataKey={s.key}
              stroke={s.color}
              strokeWidth={2}
              fill={`url(#area-trend-${s.key})`}
              dot={false}
              activeDot={{ r: 4 }}
            />
          ))}
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
