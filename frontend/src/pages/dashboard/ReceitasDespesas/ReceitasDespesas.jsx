import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { getTransactions } from "../../../api/transactions.js";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import BreakdownRows from "../../../components/dashboard/BreakdownRows/BreakdownRows.jsx";
import { TrendUpIcon, TrendDownIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { monthKey, formatMonthLong } from "../../../utils/monthLabel.js";
import { getMonthsWindow } from "../../../utils/monthlyTrend.js";
import { computeTrend } from "../../../utils/trend.js";
import "./ReceitasDespesas.css";

function breakdownByCategory(transactions) {
  const totals = new Map();
  for (const t of transactions) {
    const name = t.categoryName || "Sem categoria";
    const entry = totals.get(name) || { name, value: 0, count: 0 };
    entry.value += t.amount;
    entry.count += 1;
    totals.set(name, entry);
  }
  const total = [...totals.values()].reduce((sum, e) => sum + e.value, 0);
  return [...totals.values()]
    .map((e) => ({ ...e, percentage: total > 0 ? (e.value / total) * 100 : 0 }))
    .sort((a, b) => b.value - a.value);
}

// Decide se a frase-resumo destaca só a maior fatia ou a soma das duas maiores: uma única
// fatia dominante (>=40%) já responde a pergunta sozinha; senão, junta as duas pra não
// esconder que o gasto/renda está espalhado entre duas fontes parecidas.
function topConcentration(breakdown) {
  if (breakdown.length === 0) return null;
  if (breakdown.length === 1 || breakdown[0].percentage >= 40) {
    return { top: [breakdown[0]], combinedPercentage: breakdown[0].percentage };
  }
  return { top: breakdown.slice(0, 2), combinedPercentage: breakdown[0].percentage + breakdown[1].percentage };
}

function incomeSummarySentence(breakdown) {
  const concentration = topConcentration(breakdown);
  if (!concentration) return null;
  const [first] = concentration.top;
  return `Sua renda vem principalmente de ${first.name}, que representa ${formatPercentage(first.percentage)} do total.`;
}

function expenseSummarySentence(breakdown) {
  const concentration = topConcentration(breakdown);
  if (!concentration) return null;
  if (concentration.top.length === 1) {
    return `Seus gastos se concentram em ${concentration.top[0].name}, que representa ${formatPercentage(
      concentration.combinedPercentage
    )} do total.`;
  }
  const [a, b] = concentration.top;
  return `Seus gastos se concentram em ${a.name} e ${b.name}, que juntas são ${formatPercentage(
    concentration.combinedPercentage
  )} do total.`;
}

function closingSentence(totalIncome, totalExpense) {
  const leftover = totalIncome - totalExpense;
  if (totalIncome === 0 && totalExpense === 0) return "Nenhuma movimentação neste período.";
  if (leftover >= 0) {
    const pct = totalIncome > 0 ? (leftover / totalIncome) * 100 : 0;
    return `Você recebeu ${formatCurrency(totalIncome)} e gastou ${formatCurrency(
      totalExpense
    )} neste período. Sobraram ${formatCurrency(leftover)} — ${formatPercentage(pct)} do que entrou.`;
  }
  return `Você recebeu ${formatCurrency(totalIncome)} mas gastou ${formatCurrency(
    totalExpense
  )} neste período — ${formatCurrency(Math.abs(leftover))} a mais do que entrou.`;
}

export default function ReceitasDespesas() {
  const navigate = useNavigate();
  const [transactions, setTransactions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [monthOffset, setMonthOffset] = useState(0);
  const [expandedIncomeSources, setExpandedIncomeSources] = useState(() => new Set());

  useEffect(() => {
    getTransactions()
      .then(setTransactions)
      .catch((err) => setError(err.message || "Não foi possível carregar as transações."))
      .finally(() => setLoading(false));
  }, []);

  const currentMonth = useMemo(() => getMonthsWindow(1, monthOffset)[0], [monthOffset]);
  const previousMonth = useMemo(() => getMonthsWindow(1, monthOffset + 1)[0], [monthOffset]);

  const { incomes, expenses, previousTotalIncome, previousTotalExpense } = useMemo(() => {
    const currentKey = monthKey(currentMonth);
    const previousKey = monthKey(previousMonth);
    const nonTransfer = transactions.filter((t) => !t.isTransfer);

    const currentTx = nonTransfer.filter((t) => monthKey(new Date(t.dueDate)) === currentKey);
    const previousTx = nonTransfer.filter((t) => monthKey(new Date(t.dueDate)) === previousKey);

    return {
      incomes: currentTx.filter((t) => t.type === "Income"),
      expenses: currentTx.filter((t) => t.type === "Expense"),
      previousTotalIncome: previousTx.filter((t) => t.type === "Income").reduce((sum, t) => sum + t.amount, 0),
      previousTotalExpense: previousTx.filter((t) => t.type === "Expense").reduce((sum, t) => sum + t.amount, 0),
    };
  }, [transactions, currentMonth, previousMonth]);

  const totalIncome = useMemo(() => incomes.reduce((sum, t) => sum + t.amount, 0), [incomes]);
  const totalExpense = useMemo(() => expenses.reduce((sum, t) => sum + t.amount, 0), [expenses]);

  const incomeBreakdown = useMemo(() => breakdownByCategory(incomes), [incomes]);
  const expenseBreakdown = useMemo(() => breakdownByCategory(expenses), [expenses]);

  const incomeTrend = computeTrend(totalIncome, previousTotalIncome);
  const expenseTrend = computeTrend(totalExpense, previousTotalExpense, { invertTone: true });

  function toggleIncomeSource(name) {
    setExpandedIncomeSources((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  }

  function goToCategory(categoryName) {
    navigate(`/dashboard/categorias?categoria=${encodeURIComponent(categoryName)}`);
  }

  if (loading) return <p className="receitas-despesas__hint">Carregando...</p>;
  if (error) return <p className="receitas-despesas__error">{error}</p>;

  const leftover = totalIncome - totalExpense;

  return (
    <div className="receitas-despesas">
      <SectionHeading kicker="O mapa do seu dinheiro" title="Receitas e despesas" align="left" />

      <RangePicker
        label={formatMonthLong(currentMonth)}
        onPrev={() => setMonthOffset((prev) => prev + 1)}
        onNext={() => setMonthOffset((prev) => Math.max(0, prev - 1))}
        nextDisabled={monthOffset === 0}
      />

      <div className="receitas-despesas__halves">
        <section className="receitas-despesas__half">
          <StatCard
            icon={<TrendUpIcon />}
            label="Receitas"
            value={formatCurrency(totalIncome)}
            valueTone="income"
            trend={incomeTrend}
          />

          {incomeBreakdown.length === 0 ? (
            <p className="receitas-despesas__hint">Sem receitas neste período.</p>
          ) : (
            <>
              <p className="receitas-despesas__sentence">{incomeSummarySentence(incomeBreakdown)}</p>
              <BreakdownRows
                data={incomeBreakdown}
                tone="income"
                onRowClick={toggleIncomeSource}
                expandedNames={expandedIncomeSources}
                renderExpanded={(name) => (
                  <ul className="rd-transactions">
                    {incomes
                      .filter((t) => (t.categoryName || "Sem categoria") === name)
                      .sort((a, b) => new Date(b.dueDate) - new Date(a.dueDate))
                      .map((t) => (
                        <li key={t.id} className="rd-transactions__item">
                          <span className="rd-transactions__name">{t.name}</span>
                          <span className="rd-transactions__date">{new Date(t.dueDate).toLocaleDateString("pt-BR")}</span>
                          <span className="rd-transactions__amount">{formatCurrency(t.amount)}</span>
                        </li>
                      ))}
                  </ul>
                )}
              />
            </>
          )}
        </section>

        <section className="receitas-despesas__half">
          <StatCard
            icon={<TrendDownIcon />}
            label="Despesas"
            value={formatCurrency(totalExpense)}
            valueTone="expense"
            trend={expenseTrend}
          />

          {expenseBreakdown.length === 0 ? (
            <p className="receitas-despesas__hint">Sem despesas neste período.</p>
          ) : (
            <>
              <p className="receitas-despesas__sentence">{expenseSummarySentence(expenseBreakdown)}</p>
              <BreakdownRows data={expenseBreakdown} tone="expense" onRowClick={goToCategory} showLinkHint />
            </>
          )}
        </section>
      </div>

      <Card title="Resultado do período" className="receitas-despesas__closing">
        <p
          className={`receitas-despesas__closing-value ${
            leftover >= 0 ? "receitas-despesas__closing-value--income" : "receitas-despesas__closing-value--expense"
          }`}
        >
          {formatCurrency(leftover)}
        </p>
        <p className="receitas-despesas__closing-text">{closingSentence(totalIncome, totalExpense)}</p>
      </Card>
    </div>
  );
}
