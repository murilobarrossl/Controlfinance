import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Cell, ResponsiveContainer } from "recharts";
import { TOOLTIP_STYLE, TOOLTIP_ITEM_STYLE } from "./chartTheme.js";

export default function BarComparisonChart({ data, height = 220, layout = "horizontal", formatValue, highlightMax = false }) {
  const isVertical = layout === "vertical";
  const maxValue = highlightMax ? Math.max(...data.map((d) => d.value)) : null;

  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart
        data={data}
        layout={isVertical ? "vertical" : "horizontal"}
        margin={isVertical ? { top: 8, right: 24, left: 0, bottom: 8 } : { top: 8, right: 8, left: 0, bottom: 8 }}
      >
        <CartesianGrid stroke="#2a2a2a" strokeDasharray="3 3" vertical={isVertical} horizontal={!isVertical} />
        {isVertical ? (
          <>
            <XAxis type="number" stroke="#808080" tick={{ fontSize: 12 }} tickFormatter={formatValue} />
            <YAxis type="category" dataKey="name" stroke="#808080" width={110} tick={{ fontSize: 12 }} />
          </>
        ) : (
          <>
            <XAxis dataKey="name" stroke="#808080" tick={{ fontSize: 12 }} />
            <YAxis stroke="#808080" tick={{ fontSize: 12 }} tickFormatter={formatValue} />
          </>
        )}
        <Tooltip
          formatter={(value) => (formatValue ? formatValue(value) : value)}
          contentStyle={TOOLTIP_STYLE}
          labelStyle={{ color: "#b8b8b8" }}
          itemStyle={TOOLTIP_ITEM_STYLE}
          cursor={{ fill: "#242424" }}
        />
        <Bar dataKey="value" radius={[6, 6, 6, 6]} maxBarSize={isVertical ? 22 : 40} barGap={4}>
          {data.map((entry, index) => (
            <Cell
              key={entry.name ?? index}
              fill={entry.color || "#ED4A31"}
              opacity={maxValue !== null && entry.value !== maxValue ? 0.45 : 1}
            />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}
