import { PieChart, Pie, Cell, Tooltip, ResponsiveContainer } from "recharts";
import { TOOLTIP_STYLE, TOOLTIP_ITEM_STYLE } from "./chartTheme.js";
import { donutSliceColors } from "../../utils/categoryColors.js";
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
  const format = (value) => (formatValue ? formatValue(value) : value);
  const total = data.reduce((sum, entry) => sum + entry.value, 0);
  const colors = donutSliceColors(data);

  return (
    <div className={`category-donut ${showLegend ? "category-donut--with-legend" : ""}`}>
      {/* Largura fixa igual à altura: antes o gráfico esticava (flex: 1) e a rosca ficava pequena
          no meio de um retângulo largo, com o valor central maior que o furo da rosca. */}
      <div className="category-donut__chart" style={{ width: height, maxWidth: "100%" }}>
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={data}
              dataKey="value"
              nameKey="name"
              innerRadius="66%"
              outerRadius="96%"
              // Com uma fatia só, o espaçamento abria um "corte" na rosca que parecia dado faltando.
              paddingAngle={data.length > 1 ? 2 : 0}
              stroke="none"
              isAnimationActive={false}
              onClick={onSliceClick ? (entry) => onSliceClick(entry.name) : undefined}
              cursor={onSliceClick ? "pointer" : "default"}
            >
              {data.map((entry, index) => (
                <Cell key={entry.name ?? index} fill={colors[index]} />
              ))}
            </Pie>
            <Tooltip
              formatter={(value, name) => [
                `${format(value)}${total > 0 ? ` (${Math.round((value / total) * 100)}%)` : ""}`,
                name,
              ]}
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
          {data.map((entry, index) => {
            const percent = total > 0 ? (entry.value / total) * 100 : 0;
            const color = colors[index];
            return (
              <li key={entry.name ?? index} className="category-donut__legend-item">
                <div className="category-donut__legend-row">
                  <span className="category-donut__legend-dot" style={{ backgroundColor: color }} />
                  <span className="category-donut__legend-name" title={entry.name}>
                    {entry.name}
                  </span>
                  <span className="category-donut__legend-percent">{Math.round(percent)}%</span>
                  <span className="category-donut__legend-value">{format(entry.value)}</span>
                </div>
                <div className="category-donut__legend-bar" aria-hidden="true">
                  <div style={{ width: `${percent}%`, backgroundColor: color }} />
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
