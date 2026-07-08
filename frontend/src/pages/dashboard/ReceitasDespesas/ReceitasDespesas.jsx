import { useEffect, useMemo, useState } from "react";
import { getTransactions } from "../../../api/transactions.js";
import Card from "../../../components/ui/Card/Card.jsx";
import BarComparisonChart from "../../../components/charts/BarComparisonChart.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import "./ReceitasDespesas.css";

const MONTH_LABEL = new Intl.DateTimeFormat("pt-BR", { month: "long", year: "numeric", timeZone: "UTC" });

function monthKey(date) {
  return `${date.getUTCFullYear()}-${date.getUTCMonth()}`;
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
  const [selectedMonth, setSelectedMonth] = useState(() => monthKey(new Date()));

  useEffect(() => {
    getTransactions()
      .then(setTransactions)
      .catch((err) => setError(err.message || "Não foi possível carregar as transações."))
      .finally(() => setLoading(false));
  }, []);

  const monthDate = useMemo(() => {
    const [year, month] = selectedMonth.split("-").map(Number);
    return new Date(Date.UTC(year, month, 1));
  }, [selectedMonth]);

  function shiftMonth(delta) {
    const next = new Date(monthDate);
    next.setUTCMonth(next.getUTCMonth() + delta);
    setSelectedMonth(monthKey(next));
  }

  const monthTransactions = useMemo(
    () => transactions.filter((t) => monthKey(new Date(t.dueDate)) === selectedMonth),
    [transactions, selectedMonth]
  );

  const incomes = monthTransactions.filter((t) => t.type === "Income");
  const expenses = monthTransactions.filter((t) => t.type === "Expense");
  const totalIncome = incomes.reduce((sum, t) => sum + t.amount, 0);
  const totalExpense = expenses.reduce((sum, t) => sum + t.amount, 0);
  const topIncomes = topByCategory(incomes);
  const topExpenses = topByCategory(expenses);
  const biggestIncome = biggestTransaction(incomes);
  const biggestExpense = biggestTransaction(expenses);

  if (loading) return <p className="receitas-despesas__hint">Carregando...</p>;
  if (error) return <p className="receitas-despesas__error">{error}</p>;

  return (
    <div className="receitas-despesas">
      <div className="receitas-despesas__month-picker">
        <button type="button" onClick={() => shiftMonth(-1)} aria-label="Mês anterior">
          ‹
        </button>
        <span>{MONTH_LABEL.format(monthDate)}</span>
        <button type="button" onClick={() => shiftMonth(1)} aria-label="Próximo mês">
          ›
        </button>
      </div>

      <Card title="Receitas x despesas no mês">
        <BarComparisonChart
          height={260}
          formatValue={formatCurrency}
          data={[
            { name: "Receitas", value: totalIncome, color: "#4ECDC4" },
            { name: "Despesas", value: totalExpense, color: "#ED4A31" },
          ]}
        />
      </Card>

      <div className="receitas-despesas__grid">
        <Card title="Maiores fontes de receita">
          {topIncomes.length === 0 ? (
            <p className="receitas-despesas__hint">Sem receitas neste mês.</p>
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
            <p className="receitas-despesas__hint">Sem despesas neste mês.</p>
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
