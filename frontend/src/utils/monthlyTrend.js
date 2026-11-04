import { formatMonthShort, toIsoDate } from "./monthLabel.js";

export function getMonthsWindow(count, offset = 0) {
  const now = new Date();
  const months = [];
  for (let i = count - 1; i >= 0; i--) {
    months.push(new Date(Date.UTC(now.getFullYear(), now.getMonth() - i - offset * count, 1)));
  }
  return months;
}

export function monthRange(firstMonth, lastMonth = firstMonth) {
  const end = new Date(Date.UTC(lastMonth.getUTCFullYear(), lastMonth.getUTCMonth() + 1, 1));
  return { from: toIsoDate(firstMonth), to: toIsoDate(end) };
}

export function previousMonthSamePeriodRange(referenceDate = new Date()) {
  const start = new Date(Date.UTC(referenceDate.getFullYear(), referenceDate.getMonth() - 1, 1));
  const monthEnd = Date.UTC(referenceDate.getFullYear(), referenceDate.getMonth(), 1);
  const cutoffEnd = Date.UTC(referenceDate.getFullYear(), referenceDate.getMonth() - 1, referenceDate.getDate() + 1);
  return { from: toIsoDate(start), to: toIsoDate(new Date(Math.min(monthEnd, cutoffEnd))) };
}

export function findAggregateMonth(aggregates, monthDate) {
  return (
    aggregates?.months?.find(
      (m) => m.year === monthDate.getUTCFullYear() && m.month === monthDate.getUTCMonth() + 1
    ) ?? null
  );
}

export function buildMonthlyTrend(aggregates, months) {
  return months.map((monthDate) => {
    const entry = findAggregateMonth(aggregates, monthDate);
    return {
      month: formatMonthShort(monthDate),
      Receitas: entry ? entry.income : null,
      Despesas: entry ? entry.expense : null,
    };
  });
}
