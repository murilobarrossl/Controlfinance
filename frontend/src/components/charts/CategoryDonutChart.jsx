import { PieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { TOOLTIP_STYLE, TOOLTIP_ITEM_STYLE, FALLBACK_COLORS } from "./chartTheme.js";
import "./CategoryDonutChart.css";

export default function CategoryDonutChart({
  data,
  height = 240,
  showLegend = true,
  formatValue,
  onSliceClick,
  centerLabel,
}) {
  return (
    <div className="category-donut">
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
            itemStyle={TOOLTIP_ITEM_STYLE}
          />
          {showLegend && (
            <Legend verticalAlign="bottom" iconType="circle" wrapperStyle={{ color: "#b8b8b8", fontSize: 12 }} />
          )}
        </PieChart>
      </ResponsiveContainer>

      {centerLabel && !showLegend && (
        <div className="category-donut__center">
          <span className="category-donut__center-label">Total</span>
          <span className="category-donut__center-value">{centerLabel}</span>
        </div>
      )}
    </div>
  );
}
