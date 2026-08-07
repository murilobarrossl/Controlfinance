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
    const monthTransactions = transactions.filter((t) => !t.isTransfer && monthKey(new Date(t.dueDate)) === key);
    return {
      month: formatMonthShort(monthDate),
      Receitas: monthTransactions.filter((t) => t.type === "Income").reduce((sum, t) => sum + t.amount, 0),
      Despesas: monthTransactions.filter((t) => t.type === "Expense").reduce((sum, t) => sum + t.amount, 0),
    };
  });
}

// Comparar o mês corrente (quase sempre parcial, só os dias já decorridos) contra o mês anterior
// INTEIRO sempre mostra uma queda enorme nos primeiros dias do mês, mesmo sem nada de anormal
// acontecendo — não é uma tendência de verdade, é só o mês ainda não ter terminado. Recorta o mês
// anterior no mesmo dia-do-mês de hoje pra comparar quantidades comparáveis (dia 1-7 com dia 1-7).
export function buildPreviousMonthSamePeriod(transactions, referenceDate = new Date()) {
  const previousMonthDate = new Date(
    Date.UTC(referenceDate.getUTCFullYear(), referenceDate.getUTCMonth() - 1, 1)
  );
  const key = monthKey(previousMonthDate);
  const dayCutoff = referenceDate.getUTCDate();
  const monthTransactions = transactions.filter(
    (t) => !t.isTransfer && monthKey(new Date(t.dueDate)) === key && new Date(t.dueDate).getUTCDate() <= dayCutoff
  );
  return {
    Receitas: monthTransactions.filter((t) => t.type === "Income").reduce((sum, t) => sum + t.amount, 0),
    Despesas: monthTransactions.filter((t) => t.type === "Expense").reduce((sum, t) => sum + t.amount, 0),
  };
}
