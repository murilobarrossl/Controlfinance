import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router";
import { Bar, BarChart, CartesianGrid, Cell, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { getTransactionAggregates } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { CHART_COLORS, TOOLTIP_ITEM_STYLE, TOOLTIP_STYLE } from "../../../components/charts/chartTheme.js";
import { formatCurrency, roundMoney } from "../../../utils/financeMath.js";
import { formatMonthLong } from "../../../utils/monthLabel.js";
import { getMonthsWindow } from "../../../utils/monthlyTrend.js";
import { PERIODS } from "../../../utils/periods.js";
import { currentMonthStart, formatResult, monthParam } from "./cashFormat.js";

const GRANULARITIES = [
  { key: "month", label: "Meses" },
  { key: "year", label: "Anos" },
];

const compactCurrency = (value) =>
  value.toLocaleString("pt-BR", { style: "currency", currency: "BRL", notation: "compact", maximumFractionDigits: 1 });

function previousLabel(granularity, offset) {
  if (granularity === "month") return formatMonthLong(getMonthsWindow(1, offset + 1)[0]);
  return String(new Date().getFullYear() - offset - 1);
}

function variation(current, previous) {
  if (typeof current !== "number" || typeof previous !== "number" || previous === 0) return "—";
  const change = ((current - previous) / Math.abs(previous)) * 100;
  return `${change >= 0 ? "+" : "−"}${Math.abs(change).toLocaleString("pt-BR", { maximumFractionDigits: 1 })}%`;
}

function metricTone(key, value) {
  if (key === "income") return CHART_COLORS.income;
  if (key === "expense") return CHART_COLORS.expense;
  return value < 0 ? CHART_COLORS.expense : CHART_COLORS.income;
}

export default function CashAnalysis({ scopeQuery, scopeKey, refreshKey }) {
  const navigate = useNavigate();
  const [granularity, setGranularity] = useState("month");
  const [offset, setOffset] = useState(0);
  const [result, setResult] = useState(null);

  const period = PERIODS[granularity];
  const current = useMemo(() => period.current(offset), [period, offset]);
  const comparison = useMemo(() => period.previous(offset), [period, offset]);
  const key = `${scopeKey}|${refreshKey}|${granularity}|${offset}`;

  useEffect(() => {
    if (!scopeKey) return;
    const controller = new AbortController();
    const { signal } = controller;
    Promise.all([
      getTransactionAggregates({ ...scopeQuery, ...current.range }, { signal }),
      getTransactionAggregates({ ...scopeQuery, ...comparison.range }, { signal }),
    ])
      .then(([now, before]) => setResult({ key, now, before, error: "" }))
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ key, now: null, before: null, error: err.message || "Não foi possível carregar a análise." });
      });
    return () => controller.abort();
  }, [key, scopeKey, scopeQuery, current, comparison]);

  const ready = result?.key === key ? result : null;
  const loading = !ready;
  const now = ready?.now;
  const before = ready?.before;
  const currentName = current.label;
  const previousName = previousLabel(granularity, offset);

  const rows = now && before ? [
    { key: "income", label: "Receitas", current: now.totalIncome, previous: before.totalIncome },
    { key: "expense", label: "Despesas", current: now.totalExpense, previous: before.totalExpense },
    {
      key: "result",
      label: "Resultado",
      current: roundMoney(now.totalIncome - now.totalExpense),
      previous: roundMoney(before.totalIncome - before.totalExpense),
    },
  ] : [];

  const monthDate = granularity === "month" ? getMonthsWindow(1, offset)[0] : null;
  const monthHref = monthDate ? `/dashboard/fluxo-de-caixa/mes/${monthParam(monthDate)}` : null;
  const lastMonth = currentMonthStart();
  const yearMonths = granularity === "year" && now
    ? now.months
        .map((m) => ({ ...m, date: new Date(Date.UTC(m.year, m.month - 1, 1)) }))
        .filter((m) => m.date <= lastMonth)
    : [];

  return (
    <section className="cash-analysis" aria-labelledby="cash-analysis-title">
      <header className="cash-analysis__header">
        <h2 id="cash-analysis-title" className="cash-analysis__title">Análise</h2>
        <div className="receitas-despesas__controls">
          <div className="rd-granularity" role="group" aria-label="Período da análise">
            {GRANULARITIES.map((option) => (
              <button key={option.key} type="button" aria-pressed={granularity === option.key}
                onClick={() => { setGranularity(option.key); setOffset(0); }}>
                {option.label}
              </button>
            ))}
          </div>
          <RangePicker
            label={currentName}
            onPrev={() => setOffset((prev) => prev + 1)}
            onNext={() => setOffset((prev) => Math.max(0, prev - 1))}
            nextDisabled={offset === 0}
          />
        </div>
      </header>

      {ready?.error ? (
        <p className="receitas-despesas__error">{ready.error}</p>
      ) : (
        <>
          <div className="cash-analysis__legend" aria-hidden="true">
            <span className="cash-analysis__key cash-analysis__key--current">{currentName}</span>
            <span className="cash-analysis__key cash-analysis__key--previous">{previousName}</span>
          </div>

          <div className="cash-analysis__chart" aria-hidden="true">
            {loading ? (
              <Skeleton width="100%" height="15rem" />
            ) : (
              <ResponsiveContainer width="100%" height={240}>
                <BarChart
                  data={rows}
                  margin={{ top: 8, right: 8, left: 4, bottom: 0 }}
                  barGap={6}
                  onClick={monthHref ? () => navigate(monthHref) : undefined}
                  style={monthHref ? { cursor: "pointer" } : undefined}
                >
                  <CartesianGrid stroke={CHART_COLORS.grid} strokeDasharray="3 3" vertical={false} />
                  <XAxis dataKey="label" stroke={CHART_COLORS.axis} tick={{ fontSize: 12 }} tickLine={false} />
                  <YAxis stroke={CHART_COLORS.axis} tick={{ fontSize: 12 }} tickFormatter={compactCurrency} width={76} />
                  <ReferenceLine y={0} stroke={CHART_COLORS.cursor} />
                  <Tooltip
                    formatter={(value) => formatResult(value)}
                    contentStyle={TOOLTIP_STYLE}
                    labelStyle={{ color: CHART_COLORS.label }}
                    itemStyle={TOOLTIP_ITEM_STYLE}
                    cursor={{ fill: "var(--color-surface-hover)" }}
                  />
                  <Bar dataKey="previous" name={previousName} fill={CHART_COLORS.neutral} fillOpacity={0.45} radius={[3, 3, 0, 0]} maxBarSize={44} />
                  <Bar dataKey="current" name={currentName} radius={[3, 3, 0, 0]} maxBarSize={44}>
                    {rows.map((row) => <Cell key={row.key} fill={metricTone(row.key, row.current)} />)}
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
            )}
          </div>

          <div className="cash-analysis__table-wrap">
            <table className="cash-analysis__table">
              <caption className="visually-hidden">{`${currentName} comparado com ${previousName}`}</caption>
              <thead>
                <tr>
                  <th scope="col"><span className="visually-hidden">Indicador</span></th>
                  <th scope="col">{currentName}</th>
                  <th scope="col">{previousName}</th>
                  <th scope="col">Variação</th>
                </tr>
              </thead>
              <tbody>
                {loading
                  ? Array.from({ length: 3 }, (_, i) => (
                      <tr key={i}>
                        <td colSpan={4}><Skeleton width="100%" height="1rem" /></td>
                      </tr>
                    ))
                  : rows.map((row) => (
                      <tr key={row.key}>
                        <th scope="row">{row.label}</th>
                        <td className={`cash-analysis__value cash-analysis__value--${row.key === "result" ? (row.current < 0 ? "expense" : "income") : row.key}`}>
                          {formatResult(row.current)}
                        </td>
                        <td>{formatResult(row.previous)}</td>
                        <td>{variation(row.current, row.previous)}</td>
                      </tr>
                    ))}
              </tbody>
            </table>
          </div>

          {monthHref && (
            <Link to={monthHref} className="cash-analysis__more">
              Detalhes de {currentName} ›
            </Link>
          )}

          {yearMonths.length > 0 && (
            <ul className="cash-months" aria-label={`Meses de ${currentName}`}>
              <li className="cash-months__head" aria-hidden="true">
                <span>Mês</span>
                <span>Receitas</span>
                <span>Despesas</span>
                <span>Resultado</span>
                <span />
              </li>
              {yearMonths.map((m) => {
                const net = roundMoney(m.income - m.expense);
                return (
                  <li key={m.month}>
                    <Link to={`/dashboard/fluxo-de-caixa/mes/${monthParam(m.date)}`} className="cash-months__row">
                      <span className="cash-months__name">{formatMonthLong(m.date)}</span>
                      <span className="cash-months__num cash-months__num--income"><span className="cash-months__label">Receitas </span>{formatCurrency(m.income)}</span>
                      <span className="cash-months__num cash-months__num--expense"><span className="cash-months__label">Despesas </span>{formatCurrency(m.expense)}</span>
                      <span className={`cash-months__num cash-months__num--${net < 0 ? "expense" : "income"}`}><span className="cash-months__label">Resultado </span>{formatResult(net)}</span>
                      <span className="cash-months__chevron" aria-hidden="true">›</span>
                    </Link>
                  </li>
                );
              })}
            </ul>
          )}
        </>
      )}
    </section>
  );
}
