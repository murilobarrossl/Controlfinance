import { financialTone, transactionTone } from "../../../utils/financialTone.js";
import { useEffect, useMemo, useState } from "react";
import { getTransactionAggregates } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import { isBusinessHolder } from "../../../utils/holder.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import NoBankAccount from "../../../components/dashboard/NoBankAccount/NoBankAccount.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import CategoryBlocks from "./CategoryBlocks.jsx";
import LoanContracts from "./LoanContracts.jsx";
import SummaryMetric from "./SummaryMetric.jsx";
import CashClosingPanel from "./CashClosingPanel.jsx";
import CashAnalysis from "./CashAnalysis.jsx";
import { dayOffsetFrom } from "./cashFormat.js";
import { formatCurrency, roundMoney, formatPercentage } from "../../../utils/financeMath.js";
import { computeTrend } from "../../../utils/trend.js";
import { PERIODS } from "../../../utils/periods.js";
import { breakdownFromAggregates } from "../../../utils/categoryBreakdown.js";
import "./ReceitasDespesas.css";
import usePageTitle from "../../../hooks/usePageTitle.js";

function closingSentence(totalIncome, totalExpense) {
  const leftover = roundMoney(totalIncome - totalExpense);
  if (totalIncome === 0 && totalExpense === 0) return "Nenhuma movimentação neste período.";
  if (leftover >= 0) {
    const pct = totalIncome > 0 ? (leftover / totalIncome) * 100 : 0;
    return `${formatPercentage(pct)} das receitas não foram gastas no período.`;
  }
  return "As despesas superaram as receitas no período.";
}

