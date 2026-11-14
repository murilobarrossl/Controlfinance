import { useMemo, useState } from "react";
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import { TOOLTIP_STYLE, TOOLTIP_ITEM_STYLE } from "../../../components/charts/chartTheme.js";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { buildPriceSchedule, compareTerms } from "../../../utils/investmentMath.js";
import { readCssVar } from "../../../utils/cssVar.js";
import useIsNarrow from "./useIsNarrow.js";

function formatAxisCurrency(value) {
  if (Math.abs(value) < 1000) return `R$ ${Math.round(value)}`;
  return `R$ ${(value / 1000).toLocaleString("pt-BR", { maximumFractionDigits: 1 })} mil`;
}

const EMPTY_FORM = { totalValue: "", downPayment: "0", monthlyRate: "1.5", installmentsCount: "36" };
const COMPARE_TERMS = [12, 24, 36, 48, 60];

// Simulador de financiamento/consórcio pela Tabela Price, calculado enquanto o usuário digita.
// Além da parcela, mostra pra onde o dinheiro vai (juros x dívida abatida) e como o prazo muda o
// custo, que é o que costuma faltar numa proposta de banco: só a parcela aparece em destaque.
export default function FinancingSimulator() {
  const [form, setForm] = useState(EMPTY_FORM);
  const colors = useMemo(
    () => ({
      axis: readCssVar("--color-text-tertiary", "#8b8b8b"),
      grid: readCssVar("--color-border", "#2a2a2a"),
      cursor: readCssVar("--color-border-strong", "#3a3a3a"),
      label: readCssVar("--color-text-secondary", "#b8b8b8"),
      principal: readCssVar("--color-income", "#4ecdc4"),
      interest: readCssVar("--color-accent", "#ed4a31"),
    }),
    [],
  );
  const set = (field) => (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }));
  const [chartRef, narrowChart] = useIsNarrow();

  const totalValue = Math.max(0, Number(form.totalValue) || 0);
  const downPayment = Math.max(0, Number(form.downPayment) || 0);
  const monthlyRate = Math.max(0, (Number(form.monthlyRate) || 0) / 100);
  const installmentsCount = Math.min(480, Math.max(0, Math.round(Number(form.installmentsCount) || 0)));
  const financedAmount = Math.max(totalValue - downPayment, 0);
  const downPaymentTooBig = totalValue > 0 && downPayment >= totalValue;

  const analysis = useMemo(() => {
    const schedule = buildPriceSchedule(financedAmount, monthlyRate, installmentsCount);
    if (schedule.length === 0) return null;

    const payment = schedule[0].payment;
    const totalPaid = payment * installmentsCount;
    const totalInterest = totalPaid - financedAmount;

    // Prazos de comparação + o prazo digitado, sem repetir, em ordem.
    const terms = [...new Set([...COMPARE_TERMS, installmentsCount])].sort((a, b) => a - b);

    const step = Math.max(1, Math.ceil(schedule.length / (narrowChart ? 8 : 18)));
    const chartData = schedule
      .filter((row) => row.month % step === 0 || row.month === 1 || row.month === schedule.length)
      .map((row) => ({
        month: `${row.month}ª`,
        interest: Math.round(row.interest * 100) / 100,
        amortization: Math.round(row.amortization * 100) / 100,
      }));

    return {
      payment,
      totalPaid,
      totalInterest,
      firstInterest: schedule[0].interest,
      interestShare: totalPaid > 0 ? (totalInterest / totalPaid) * 100 : 0,
      costPer100: financedAmount > 0 ? (totalPaid / financedAmount) * 100 : 0,
      terms: compareTerms(financedAmount, monthlyRate, terms),
      chartData,
    };
  }, [financedAmount, monthlyRate, installmentsCount, narrowChart]);

  return (
    <section className="investimentos__section" aria-labelledby="investimentos-financing-title">
      <div className="investimentos__section-head">
        <h2 id="investimentos-financing-title" className="investimentos__group-title">
          Simulador de financiamento
        </h2>
        <p className="investimentos__lead">Financiamento ou consórcio pela Tabela Price, com parcelas fixas.</p>
      </div>

      <div className="fin-sim">

        <div className="fin-sim__layout">
          <div className="investimentos__form fin-sim__inputs">
            <label className="investimentos__field">
              <span className="investimentos__field-label">Valor total do bem</span>
              <CurrencyInput placeholder="Ex: 40.000,00" value={form.totalValue} onChange={set("totalValue")} />
            </label>
            <label className="investimentos__field">
              <span className="investimentos__field-label">Entrada</span>
              <CurrencyInput placeholder="Ex: 8.000,00" value={form.downPayment} onChange={set("downPayment")} />
            </label>
            <label className="investimentos__field">
              <span className="investimentos__field-label">Juros ao mês (%)</span>
              <input type="number" inputMode="decimal" placeholder="Ex: 1,5" min="0" step="0.01" value={form.monthlyRate} onChange={set("monthlyRate")} />
              <span className="investimentos__field-help">Use a taxa mensal da proposta. Taxa anual ÷ 12 dá só uma aproximação.</span>
            </label>
            <label className="investimentos__field">
              <span className="investimentos__field-label">Número de parcelas</span>
              <input type="number" inputMode="numeric" placeholder="Ex: 36" min="1" max="480" step="1" value={form.installmentsCount} onChange={set("installmentsCount")} />
            </label>
          </div>

          <div className="fin-sim__results">
            {downPaymentTooBig && (
              <p className="investimentos__hint investimentos__hint--warn">
                A entrada cobre o valor todo, então não há nada a financiar.
              </p>
            )}

            {!analysis ? (
              !downPaymentTooBig && (
                <p className="investimentos__hint">Informe o valor total e o número de parcelas para ver a simulação.</p>
              )
            ) : (
              <>
                <dl className="fin-sim__stats">
                  <div className="fin-sim__stat fin-sim__stat--main">
                    <dt>Parcela mensal</dt>
                    <dd>{formatCurrency(analysis.payment)}</dd>
                    <span className="fin-sim__note">{installmentsCount} parcelas</span>
                  </div>
                  <div className="fin-sim__stat">
                    <dt>Valor financiado</dt>
                    <dd>{formatCurrency(financedAmount)}</dd>
                  </div>
                  <div className="fin-sim__stat">
                    <dt>Total pago nas parcelas</dt>
                    <dd>{formatCurrency(analysis.totalPaid)}</dd>
                  </div>
                  <div className="fin-sim__stat fin-sim__stat--bad">
                    <dt>Só de juros</dt>
                    <dd>{formatCurrency(analysis.totalInterest)}</dd>
                    <span className="fin-sim__note">{formatPercentage(analysis.interestShare)} do que você paga</span>
                  </div>
                </dl>

                <p className="investimentos__verdict">
                  Para cada <strong>R$ 100</strong> financiados, você paga <strong>{formatCurrency(analysis.costPer100)}</strong>.
                  {downPayment > 0 && <> Com a entrada, o custo total é de {formatCurrency(analysis.totalPaid + downPayment)}.</>}
                </p>
              </>
            )}
          </div>
        </div>

        {analysis && (
          <>
            <div className="fin-sim__block">
              <h3 className="inv-plan__title">Composição de cada parcela</h3>
              <div ref={chartRef} role="img" aria-label="Gráfico da composição de cada parcela entre juros e dívida abatida">
                <ResponsiveContainer width="100%" height={narrowChart ? 220 : 240}>
                  <AreaChart data={analysis.chartData} margin={{ top: 8, right: 16, left: 4, bottom: 8 }}>
                    <CartesianGrid stroke={colors.grid} strokeDasharray="3 3" vertical={false} />
                    <XAxis dataKey="month" stroke={colors.axis} tick={{ fontSize: 12 }} />
                    <YAxis
                      stroke={colors.axis}
                      tick={{ fontSize: 12 }}
                      tickFormatter={(v) => (narrowChart ? formatAxisCurrency(v) : formatCurrency(v))}
                      width={narrowChart ? 62 : 76}
                    />
                    <Tooltip
                      formatter={(value) => formatCurrency(value)}
                      contentStyle={TOOLTIP_STYLE}
                      labelStyle={{ color: colors.label }}
                      itemStyle={TOOLTIP_ITEM_STYLE}
                      cursor={{ stroke: colors.cursor, strokeWidth: 1 }}
                    />
                    <Legend wrapperStyle={{ fontSize: 12, color: colors.label }} />
                    <Area type="monotone" stackId="1" dataKey="amortization" name="Abate a dívida" stroke={colors.principal} fill={colors.principal} fillOpacity={0.45} />
                    <Area type="monotone" stackId="1" dataKey="interest" name="Juros" stroke={colors.interest} fill={colors.interest} fillOpacity={0.55} />
                  </AreaChart>
                </ResponsiveContainer>
              </div>
              <p className="investimentos__chart-takeaway">
                Na 1ª parcela, <strong>{formatCurrency(analysis.firstInterest)}</strong> (
                {formatPercentage((analysis.firstInterest / analysis.payment) * 100)}) são juros.
              </p>
            </div>

            <div className="fin-sim__block">
              <h3 className="inv-plan__title">Mesmos valores em outros prazos</h3>
              <div className="investimentos__table-wrap">
                <table className="investimentos__table investimentos__table--wide">
                  <thead>
                    <tr>
                      <th>Prazo</th>
                      <th className="investimentos__num">Parcela</th>
                      <th className="investimentos__num">Total de juros</th>
                    </tr>
                  </thead>
                  <tbody>
                    {analysis.terms.map((term) => (
                      <tr key={term.count} className={term.count === installmentsCount ? "investimentos__row--current" : undefined}>
                        <td className="investimentos__term">
                          {term.count} meses
                          {term.count === installmentsCount && <span className="investimentos__badge">Sua simulação</span>}
                        </td>
                        <td className="investimentos__num" data-label="Parcela">
                          {formatCurrency(term.payment)}
                        </td>
                        <td className="investimentos__num" data-label="Total de juros">
                          {formatCurrency(term.totalInterest)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </>
        )}

        <p className="investimentos__fineprint">
          Tabela Price, com parcelas fixas. Não inclui IOF, tarifas nem seguros: o custo efetivo total (CET) da instituição
          pode ser maior.
        </p>
      </div>
    </section>
  );
}
