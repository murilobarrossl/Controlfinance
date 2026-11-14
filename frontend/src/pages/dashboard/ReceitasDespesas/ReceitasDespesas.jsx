import { useEffect, useMemo, useState } from "react";
import { getTransactionAggregates } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import NoBankAccount from "../../../components/dashboard/NoBankAccount/NoBankAccount.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import TransactionFormDialog from "../../../components/dashboard/TransactionFormDialog/TransactionFormDialog.jsx";
import CategoryBlocks from "./CategoryBlocks.jsx";
import { TrendUpIcon, TrendDownIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, roundMoney, formatPercentage } from "../../../utils/financeMath.js";
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
  const { accounts, scopeKey, scopeQuery, effectiveScope, dataRefreshKey, bumpDataRefresh, accountsStatus } = useAccount();
  const [aggregateResult, setAggregateResult] = useState(null);
  const [newEntryOpen, setNewEntryOpen] = useState(false);
  const [monthOffset, setMonthOffset] = useState(0);
  const [expandedIncome, setExpandedIncome] = useState(() => new Set());
  const [expandedExpense, setExpandedExpense] = useState(() => new Set());
  const requestKey = `${scopeKey}|${dataRefreshKey}`;
  const aggregateKey = `${requestKey}|${monthOffset}`;
  const loading = accountsStatus !== "ready" || aggregateResult?.key !== aggregateKey;

  const currentMonth = useMemo(() => getMonthsWindow(1, monthOffset)[0], [monthOffset]);
  const previousMonth = useMemo(() => getMonthsWindow(1, monthOffset + 1)[0], [monthOffset]);

  useEffect(() => {
    if (accountsStatus !== "ready" || !scopeKey) return;

    const controller = new AbortController();
    const { signal } = controller;
    const key = `${scopeKey}|${dataRefreshKey}|${monthOffset}`;

    Promise.all([
      getTransactionAggregates({ ...scopeQuery, ...monthRange(currentMonth) }, { signal }),
      getTransactionAggregates({ ...scopeQuery, ...monthRange(previousMonth) }, { signal }),
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
  }, [scopeKey, scopeQuery, dataRefreshKey, monthOffset, currentMonth, previousMonth, accountsStatus]);

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

  function toggleIn(setter) {
    return (name) =>
      setter((prev) => {
        const next = new Set(prev);
        if (next.has(name)) next.delete(name);
        else next.add(name);
        return next;
      });
  }

  // Categorias só aceita "periodo=mes" para o mês corrente; num mês anterior ela abre no ano.
  function categoryHref(categoryName) {
    const params = new URLSearchParams();
    if (monthOffset === 0) params.set("periodo", "mes");
    params.set("categoria", categoryName);
    return `/dashboard/categorias?${params}`;
  }

  if (error) return <p className="receitas-despesas__error">{error}</p>;
  if (accountsStatus === "ready" && !scopeKey) return <NoBankAccount />;

  const leftover = roundMoney(totalIncome - totalExpense);
  const monthQuery = { ...scopeQuery, ...monthRange(currentMonth), refresh: dataRefreshKey };

  return (
    <div className="receitas-despesas">
      <header className="receitas-despesas__header">
        <SectionHeading title="Receitas e despesas" level={1} />
        <RangePicker
          label={formatMonthLong(currentMonth)}
          onPrev={() => setMonthOffset((prev) => prev + 1)}
          onNext={() => setMonthOffset((prev) => Math.max(0, prev - 1))}
          nextDisabled={monthOffset === 0}
        />
        <Button as="button" type="button" variant="primary" size="sm" onClick={() => setNewEntryOpen(true)}>
          + Novo lançamento
        </Button>
      </header>

      <TransactionFormDialog
        open={newEntryOpen}
        accounts={accounts}
        defaultAccountId={effectiveScope?.accountId ?? null}
        onClose={() => setNewEntryOpen(false)}
        onCreated={bumpDataRefresh}
      />

      <div className="receitas-despesas__halves">
        <section className="receitas-despesas__half" aria-label="Receitas">
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
            <CategoryBlocks
              data={incomeBreakdown}
              tone="income"
              expandedNames={expandedIncome}
              onToggle={toggleIn(setExpandedIncome)}
              detailsQuery={{ ...monthQuery, type: "Income" }}
            />
          )}
        </section>

        <section className="receitas-despesas__half" aria-label="Despesas">
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
            <CategoryBlocks
              data={expenseBreakdown}
              tone="expense"
              expandedNames={expandedExpense}
              onToggle={toggleIn(setExpandedExpense)}
              detailsQuery={{ ...monthQuery, type: "Expense" }}
              categoryHref={categoryHref}
            />
          )}
        </section>
      </div>

      <section className="rd-result" aria-labelledby="rd-result-label">
        <h2 id="rd-result-label" className="rd-result__label">
          Resultado do período
        </h2>
        {loading ? (
          <div className="rd-result__loading">
            <Skeleton width="10rem" height="2.25rem" />
            <Skeleton width="min(100%, 32rem)" height="1rem" />
          </div>
        ) : (
          <>
            <p className={`rd-result__value rd-result__value--${leftover >= 0 ? "income" : "expense"}`}>
              {leftover < 0 ? `−${formatCurrency(Math.abs(leftover))}` : formatCurrency(leftover)}
            </p>
            <p className="rd-result__text">{closingSentence(totalIncome, totalExpense)}</p>
          </>
        )}
      </section>
    </div>
  );
}
