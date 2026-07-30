import { monthKey, formatMonthShort } from "./monthLabel.js";

// offset 0 = janela atual; offset 1 = a janela anterior a essa; e por aí vai.
export function getMonthsWindow(count, offset = 0) {
  const now = new Date();
  const months = [];
  for (let i = count - 1; i >= 0; i--) {
    months.push(new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - i - offset * count, 1)));
  }
  return months;
}

export function buildMonthlyTrend(transactions, months) {
  return months.map((monthDate) => {
    const key = monthKey(monthDate);
    const monthTransactions = transactions.filter((t) => monthKey(new Date(t.dueDate)) === key);
    return {
      month: formatMonthShort(monthDate),
      Receitas: monthTransactions.filter((t) => t.type === "Income").reduce((sum, t) => sum + t.amount, 0),
      Despesas: monthTransactions.filter((t) => t.type === "Expense").reduce((sum, t) => sum + t.amount, 0),
    };
  });
}
