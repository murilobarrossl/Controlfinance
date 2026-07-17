import { PieChart, Pie, Cell, Tooltip, ResponsiveContainer } from "recharts";
import { TOOLTIP_STYLE, TOOLTIP_ITEM_STYLE, FALLBACK_COLORS } from "./chartTheme.js";
import "./CategoryDonutChart.css";

export default function CategoryDonutChart({
  data,
  height = 240,
  showLegend = true,
  formatValue,
  onSliceClick,
  centerLabel,
  centerCaption = "Total",
}) {
  return (
    <div className={`category-donut ${showLegend ? "category-donut--with-legend" : ""}`}>
      <div className="category-donut__chart">
        <ResponsiveContainer width="100%" height={height}>
          <PieChart>
            <Pie
              data={data}
              dataKey="value"
              nameKey="name"
              innerRadius="62%"
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
          </PieChart>
        </ResponsiveContainer>

        {centerLabel && (
          <div className="category-donut__center">
            <span className="category-donut__center-value">{centerLabel}</span>
            <span className="category-donut__center-label">{centerCaption}</span>
          </div>
        )}
      </div>

      {showLegend && (
        <ul className="category-donut__legend">
          {data.map((entry, index) => (
            <li key={entry.name ?? index} className="category-donut__legend-item">
              <span
                className="category-donut__legend-dot"
                style={{ backgroundColor: entry.color || FALLBACK_COLORS[index % FALLBACK_COLORS.length] }}
              />
              <span className="category-donut__legend-name">{entry.name}</span>
              <span className="category-donut__legend-value">
                {formatValue ? formatValue(entry.value) : entry.value}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
