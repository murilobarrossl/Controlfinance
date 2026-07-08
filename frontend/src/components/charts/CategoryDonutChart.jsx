import { PieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from "recharts";

const TOOLTIP_STYLE = {
  backgroundColor: "#1a1a1a",
  border: "1px solid #2a2a2a",
  borderRadius: 8,
  color: "#ffffff",
};

const FALLBACK_COLORS = ["#ED4A31", "#4ECDC4", "#45B7D1", "#96CEB4", "#FFEAA7", "#B39DDB", "#F4A261"];

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
