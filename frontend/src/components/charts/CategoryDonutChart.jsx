import { PieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { TOOLTIP_STYLE, FALLBACK_COLORS } from "./chartTheme.js";

export default function CategoryDonutChart({ data, height = 240, showLegend = true, formatValue, onSliceClick }) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <PieChart>
        <Pie
          data={data}
          dataKey="value"
          nameKey="name"
          innerRadius="55%"
          outerRadius="80%"
          paddingAngle={2}
          onClick={onSliceClick ? (entry) => onSliceClick(entry.name) : undefined}
          cursor={onSliceClick ? "pointer" : "default"}
        >
          {data.map((entry, index) => (
            <Cell key={entry.name ?? index} fill={entry.color || FALLBACK_COLORS[index % FALLBACK_COLORS.length]} />
          ))}
        </Pie>
        <Tooltip
          formatter={(value) => (formatValue ? formatValue(value) : value)}
          contentStyle={TOOLTIP_STYLE}
        />
        {showLegend && (
          <Legend verticalAlign="bottom" iconType="circle" wrapperStyle={{ color: "#b8b8b8", fontSize: 12 }} />
        )}
      </PieChart>
    </ResponsiveContainer>
  );
}
