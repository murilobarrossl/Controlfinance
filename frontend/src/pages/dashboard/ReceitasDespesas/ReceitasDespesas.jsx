import { useEffect, useMemo, useState } from "react";
import { getTransactions } from "../../../api/transactions.js";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import BarComparisonChart from "../../../components/charts/BarComparisonChart.jsx";
import AreaTrendChart from "../../../components/charts/AreaTrendChart.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import { monthKey, formatMonthShort } from "../../../utils/monthLabel.js";
import { getMonthsWindow, buildMonthlyTrend } from "../../../utils/monthlyTrend.js";
import "./ReceitasDespesas.css";

const MONTHS_WINDOW = 3;

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

  const monthlyTrend = useMemo(() => buildMonthlyTrend(transactions, windowMonths), [transactions, windowMonths]);

  const windowTransactions = useMemo(() => {
    const keys = new Set(windowMonths.map(monthKey));
    return transactions.filter((t) => !t.isTransfer && keys.has(monthKey(new Date(t.dueDate))));
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

      <RangePicker
        label={rangeLabel}
        onPrev={() => setWindowOffset((prev) => prev + 1)}
        onNext={() => setWindowOffset((prev) => Math.max(0, prev - 1))}
        nextDisabled={windowOffset === 0}
      />

      <Card title={`Receitas x despesas: ${rangeLabel}`}>
        <AreaTrendChart
          height={260}
          formatValue={formatCurrency}
          series={[
            { key: "Receitas", color: "#4ECDC4" },
            { key: "Despesas", color: "#ED4A31" },
          ]}
          data={monthlyTrend}
        />
      </Card>

      <div className="receitas-despesas__grid">
        <Card title="Maiores fontes de receita">
          {topIncomes.length === 0 ? (
            <p className="receitas-despesas__hint">Sem receitas nesse período.</p>
          ) : (
            <BarComparisonChart layout="vertical" height={220} formatValue={formatCurrency} data={topIncomes} highlightMax />
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
            <BarComparisonChart layout="vertical" height={220} formatValue={formatCurrency} data={topExpenses} highlightMax />
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
