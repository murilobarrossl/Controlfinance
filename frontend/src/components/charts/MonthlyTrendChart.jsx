import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import { TOOLTIP_STYLE, TOOLTIP_ITEM_STYLE } from "./chartTheme.js";

export default function MonthlyTrendChart({ data, height = 260, formatValue }) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={data} margin={{ top: 8, right: 8, left: 0, bottom: 8 }}>
        <CartesianGrid stroke="#2a2a2a" strokeDasharray="3 3" vertical={false} />
        <XAxis dataKey="month" stroke="#808080" tick={{ fontSize: 12 }} />
        <YAxis stroke="#808080" tick={{ fontSize: 12 }} tickFormatter={formatValue} width={64} />
        <Tooltip
          formatter={(value) => (formatValue ? formatValue(value) : value)}
          contentStyle={TOOLTIP_STYLE}
          labelStyle={{ color: "#b8b8b8" }}
          itemStyle={TOOLTIP_ITEM_STYLE}
          cursor={{ fill: "#242424" }}
        />
        <Legend wrapperStyle={{ fontSize: 12, color: "#b8b8b8" }} />
        <Bar dataKey="Receitas" fill="#4ECDC4" radius={[3, 3, 0, 0]} maxBarSize={40} />
        <Bar dataKey="Despesas" fill="#ED4A31" radius={[3, 3, 0, 0]} maxBarSize={40} />
      </BarChart>
    </ResponsiveContainer>
  );
}
