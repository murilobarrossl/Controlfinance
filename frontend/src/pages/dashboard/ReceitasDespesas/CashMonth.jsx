import { useEffect, useMemo, useState } from "react";
import { Link, Navigate, useNavigate, useParams } from "react-router";
import { Bar, CartesianGrid, ComposedChart, Line, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { getTransactionAggregates, getTransactionsReport } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import NoBankAccount from "../../../components/dashboard/NoBankAccount/NoBankAccount.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import Pager from "../../../components/ui/Pager/Pager.jsx";
import { CHART_COLORS, TOOLTIP_ITEM_STYLE, TOOLTIP_STYLE } from "../../../components/charts/chartTheme.js";
import { financialTone, transactionTone } from "../../../utils/financialTone.js";
import { formatCurrency, formatDate, roundMoney } from "../../../utils/financeMath.js";
import { formatMonthLong } from "../../../utils/monthLabel.js";
import { monthRange } from "../../../utils/monthlyTrend.js";
import { compareMonth } from "../../../utils/periods.js";
import { computeTrend } from "../../../utils/trend.js";
import usePageTitle from "../../../hooks/usePageTitle.js";
import SummaryMetric from "./SummaryMetric.jsx";
import { currentMonthStart, formatResult, monthParam, parseMonthParam } from "./cashFormat.js";
import "./ReceitasDespesas.css";

const PAGE_SIZE = 25;

const compactCurrency = (value) =>
  value.toLocaleString("pt-BR", { style: "currency", currency: "BRL", notation: "compact", maximumFractionDigits: 1 });

function shiftMonth(date, delta) {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + delta, 1));
}

function splitName(name) {
  const pipe = name.indexOf("|");
  return pipe === -1 ? { primary: name, secondary: null } : { primary: name.slice(0, pipe).trim(), secondary: name.slice(pipe + 1).trim() };
}

function MonthMovements({ month, scopeQuery, scopeKey, refreshKey }) {
  const [page, setPage] = useState({ key: "", value: 1 });
  const [result, setResult] = useState(null);
  const baseKey = `${scopeKey}|${refreshKey}|${monthParam(month)}`;
  const currentPage = page.key === baseKey ? page.value : 1;
  const key = `${baseKey}|${currentPage}`;

  useEffect(() => {
    const controller = new AbortController();
    getTransactionsReport(
      {
        ...scopeQuery,
        year: month.getUTCFullYear(),
        month: month.getUTCMonth() + 1,
        sortBy: "dueDate",
        sortDir: "desc",
        page: currentPage,
        pageSize: PAGE_SIZE,
        excludeTransfers: true,
      },
      { signal: controller.signal }
    )
      .then((data) => setResult({ key, data, error: "" }))
      .catch((err) => {
        if (isAbortError(err) || controller.signal.aborted) return;
        setResult({ key, data: null, error: err.message || "Não foi possível carregar as movimentações." });
      });
    return () => controller.abort();
  }, [key, month, scopeQuery, currentPage]);

  const ready = result?.key === key ? result : null;
  const data = ready?.data;
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE)) : null;

  return (
    <section className="cash-month__section" aria-labelledby="cash-month-movements">
      <h2 id="cash-month-movements" className="cash-analysis__title">Movimentações</h2>
      {ready?.error ? (
        <p className="receitas-despesas__error">{ready.error}</p>
      ) : !data ? (
        <Skeleton width="100%" height="14rem" />
      ) : data.items.length === 0 ? (
        <p className="receitas-despesas__hint">Nenhuma movimentação neste mês.</p>
      ) : (
        <ul className="rd-tx cash-month__list">
          {data.items.map((t) => {
            const { primary, secondary } = splitName(t.name);
            const expense = t.type === "Expense";
            return (
              <li key={t.id} className="rd-tx__item">
                <span className="rd-tx__desc">
                  <span className="rd-tx__name">{primary}</span>
                  <span className="rd-tx__secondary">{[secondary, t.categoryName].filter(Boolean).join(" · ")}</span>
                </span>
                <span className="rd-tx__date">{formatDate(t.dueDate)}</span>
                <span className={`rd-tx__amount rd-tx__amount--${transactionTone(t.type, t.amount, t.isTransfer)}`}>
                  {`${expense ? "−" : "+"}${formatCurrency(Math.abs(t.amount))}`}
                </span>
              </li>
            );
          })}
        </ul>
      )}
      <Pager
        page={currentPage}
        totalPages={totalPages}
        totalCount={data?.totalCount}
        countLabel="movimentações"
        loading={!data}
        label="Páginas de movimentações"
        onChange={(value) => setPage({ key: baseKey, value })}
      />
    </section>
  );
}

