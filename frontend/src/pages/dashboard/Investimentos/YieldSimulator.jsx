import { useMemo, useState } from "react";
import Card from "../../../components/ui/Card/Card.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import AreaTrendChart from "../../../components/charts/AreaTrendChart.jsx";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { readCssVar } from "../../../utils/cssVar.js";
import { poupancaAnnualPct, simulateGrowth } from "../../../utils/investmentMath.js";
import useIsNarrow from "./useIsNarrow.js";
import { poupancaRuleLabel } from "./poupancaRule.js";
import { REFERENCE_SELIC_PCT, flattenProducts } from "../../../utils/investmentProducts.js";

const PRODUCTS = flattenProducts();
const CUSTOM = "custom";
const POUPANCA = "poupanca";

const CHART_X_PADDING = { left: 28, right: 12 };

function buildSeries({ chosenLabel, poupancaLabel }) {
  return [
    { key: "invested", label: "O que você colocou", color: readCssVar("--color-text-tertiary", "#8b8b8b") },
    { key: "chosen", label: chosenLabel, color: readCssVar("--color-income", "#4ecdc4") },
    { key: "poupanca", label: poupancaLabel, color: readCssVar("--color-accent", "#ed4a31") },
  ];
}

function periodLabel(month) {
  if (month === 0) return "Hoje";
  const years = Math.floor(month / 12);
  const months = month % 12;
  const yearsText = years > 0 ? `${years} ${years === 1 ? "ano" : "anos"}` : "";
  const monthsText = months > 0 ? `${months} ${months === 1 ? "mês" : "meses"}` : "";
  return [yearsText, monthsText].filter(Boolean).join(" e ");
}

function monthLabel(month) {
  if (month === 0) return "Hoje";
  if (month % 12 === 0) return `${month / 12} ${month === 12 ? "ano" : "anos"}`;
  return `${month}m`;
}

