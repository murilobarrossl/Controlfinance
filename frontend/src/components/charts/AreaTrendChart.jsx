import { useId } from "react";
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { CHART_COLORS, TOOLTIP_STYLE, TOOLTIP_ITEM_STYLE } from "./chartTheme.js";
import "./AreaTrendChart.css";

const DEFAULT_X_PADDING = { right: 12 };

export default function AreaTrendChart({
  data,
  xKey = "month",
  series,
  height = 260,
  formatValue,
  badge,
  xAxisPadding = DEFAULT_X_PADDING,
}) {
  const gradientId = useId().replace(/[^a-zA-Z0-9_-]/g, "");
  return (
    <div className="area-trend-chart">
      {badge && (
        <div className="area-trend-chart__badge">
          <span className={`area-trend-chart__badge-value area-trend-chart__badge-value--${badge.tone ?? "default"}`}>{badge.value}</span>
          <span className="area-trend-chart__badge-label">{badge.label}</span>
        </div>
      )}

      <ResponsiveContainer width="100%" height={height}>
        <AreaChart data={data} margin={{ top: 8, right: 16, left: 4, bottom: 8 }}>
          <defs>
            {series.map((s, index) => (
              <linearGradient key={s.key} id={`${gradientId}-${index}`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor={s.color} stopOpacity={0.35} />
                <stop offset="95%" stopColor={s.color} stopOpacity={0} />
              </linearGradient>
            ))}
          </defs>
          <CartesianGrid stroke={CHART_COLORS.grid} strokeDasharray="3 3" vertical={false} />
          <XAxis dataKey={xKey} stroke={CHART_COLORS.axis} tick={{ fontSize: 12 }} padding={xAxisPadding} />
          <YAxis stroke={CHART_COLORS.axis} tick={{ fontSize: 12 }} tickFormatter={formatValue} width={76} />
          <Tooltip
            formatter={(value) => (formatValue ? formatValue(value) : value)}
            contentStyle={TOOLTIP_STYLE}
            labelStyle={{ color: CHART_COLORS.label }}
            itemStyle={TOOLTIP_ITEM_STYLE}
            cursor={{ stroke: CHART_COLORS.cursor, strokeWidth: 1 }}
          />
          <Legend wrapperStyle={{ fontSize: 12, color: CHART_COLORS.label }} />
          {series.map((s, index) => (
            <Area
              key={s.key}
              type="monotone"
              dataKey={s.key}
              name={s.label ?? s.key}
              stroke={s.color}
              strokeWidth={2}
              strokeDasharray={s.strokeDasharray}
              fill={`url(#${gradientId}-${index})`}
              dot={false}
              activeDot={{ r: 4 }}
            />
          ))}
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
