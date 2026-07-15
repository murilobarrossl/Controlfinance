import { useEffect, useMemo, useState } from "react";
import { getTransactions } from "../../../api/transactions.js";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import BarComparisonChart from "../../../components/charts/BarComparisonChart.jsx";
import MonthlyTrendChart from "../../../components/charts/MonthlyTrendChart.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import "./ReceitasDespesas.css";

const MONTHS_WINDOW = 3;
const MONTH_SHORT_LABEL = new Intl.DateTimeFormat("pt-BR", { month: "short", timeZone: "UTC" });

function monthKey(date) {
  return `${date.getUTCFullYear()}-${date.getUTCMonth()}`;
}

function formatMonthShort(date) {
  const label = MONTH_SHORT_LABEL.format(date).replace(".", "");
  const capitalized = label.charAt(0).toUpperCase() + label.slice(1);
  return `${capitalized}/${String(date.getUTCFullYear()).slice(2)}`;
}

// offset 0 = janela atual (últimos 3 meses); offset 1 = os 3 meses anteriores a essa janela; e por aí vai.
function getMonthsWindow(count, offset) {
  const now = new Date();
  const months = [];
  for (let i = count - 1; i >= 0; i--) {
    months.push(new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - i - offset * count, 1)));
  }
  return months;
}

function topByCategory(transactions, limit = 5) {
  const totals = new Map();
  for (const t of transactions) {
    const name = t.categoryName || "Sem categoria";
    totals.set(name, (totals.get(name) || 0) + t.amount);
  }
  return [...totals.entries()]
    .map(([name, value]) => ({ name, value }))
    .sort((a, b) => b.value - a.value)
    .slice(0, limit);
}

function biggestTransaction(transactions) {
  return transactions.reduce((max, t) => (!max || t.amount > max.amount ? t : max), null);
}

export default function ReceitasDespesas() {
  const [transactions, setTransactions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [windowOffset, setWindowOffset] = useState(0);

  useEffect(() => {
    getTransactions()
      .then(setTransactions)
      .catch((err) => setError(err.message || "Não foi possível carregar as transações."))
      .finally(() => setLoading(false));
  }, []);

  const windowMonths = useMemo(() => getMonthsWindow(MONTHS_WINDOW, windowOffset), [windowOffset]);
  const rangeLabel = `${formatMonthShort(windowMonths[0])} – ${formatMonthShort(windowMonths[windowMonths.length - 1])}`;

  const monthlyTrend = useMemo(
    () =>
      windowMonths.map((monthDate) => {
        const key = monthKey(monthDate);
        const monthTransactions = transactions.filter((t) => monthKey(new Date(t.dueDate)) === key);
        return {
          month: formatMonthShort(monthDate),
          Receitas: monthTransactions.filter((t) => t.type === "Income").reduce((sum, t) => sum + t.amount, 0),
          Despesas: monthTransactions.filter((t) => t.type === "Expense").reduce((sum, t) => sum + t.amount, 0),
        };
      }),
    [transactions, windowMonths]
  );

  const windowTransactions = useMemo(() => {
    const keys = new Set(windowMonths.map(monthKey));
    return transactions.filter((t) => keys.has(monthKey(new Date(t.dueDate))));
  }, [transactions, windowMonths]);

  const incomes = windowTransactions.filter((t) => t.type === "Income");
  const expenses = windowTransactions.filter((t) => t.type === "Expense");
  const topIncomes = topByCategory(incomes);
  const topExpenses = topByCategory(expenses);
  const biggestIncome = biggestTransaction(incomes);
  const biggestExpense = biggestTransaction(expenses);

  if (loading) return <p className="receitas-despesas__hint">Carregando...</p>;
  if (error) return <p className="receitas-despesas__error">{error}</p>;

  return (
    <div className="receitas-despesas">
      <SectionHeading kicker="Movimentações" title="Receitas e despesas" align="left" />

      <div className="receitas-despesas__window-picker">
        <button
          type="button"
          onClick={() => setWindowOffset((prev) => prev + 1)}
          aria-label="3 meses anteriores"
        >
          ‹
        </button>
        <span>{rangeLabel}</span>
        <button
          type="button"
          onClick={() => setWindowOffset((prev) => Math.max(0, prev - 1))}
          disabled={windowOffset === 0}
          aria-label="3 meses seguintes"
        >
          ›
        </button>
      </div>

      <Card title={`Receitas x despesas: ${rangeLabel}`}>
        <MonthlyTrendChart height={260} formatValue={formatCurrency} data={monthlyTrend} />
      </Card>

      <div className="receitas-despesas__grid">
        <Card title="Maiores fontes de receita">
          {topIncomes.length === 0 ? (
            <p className="receitas-despesas__hint">Sem receitas nesse período.</p>
          ) : (
            <BarComparisonChart layout="vertical" height={220} formatValue={formatCurrency} data={topIncomes} />
          )}
          {biggestIncome && (
            <p className="receitas-despesas__highlight">
              Maior receita: <strong>{biggestIncome.name}</strong> ({formatCurrency(biggestIncome.amount)})
            </p>
          )}
        </Card>

        <Card title="Maiores despesas">
          {topExpenses.length === 0 ? (
            <p className="receitas-despesas__hint">Sem despesas nesse período.</p>
          ) : (
            <BarComparisonChart layout="vertical" height={220} formatValue={formatCurrency} data={topExpenses} />
          )}
          {biggestExpense && (
            <p className="receitas-despesas__highlight">
              Maior despesa: <strong>{biggestExpense.name}</strong> ({formatCurrency(biggestExpense.amount)})
            </p>
          )}
        </Card>
      </div>
    </div>
  );
}