export default function ReceitasDespesas() {
  const { user } = useAuth();
  const business = isBusinessHolder(user);
  usePageTitle(business ? "Fluxo de Caixa" : "Receitas e despesas");
  const { scopeKey, scopeQuery, dataRefreshKey, accountsStatus } = useAccount();
  const [aggregateResult, setAggregateResult] = useState(null);
  const [offset, setOffset] = useState(0);
  const [expandedIncome, setExpandedIncome] = useState(() => new Set());
  const [expandedExpense, setExpandedExpense] = useState(() => new Set());

  const period = PERIODS[business ? "day" : "month"];
  const current = useMemo(() => period.current(offset), [period, offset]);
  const comparison = useMemo(() => period.previous(offset), [period, offset]);
  const aggregateKey = `${scopeKey}|${dataRefreshKey}|${period.key}|${offset}`;
  const loading = accountsStatus !== "ready" || aggregateResult?.key !== aggregateKey;

  useEffect(() => {
    if (accountsStatus !== "ready" || !scopeKey) return;

    const controller = new AbortController();
    const { signal } = controller;

    Promise.all([
      getTransactionAggregates({ ...scopeQuery, ...current.range }, { signal }),
      getTransactionAggregates({ ...scopeQuery, ...comparison.range }, { signal }),
    ])
      .then(([currentTotals, previousTotals]) => {
        if (signal.aborted) return;
        setAggregateResult({ key: aggregateKey, current: currentTotals, previous: previousTotals, error: "" });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setAggregateResult({
          key: aggregateKey,
          current: null,
          previous: null,
          error: err.message || "Não foi possível carregar as transações.",
        });
      });

    return () => controller.abort();
  }, [scopeKey, scopeQuery, accountsStatus, current, comparison, aggregateKey]);

  const totals = aggregateResult?.key === aggregateKey ? aggregateResult.current : null;
  const previous = aggregateResult?.key === aggregateKey ? aggregateResult.previous : null;
  const error =
    accountsStatus === "error"
      ? "Não foi possível carregar as contas."
      : (aggregateResult?.key === aggregateKey && aggregateResult.error) || "";

  const totalIncome = totals?.totalIncome;
  const totalExpense = totals?.totalExpense;

  const incomeBreakdown = useMemo(() => breakdownFromAggregates(totals?.categories, "Income"), [totals]);
  const expenseBreakdown = useMemo(() => breakdownFromAggregates(totals?.categories, "Expense"), [totals]);

  const incomeTrend = computeTrend(totalIncome, previous?.totalIncome, { label: comparison.trendLabel });
  const expenseTrend = computeTrend(totalExpense, previous?.totalExpense, { invertTone: true, label: comparison.trendLabel });

  function toggleIn(setter) {
    return (name) =>
      setter((prev) => {
        const next = new Set(prev);
        if (next.has(name)) next.delete(name);
        else next.add(name);
        return next;
      });
  }

  const categoryHref = (categoryName) => {
    // Categorias é exclusiva do CPF. No CNPJ, o mesmo link abre as movimentações
    // da categoria nesta página, sem mudar sua aparência nem sair do período.
    if (business) return `#rd-expense-${encodeURIComponent(categoryName)}`;
    const params = new URLSearchParams();
    if (offset === 0) params.set("periodo", "mes");
    params.set("categoria", categoryName);
    return `/dashboard/categorias?${params}`;
  };

  if (error) return <p className="receitas-despesas__error">{error}</p>;
  if (accountsStatus === "ready" && !scopeKey) return <NoBankAccount />;

  const leftover = roundMoney(totalIncome - totalExpense);
  const periodQuery = { ...scopeQuery, ...current.range, refresh: dataRefreshKey };

  return (
    <div className="receitas-despesas">
      <header className="receitas-despesas__header">
        <SectionHeading title={business ? "Fluxo de Caixa" : "Receitas e despesas"} level={1} />
        <div className="receitas-despesas__controls">
          <RangePicker
            label={current.label}
            onPrev={() => setOffset((prev) => prev + 1)}
            onNext={() => setOffset((prev) => Math.max(0, prev - 1))}
            nextDisabled={offset === 0}
          />
        </div>
      </header>

      {business && accountsStatus === "ready" && (
        <CashClosingPanel
          date={current.range.from}
          scopeQuery={scopeQuery}
          scopeKey={scopeKey}
          refreshKey={dataRefreshKey}
          onSelectDay={(day) => setOffset(Math.max(0, dayOffsetFrom(day)))}
        />
      )}

      <div className="receitas-despesas__halves">
        <section className="receitas-despesas__half" aria-label="Receitas">
          {business ? (
            <h2 className="rd-half__title">Receitas por categoria</h2>
          ) : (
            <SummaryMetric
              label="Receitas"
              value={loading ? <Skeleton width="7rem" height="1.5rem" /> : formatCurrency(totalIncome)}
              tone={loading ? "default" : financialTone(totalIncome)}
              trend={loading ? null : incomeTrend}
            />
          )}

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
              detailsQuery={{ ...periodQuery, type: "Income" }}
            />
          )}
        </section>

        <section className="receitas-despesas__half" aria-label="Despesas">
          {business ? (
            <h2 className="rd-half__title">Despesas por categoria</h2>
          ) : (
            <SummaryMetric
              label="Despesas"
              value={loading ? <Skeleton width="7rem" height="1.5rem" /> : formatCurrency(totalExpense)}
              tone={loading ? "default" : transactionTone("Expense", totalExpense)}
              trend={loading ? null : expenseTrend}
            />
          )}

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
              detailsQuery={{ ...periodQuery, type: "Expense" }}
              categoryHref={categoryHref}
              onCategoryOpen={business ? (name) => setExpandedExpense((prev) => new Set(prev).add(name)) : undefined}
            />
          )}
        </section>
      </div>

      {!business && <section className="rd-result" aria-labelledby="rd-result-label">
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
            <p className={`rd-result__value rd-result__value--${financialTone(leftover)}`}>
              {leftover < 0 ? `−${formatCurrency(Math.abs(leftover))}` : formatCurrency(leftover)}
            </p>
            <p className="rd-result__text">{closingSentence(totalIncome, totalExpense)}</p>
          </>
        )}
      </section>}

      {business && accountsStatus === "ready" && <CashAnalysis scopeQuery={scopeQuery} scopeKey={scopeKey} refreshKey={dataRefreshKey} />}

      {!business && <LoanContracts />}
    </div>
  );
}
