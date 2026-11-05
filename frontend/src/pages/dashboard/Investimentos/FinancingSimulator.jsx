import { useMemo, useState } from "react";
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import { TOOLTIP_STYLE, TOOLTIP_ITEM_STYLE } from "../../../components/charts/chartTheme.js";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { buildPriceSchedule, compareTerms } from "../../../utils/investmentMath.js";
import { readCssVar } from "../../../utils/cssVar.js";

const EMPTY_FORM = { totalValue: "", downPayment: "0", monthlyRate: "1.5", installmentsCount: "36" };
const EXAMPLE_FORM = { totalValue: "40000", downPayment: "8000", monthlyRate: "1.5", installmentsCount: "36" };
const COMPARE_TERMS = [12, 24, 36, 48, 60];

// Simulador de financiamento/consórcio pela Tabela Price, calculado enquanto o usuário digita.
// Além da parcela, mostra pra onde o dinheiro vai (juros x dívida abatida) e como o prazo muda o
// custo, que é o que costuma faltar numa proposta de banco: só a parcela aparece em destaque.
export default function FinancingSimulator() {
  const [form, setForm] = useState(EMPTY_FORM);
  const axisColor = useMemo(() => readCssVar("--color-text-tertiary", "#8b8b8b"), []);
  const set = (field) => (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }));

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

    const step = Math.max(1, Math.ceil(schedule.length / 36));
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
  }, [financedAmount, monthlyRate, installmentsCount]);

  return (
    <Card
      title="Simulador de financiamento e consórcio"
      action={
        <Button as="button" type="button" variant="outline" size="sm" onClick={() => setForm(EXAMPLE_FORM)}>
          Preencher com um exemplo
        </Button>
      }
    >
      <p className="investimentos__section-hint">
        Digite os dados da proposta que você recebeu (de banco, loja ou administradora de consórcio) e veja na hora quanto
        você vai pagar de verdade. O resultado atualiza enquanto você digita. É uma estimativa pela Tabela Price, que
        usa parcelas fixas: não inclui IOF, tarifas nem seguros, então o custo efetivo total (CET) que a instituição
        informa pode ser maior.
      </p>

      <div className="investimentos__form">
        <label className="investimentos__field">
          <span className="investimentos__field-label">Valor total do bem</span>
          <CurrencyInput placeholder="Ex: 40000" value={form.totalValue} onChange={set("totalValue")} />
          <span className="investimentos__field-help">Preço do carro, imóvel ou do que vai comprar.</span>
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Entrada</span>
          <CurrencyInput placeholder="Ex: 8000" value={form.downPayment} onChange={set("downPayment")} />
          <span className="investimentos__field-help">O que você paga na hora. Só o restante tem juros.</span>
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Juros ao mês (%)</span>
          <input type="number" placeholder="Ex: 1.5" min="0" step="0.01" value={form.monthlyRate} onChange={set("monthlyRate")} />
          <span className="investimentos__field-help">
            Está na proposta. Se vier ao ano, divida por 12 pra uma aproximação.
          </span>
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Número de parcelas</span>
          <input type="number" placeholder="Ex: 36" min="1" max="480" step="1" value={form.installmentsCount} onChange={set("installmentsCount")} />
        </label>
      </div>

      {downPaymentTooBig && (
        <p className="investimentos__hint investimentos__hint--warn">
          A entrada cobre o valor todo, então não há nada a financiar.
        </p>
      )}

      {!analysis ? (
        !downPaymentTooBig && (
          <p className="investimentos__hint">Informe o valor total e o número de parcelas pra ver a simulação.</p>
        )
      ) : (
        <>
          <div className="investimentos__stats">
            <div className="investimentos__stat">
              <span className="investimentos__stat-label">Valor financiado</span>
              <strong>{formatCurrency(financedAmount)}</strong>
            </div>
            <div className="investimentos__stat investimentos__stat--main">
              <span className="investimentos__stat-label">Parcela mensal</span>
              <strong>{formatCurrency(analysis.payment)}</strong>
            </div>
            <div className="investimentos__stat">
              <span className="investimentos__stat-label">Total pago nas parcelas</span>
              <strong>{formatCurrency(analysis.totalPaid)}</strong>
            </div>
            <div className="investimentos__stat investimentos__stat--bad">
              <span className="investimentos__stat-label">Só de juros</span>
              <strong>{formatCurrency(analysis.totalInterest)}</strong>
            </div>
          </div>

          <p className="investimentos__verdict">
            Pra cada <strong>R$ 100</strong> que você financia, você paga <strong>{formatCurrency(analysis.costPer100)}</strong>{" "}
            no final. {formatPercentage(analysis.interestShare)} de tudo que você paga nas parcelas é juro.
            {downPayment > 0 && <> Contando a entrada, o custo total é de {formatCurrency(analysis.totalPaid + downPayment)}.</>}
          </p>

          <div
            className="investimentos__split"
            role="img"
            aria-label={`Do total pago nas parcelas, ${formatPercentage(100 - analysis.interestShare)} é a dívida e ${formatPercentage(analysis.interestShare)} é juro`}
          >
            <div className="investimentos__split-principal" style={{ width: `${100 - analysis.interestShare}%` }}>
              Dívida
            </div>
            <div className="investimentos__split-interest" style={{ width: `${analysis.interestShare}%` }}>
              {analysis.interestShare >= 8 ? "Juros" : ""}
            </div>
          </div>

          <h4 className="investimentos__subtitle">Pra onde vai cada parcela</h4>
          <div role="img" aria-label="Gráfico da composição de cada parcela entre juros e dívida abatida">
            <ResponsiveContainer width="100%" height={240}>
              <AreaChart data={analysis.chartData} margin={{ top: 8, right: 16, left: 4, bottom: 8 }}>
                <CartesianGrid stroke="#2a2a2a" strokeDasharray="3 3" vertical={false} />
                <XAxis dataKey="month" stroke={axisColor} tick={{ fontSize: 12 }} />
                <YAxis stroke={axisColor} tick={{ fontSize: 12 }} tickFormatter={(v) => formatCurrency(v)} width={76} />
                <Tooltip
                  formatter={(value) => formatCurrency(value)}
                  contentStyle={TOOLTIP_STYLE}
                  labelStyle={{ color: "#b8b8b8" }}
                  itemStyle={TOOLTIP_ITEM_STYLE}
                  cursor={{ stroke: "#3a3a3a", strokeWidth: 1 }}
                />
                <Legend wrapperStyle={{ fontSize: 12, color: "#b8b8b8" }} />
                <Area type="monotone" stackId="1" dataKey="amortization" name="Abate a dívida" stroke="#4ECDC4" fill="#4ECDC4" fillOpacity={0.45} />
                <Area type="monotone" stackId="1" dataKey="interest" name="Juros" stroke="#ED4A31" fill="#ED4A31" fillOpacity={0.55} />
              </AreaChart>
            </ResponsiveContainer>
          </div>

          <p className="investimentos__chart-takeaway">
            <strong>O que olhar:</strong> na 1ª parcela, {formatCurrency(analysis.firstInterest)} (
            {formatPercentage((analysis.firstInterest / analysis.payment) * 100)}) vão só pra juros. A parte vermelha vai
            encolhendo e a verde crescendo conforme você paga, porque os juros são cobrados sobre o que ainda falta pagar.
          </p>

          <h4 className="investimentos__subtitle">E se o prazo fosse outro?</h4>
          <p className="investimentos__section-hint investimentos__section-hint--tight">
            Prazo maior deixa a parcela menor, mas faz você pagar bem mais juros. Compare com os mesmos valores:
          </p>
          <div className="investimentos__table-wrap">
            <table className="investimentos__table">
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
                    <td>
                      {term.count} meses
                      {term.count === installmentsCount && <span className="investimentos__badge">Sua simulação</span>}
                    </td>
                    <td className="investimentos__num">{formatCurrency(term.payment)}</td>
                    <td className="investimentos__num">{formatCurrency(term.totalInterest)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </Card>
  );
}
