export function breakdownFromAggregates(categories, type) {
  const totals = new Map();
  for (const c of categories ?? []) {
    if (c.type !== type) continue;
    const name = c.categoryName || "Sem categoria";
    const entry = totals.get(name) || { name, value: 0, count: 0 };
    entry.value += c.amount;
    entry.count += c.count;
    totals.set(name, entry);
  }
  const total = [...totals.values()].reduce((sum, e) => sum + e.value, 0);
  return [...totals.values()]
    .map((e) => ({ ...e, percentage: total > 0 ? (e.value / total) * 100 : 0 }))
    .sort((a, b) => b.value - a.value);
}
