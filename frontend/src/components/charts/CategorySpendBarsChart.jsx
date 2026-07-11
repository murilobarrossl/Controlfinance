import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Cell, ResponsiveContainer } from "recharts";
import { TOOLTIP_STYLE, TOOLTIP_ITEM_STYLE, FALLBACK_COLORS } from "./chartTheme.js";
import "./CategorySpendBarsChart.css";

const MIN_SLOT_WIDTH = 72;

export default function CategorySpendBarsChart({ data, height = 280, formatValue, onBarClick }) {
  const sorted = [...data].sort((a, b) => a.name.localeCompare(b.name, "pt-BR"));
  const chartWidth = Math.max(sorted.length * MIN_SLOT_WIDTH, 100);

  return (
    <div className="category-spend-bars">
      <div style={{ width: `${chartWidth}px`, height }}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={sorted} margin={{ top: 8, right: 16, left: 16, bottom: 8 }}>
            <CartesianGrid stroke="#2a2a2a" strokeDasharray="3 3" vertical={false} />
            <XAxis
              dataKey="name"
              stroke="#808080"
              tick={{ fontSize: 11 }}
              interval={0}
              angle={-35}
              textAnchor="end"
              height={80}
            />
            <YAxis stroke="#808080" tick={{ fontSize: 12 }} tickFormatter={formatValue} width={82} />
            <Tooltip
              formatter={(value) => (formatValue ? formatValue(value) : value)}
              contentStyle={TOOLTIP_STYLE}
              labelStyle={{ color: "#b8b8b8" }}
              itemStyle={TOOLTIP_ITEM_STYLE}
              cursor={{ fill: "#242424" }}
            />
            <Bar
              dataKey="value"
              radius={[3, 3, 0, 0]}
              maxBarSize={26}
              onClick={onBarClick ? (entry) => onBarClick(entry.name) : undefined}
              cursor={onBarClick ? "pointer" : "default"}
            >
              {sorted.map((entry, index) => (
                <Cell key={entry.name ?? index} fill={entry.color || FALLBACK_COLORS[index % FALLBACK_COLORS.length]} />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