// Quanto um valor inicial mais aportes mensais viram no tempo. Mostra o resultado já com imposto de
// renda, compara com a poupança e explica o cálculo em linguagem simples, pra quem nunca viu
// "juros compostos" entender de onde o número saiu.
export default function YieldSimulator({ cdi, onCdiChange, isCompany }) {
  const [form, setForm] = useState({
    initial: "",
    monthly: "",
    years: "",
    product: CUSTOM,
    customPct: "",
  });

  const [chartRef, narrowChart] = useIsNarrow();
  const set = (field) => (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }));

  const initial = Math.max(0, Number(form.initial) || 0);
  const monthly = Math.max(0, Number(form.monthly) || 0);
  const years = Math.min(40, Math.max(1, Number(form.years) || 1));
  const months = Math.round(years * 12);
  const cdiPct = Math.max(0, Number(cdi) || 0);

  const choice = useMemo(() => {
    if (form.product === POUPANCA) {
      return { label: "Poupança", annualPct: poupancaAnnualPct(REFERENCE_SELIC_PCT), taxed: false, pctCdi: null };
    }
    if (form.product === CUSTOM) {
      const pct = Math.max(0, Number(form.customPct) || 0);
      return { label: `CDB a ${pct}% do CDI`, annualPct: (cdiPct * pct) / 100, taxed: true, pctCdi: pct };
    }
    const product = PRODUCTS.find((p) => p.id === form.product) ?? PRODUCTS[0];
    return {
      label: `${product.bank} · ${product.name}`,
      annualPct: (cdiPct * product.pctCdi) / 100,
      taxed: true,
      pctCdi: product.pctCdi,
    };
  }, [form.product, form.customPct, cdiPct]);

  // Para CNPJ a poupança é tributada e a conta não desconta esse imposto: o que sai sem IR descontado
  // não pode ser chamado de líquido.
  const poupancaBeforeTax = isCompany;
  const chosenBeforeTax = !choice.taxed && isCompany;
  const chosenLabel = chosenBeforeTax ? "Aplicação escolhida (antes do IR)" : "Aplicação escolhida (líquido)";
  const poupancaLabel = poupancaBeforeTax ? "Poupança (antes do IR)" : "Poupança";
  const series = useMemo(() => buildSeries({ chosenLabel, poupancaLabel }), [chosenLabel, poupancaLabel]);

  const result = useMemo(() => {
    if (initial === 0 && monthly === 0) return null;
    const chosen = simulateGrowth({ initial, monthly, months, annualPct: choice.annualPct, taxed: choice.taxed });
    const poupanca = simulateGrowth({
      initial,
      monthly,
      months,
      annualPct: poupancaAnnualPct(REFERENCE_SELIC_PCT),
      taxed: false,
    });

    // Um ponto por mês fica pesado de ler em prazos longos: no máximo ~24 pontos no eixo, e em tela
    // estreita só anos inteiros (até ~7 rótulos), senão os rótulos do eixo se sobrepõem.
    const step = narrowChart
      ? months < 24
        ? Math.max(1, Math.ceil(months / 6))
        : 12 * Math.ceil(months / 12 / 6)
      : months >= 24
        ? 6 * Math.ceil(months / 6 / 24)
        : Math.max(1, Math.ceil(months / 24));
    const data = chosen.series
      .filter((point) => point.month % step === 0 || point.month === months)
      .map((point) => ({
        month: monthLabel(point.month),
        period: periodLabel(point.month),
        invested: Math.round(point.invested * 100) / 100,
        chosen: Math.round(point.net * 100) / 100,
        poupanca: Math.round(poupanca.series[point.month].net * 100) / 100,
      }));

    return { chosen: chosen.summary, poupanca: poupanca.summary, data };
  }, [initial, monthly, months, choice, narrowChart]);

  const diffVsPoupanca = result ? result.chosen.finalNet - result.poupanca.finalNet : 0;

  return (
    <Card title="Simulador de rendimento">
      <div className="investimentos__form">
        <label className="investimentos__field">
          <span className="investimentos__field-label">Quanto você tem pra começar</span>
          <CurrencyInput placeholder="Ex: 1.000,00" value={form.initial} onChange={set("initial")} />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Quanto guarda por mês</span>
          <CurrencyInput placeholder="Ex: 200,00" value={form.monthly} onChange={set("monthly")} />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Por quantos anos</span>
          <input type="number" inputMode="numeric" placeholder="Ex: 5" min="1" max="40" step="1" value={form.years} onChange={set("years")} />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Onde vai guardar</span>
          <select value={form.product} onChange={set("product")}>
            <option value={CUSTOM}>CDB (% do CDI)</option>
            <option value={POUPANCA}>Poupança</option>
            {PRODUCTS.map((product) => (
              <option key={product.id} value={product.id}>
                {product.bank} · {product.name} ({product.pctCdi}% do CDI)
              </option>
            ))}
          </select>
        </label>
        {form.product === CUSTOM && (
          <label className="investimentos__field">
            <span className="investimentos__field-label">% do CDI que o CDB paga</span>
            <input type="number" inputMode="decimal" placeholder="Ex: 100" min="0" step="1" value={form.customPct} onChange={set("customPct")} />
          </label>
        )}
        <label className="investimentos__field">
          <span className="investimentos__field-label">CDI hoje (% ao ano)</span>
          <input type="number" inputMode="decimal" min="0" step="0.01" value={cdi} onChange={(e) => onCdiChange(e.target.value)} />
        </label>
      </div>

      {!result ? (
        <p className="investimentos__hint">Informe um valor inicial ou mensal pra ver a projeção.</p>
      ) : (
        <>
          <div className="investimentos__stats">
            <div className="investimentos__stat">
              <span className="investimentos__stat-label">Você coloca no total</span>
              <strong>{formatCurrency(result.chosen.invested)}</strong>
            </div>
            <div className="investimentos__stat investimentos__stat--good">
              <span className="investimentos__stat-label">{chosenBeforeTax ? "Rendimento antes do IR" : "Rendimento líquido"}</span>
              <strong>{formatCurrency(result.chosen.incomeNet)}</strong>
            </div>
            <div className="investimentos__stat investimentos__stat--main">
              <span className="investimentos__stat-label">{chosenBeforeTax ? "Você teria no fim, antes do IR" : "Você teria no fim"}</span>
              <strong>{formatCurrency(result.chosen.finalNet)}</strong>
            </div>
          </div>

          <h4 className="investimentos__subtitle">Evolução projetada</h4>
          <div ref={chartRef} aria-hidden="true">
            <AreaTrendChart
              data={result.data}
              xKey="month"
              series={series}
              height={narrowChart ? 200 : 220}
              formatValue={(value) => formatCurrency(value)}
              xAxisPadding={CHART_X_PADDING}
            />
          </div>
          <details className="investimentos__explain investimentos__chart-data">
            <summary>Ver os valores do gráfico</summary>
            <div className="investimentos__table-wrap">
              <table className="investimentos__table investimentos__table--wide">
                <caption className="investimentos__visually-hidden">
                  Evolução projetada: {series.map((s) => s.label).join(", ")}, por período
                </caption>
                <thead>
                  <tr>
                    <th scope="col">Período</th>
                    {series.map((s) => (
                      <th key={s.key} scope="col" className="investimentos__num">
                        {s.label}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {result.data.map((row) => (
                    <tr key={row.period}>
                      <th scope="row" className="investimentos__term">
                        {row.period}
                      </th>
                      {series.map((s) => (
                        <td key={s.key} className="investimentos__num" data-label={s.label}>
                          {formatCurrency(row[s.key])}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </details>

          {form.product !== POUPANCA && (
            <p className="investimentos__verdict">
              Na poupança, o mesmo plano chegaria a <strong>{formatCurrency(result.poupanca.finalNet)}</strong> em{" "}
              {years} {years === 1 ? "ano" : "anos"}
              {Math.abs(diffVsPoupanca) < 0.5 ? (
                <>, praticamente o mesmo valor.</>
              ) : (
                <>
                  : {formatCurrency(Math.abs(diffVsPoupanca))} {diffVsPoupanca > 0 ? "a menos" : "a mais"}.
                </>
              )}
              {isCompany && <> Para CNPJ, o rendimento da poupança paga imposto de renda, então esse valor fica menor.</>}
            </p>
          )}

          <details className="investimentos__explain">
            <summary>Como esse número foi calculado</summary>
            <ol>
              {choice.taxed ? (
                <li>
                  O CDI de {formatPercentage(cdiPct)} ao ano
                  {choice.pctCdi != null && <> × {choice.pctCdi}% que a aplicação paga</>} dá uma taxa de{" "}
                  <strong>{formatPercentage(choice.annualPct)} ao ano</strong> (antes do imposto).
                </li>
              ) : (
                <li>
                  A poupança rende {poupancaRuleLabel(REFERENCE_SELIC_PCT)}, ou{" "}
                  <strong>{formatPercentage(choice.annualPct)} ao ano</strong> sem a TR, que não entra nesta conta.
                </li>
              )}
              <li>
                Essa taxa anual é convertida pra mensal e aplicada sobre o saldo todo mês: o rendimento de um mês
                também rende no mês seguinte (juros compostos). Por isso a curva sobe cada vez mais rápido.
              </li>
              <li>
                Todo mês entram os {formatCurrency(monthly)} que você guarda, e eles passam a render a partir do mês
                seguinte.
              </li>
              {choice.taxed ? (
                <li>
                  No fim, o imposto de renda é descontado só do rendimento ({formatPercentage(result.chosen.taxRate * 100)}{" "}
                  pra esse prazo, o que dá {formatCurrency(result.chosen.tax)}). O que você colocou não paga imposto.
                </li>
              ) : (
                <li>
                  {isCompany
                    ? "A conta não desconta imposto: para CNPJ, o rendimento da poupança é tributado e o valor real fica menor."
                    : "A poupança é isenta de imposto de renda pra pessoa física."}
                </li>
              )}
            </ol>
          </details>
        </>
      )}
    </Card>
  );
}
