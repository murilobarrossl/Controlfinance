import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router";
import { getTransactionAggregates } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import BreakdownRows from "../../../components/dashboard/BreakdownRows/BreakdownRows.jsx";
import TransactionDetails from "../../../components/dashboard/TransactionDetails/TransactionDetails.jsx";
import { TrendUpIcon, TrendDownIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatDate, roundMoney, formatPercentage } from "../../../utils/financeMath.js";
import { formatMonthLong } from "../../../utils/monthLabel.js";
import { getMonthsWindow, monthRange } from "../../../utils/monthlyTrend.js";
import { computeTrend } from "../../../utils/trend.js";
import "./ReceitasDespesas.css";
import usePageTitle from "../../../hooks/usePageTitle.js";


function breakdownFromAggregates(categories, type) {
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
  const leftover = roundMoney(totalIncome - totalExpense);
  if (totalIncome === 0 && totalExpense === 0) return "Nenhuma movimentação neste período.";
  if (leftover >= 0) {
    const pct = totalIncome > 0 ? (leftover / totalIncome) * 100 : 0;
    return `Você recebeu ${formatCurrency(totalIncome)} e gastou ${formatCurrency(
      totalExpense
    )} neste período. Sobraram ${formatCurrency(leftover)}, ${formatPercentage(pct)} do que entrou.`;
  }
  return `Você recebeu ${formatCurrency(totalIncome)} mas gastou ${formatCurrency(
    totalExpense
  )} neste período, ${formatCurrency(Math.abs(leftover))} a mais do que entrou.`;
}

export default function ReceitasDespesas() {
  usePageTitle("Receitas e despesas");
  const navigate = useNavigate();
  const { effectiveAccountId, dataRefreshKey, accountsStatus } = useAccount();
  const [aggregateResult, setAggregateResult] = useState(null);
  const [monthOffset, setMonthOffset] = useState(0);
  const [expandedIncomeSources, setExpandedIncomeSources] = useState(() => new Set());
  const requestKey = `${effectiveAccountId ?? ""}|${dataRefreshKey}`;
  const aggregateKey = `${requestKey}|${monthOffset}`;
  const loading = accountsStatus !== "ready" || aggregateResult?.key !== aggregateKey;

  const currentMonth = useMemo(() => getMonthsWindow(1, monthOffset)[0], [monthOffset]);
  const previousMonth = useMemo(() => getMonthsWindow(1, monthOffset + 1)[0], [monthOffset]);

  useEffect(() => {
    if (accountsStatus !== "ready") return;

    const controller = new AbortController();
    const { signal } = controller;
    const key = `${effectiveAccountId ?? ""}|${dataRefreshKey}|${monthOffset}`;

    Promise.all([
      getTransactionAggregates({ bankAccountId: effectiveAccountId, ...monthRange(currentMonth) }, { signal }),
      getTransactionAggregates({ bankAccountId: effectiveAccountId, ...monthRange(previousMonth) }, { signal }),
    ])
      .then(([current, previous]) => {
        if (signal.aborted) return;
        setAggregateResult({ key, current, previous, error: "" });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setAggregateResult({
          key,
          current: null,
          previous: null,
          error: err.message || "Não foi possível carregar as transações.",
        });
      });

    return () => controller.abort();
  }, [effectiveAccountId, dataRefreshKey, monthOffset, currentMonth, previousMonth, accountsStatus]);

  const current = aggregateResult?.current ?? null;
  const previous = aggregateResult?.previous ?? null;
  const error =
    accountsStatus === "error"
      ? "Não foi possível carregar as contas."
      : (aggregateResult?.key === aggregateKey && aggregateResult.error) || "";

  const totalIncome = current?.totalIncome;
  const totalExpense = current?.totalExpense;

  const incomeBreakdown = useMemo(() => breakdownFromAggregates(current?.categories, "Income"), [current]);
  const expenseBreakdown = useMemo(() => breakdownFromAggregates(current?.categories, "Expense"), [current]);

  const incomeTrend = computeTrend(totalIncome, previous?.totalIncome);
  const expenseTrend = computeTrend(totalExpense, previous?.totalExpense, { invertTone: true });

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

  if (error) return <p className="receitas-despesas__error">{error}</p>;

  const leftover = roundMoney(totalIncome - totalExpense);

  return (
    <div className="receitas-despesas">
      <SectionHeading title="Receitas e despesas" level={1}>O mapa do seu dinheiro</SectionHeading>

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
            value={loading ? <Skeleton width="7rem" height="1.5rem" /> : formatCurrency(totalIncome)}
            valueTone="income"
            trend={loading ? null : incomeTrend}
          />

          {loading ? (
            <Skeleton width="100%" height="12rem" />
          ) : incomeBreakdown.length === 0 ? (
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
                  <TransactionDetails
                    className="rd-transactions"
                    query={{
                      bankAccountId: effectiveAccountId,
                      ...monthRange(currentMonth),
                      type: "Income",
                      category: name,
                      refresh: dataRefreshKey,
                    }}
                    renderItem={(t) => (
                      <li key={t.id} className="rd-transactions__item">
                        <span className="rd-transactions__name">{t.name}</span>
                        <span className="rd-transactions__date">{formatDate(t.dueDate)}</span>
                        <span className="rd-transactions__amount">{formatCurrency(t.amount)}</span>
                      </li>
                    )}
                  />
                )}
              />
            </>
          )}
        </section>

        <section className="receitas-despesas__half">
          <StatCard
            icon={<TrendDownIcon />}
            label="Despesas"
            value={loading ? <Skeleton width="7rem" height="1.5rem" /> : formatCurrency(totalExpense)}
            valueTone="expense"
            trend={loading ? null : expenseTrend}
          />

          {loading ? (
            <Skeleton width="100%" height="12rem" />
          ) : expenseBreakdown.length === 0 ? (
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
        {loading ? (
          <>
            <Skeleton width="8rem" height="2rem" />
            <Skeleton width="100%" height="1rem" />
          </>
        ) : (
          <>
            <p
              className={`receitas-despesas__closing-value ${
                leftover >= 0 ? "receitas-despesas__closing-value--income" : "receitas-despesas__closing-value--expense"
              }`}
            >
              {formatCurrency(leftover)}
            </p>
            <p className="receitas-despesas__closing-text">{closingSentence(totalIncome, totalExpense)}</p>
          </>
        )}
      </Card>
    </div>
  );
}