export default function CashMonth() {
  const { month: monthText } = useParams();
  const navigate = useNavigate();
  const month = useMemo(() => parseMonthParam(monthText), [monthText]);
  const lastMonth = currentMonthStart();
  const label = month ? formatMonthLong(month) : "";
  usePageTitle(label ? `Fluxo de Caixa · ${label}` : "Fluxo de Caixa");
  const { scopeKey, scopeQuery, dataRefreshKey, accountsStatus } = useAccount();
  const [result, setResult] = useState(null);

  const valid = month && month <= lastMonth;
  const key = valid ? `${scopeKey}|${dataRefreshKey}|${monthParam(month)}` : "";

  useEffect(() => {
    if (!valid || accountsStatus !== "ready" || !scopeKey) return;
    const controller = new AbortController();
    const { signal } = controller;
    const comparison = compareMonth(month);
    Promise.all([
      getTransactionAggregates({ ...scopeQuery, ...monthRange(month) }, { signal }),
      getTransactionAggregates({ ...scopeQuery, ...comparison.range }, { signal }),
      comparison.currentRange ? getTransactionAggregates({ ...scopeQuery, ...comparison.currentRange }, { signal }) : null,
    ])
      .then(([now, before, compared]) => setResult({ key, now, before, compared: compared ?? now, error: "" }))
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ key, now: null, before: null, error: err.message || "Não foi possível carregar o mês." });
      });
    return () => controller.abort();
  }, [valid, key, month, scopeKey, scopeQuery, accountsStatus]);

  if (!valid) return <Navigate to="/dashboard/fluxo-de-caixa" replace />;
  if (accountsStatus === "error") return <p className="receitas-despesas__error">Não foi possível carregar as contas.</p>;
  if (accountsStatus === "ready" && !scopeKey) return <NoBankAccount />;

  const ready = result?.key === key ? result : null;
  const now = ready?.now;
  const before = ready?.before;
  const compared = ready?.compared;
  const loading = !now;
  const { trendLabel, previousName } = compareMonth(month);
  const net = now ? roundMoney(now.totalIncome - now.totalExpense) : null;
  const comparedNet = compared ? roundMoney(compared.totalIncome - compared.totalExpense) : null;
  const previousNet = before ? roundMoney(before.totalIncome - before.totalExpense) : null;

  let running = 0;
  const daily = (now?.days ?? []).map((d) => {
    running = roundMoney(running + d.income - d.expense);
    return { day: String(Number(d.date.slice(8, 10))), date: d.date, Receitas: d.income, Despesas: d.expense, Acumulado: running };
  });

  const goTo = (delta) => navigate(`/dashboard/fluxo-de-caixa/mes/${monthParam(shiftMonth(month, delta))}`);

  return (
    <div className="receitas-despesas cash-month">
      <Link to="/dashboard/fluxo-de-caixa" className="cash-month__back">‹ Fluxo de caixa</Link>

      <header className="receitas-despesas__header">
        <SectionHeading title={label} level={1} />
        <RangePicker label={label} onPrev={() => goTo(-1)} onNext={() => goTo(1)} nextDisabled={month >= lastMonth} />
      </header>

      {ready?.error ? (
        <p className="receitas-despesas__error">{ready.error}</p>
      ) : (
        <>
          <div className="cash-month__metrics">
            <SummaryMetric
              label="Receitas"
              value={loading ? <Skeleton width="7rem" height="1.5rem" /> : formatCurrency(now.totalIncome)}
              tone={loading ? "default" : financialTone(now.totalIncome)}
              trend={loading ? null : computeTrend(compared.totalIncome, before.totalIncome, { label: trendLabel })}
            />
            <SummaryMetric
              label="Despesas"
              value={loading ? <Skeleton width="7rem" height="1.5rem" /> : formatCurrency(now.totalExpense)}
              tone={loading ? "default" : transactionTone("Expense", now.totalExpense)}
              trend={loading ? null : computeTrend(compared.totalExpense, before.totalExpense, { invertTone: true, label: trendLabel })}
            />
            <SummaryMetric
              label="Resultado"
              value={loading ? <Skeleton width="7rem" height="1.5rem" /> : formatResult(net)}
              tone={loading ? "default" : financialTone(net)}
              trend={loading ? null : computeTrend(comparedNet, previousNet, { label: trendLabel })}
            />
          </div>

          {!loading && (
            <dl className="cash-month__previous">
              <dt>{previousName}</dt>
              <dd>Receitas {formatCurrency(before.totalIncome)}</dd>
              <dd>Despesas {formatCurrency(before.totalExpense)}</dd>
              <dd>Resultado {formatResult(previousNet)}</dd>
            </dl>
          )}

          <section className="cash-month__section" aria-labelledby="cash-month-evolution">
            <h2 id="cash-month-evolution" className="cash-analysis__title">Evolução no mês</h2>
            <div className="cash-analysis__legend" aria-hidden="true">
              <span className="cash-analysis__key cash-analysis__key--income">Receitas</span>
              <span className="cash-analysis__key cash-analysis__key--expense">Despesas</span>
              <span className="cash-analysis__key cash-analysis__key--line">Resultado acumulado</span>
            </div>
            <div className="cash-analysis__chart" aria-hidden="true">
              {loading ? (
                <Skeleton width="100%" height="16rem" />
              ) : (
                <ResponsiveContainer width="100%" height={260}>
                  <ComposedChart data={daily} margin={{ top: 8, right: 8, left: 4, bottom: 0 }}>
                    <CartesianGrid stroke={CHART_COLORS.grid} strokeDasharray="3 3" vertical={false} />
                    <XAxis dataKey="day" stroke={CHART_COLORS.axis} tick={{ fontSize: 12 }} tickLine={false} interval="preserveStartEnd" minTickGap={8} />
                    <YAxis stroke={CHART_COLORS.axis} tick={{ fontSize: 12 }} tickFormatter={compactCurrency} width={76} />
                    <ReferenceLine y={0} stroke={CHART_COLORS.cursor} />
                    <Tooltip
                      formatter={(value) => formatResult(value)}
                      labelFormatter={(_, payload) => (payload?.[0] ? formatDate(payload[0].payload.date) : "")}
                      contentStyle={TOOLTIP_STYLE}
                      labelStyle={{ color: CHART_COLORS.label }}
                      itemStyle={TOOLTIP_ITEM_STYLE}
                      cursor={{ fill: "var(--color-surface-hover)" }}
                    />
                    <Bar dataKey="Receitas" fill={CHART_COLORS.income} radius={[2, 2, 0, 0]} maxBarSize={14} />
                    <Bar dataKey="Despesas" fill={CHART_COLORS.expense} radius={[2, 2, 0, 0]} maxBarSize={14} />
                    <Line dataKey="Acumulado" name="Resultado acumulado" stroke={CHART_COLORS.label} strokeWidth={2} dot={false} type="monotone" />
                  </ComposedChart>
                </ResponsiveContainer>
              )}
            </div>
            {!loading && (
              <table className="visually-hidden">
                <caption>{`Movimentação diária de ${label}`}</caption>
                <thead>
                  <tr><th scope="col">Dia</th><th scope="col">Receitas</th><th scope="col">Despesas</th><th scope="col">Resultado acumulado</th></tr>
                </thead>
                <tbody>
                  {daily.filter((d) => d.Receitas || d.Despesas).map((d) => (
                    <tr key={d.date}>
                      <th scope="row">{formatDate(d.date)}</th>
                      <td>{formatCurrency(d.Receitas)}</td>
                      <td>{formatCurrency(d.Despesas)}</td>
                      <td>{formatResult(d.Acumulado)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          <MonthMovements month={month} scopeQuery={scopeQuery} scopeKey={scopeKey} refreshKey={dataRefreshKey} />
        </>
      )}
    </div>
  );
}
