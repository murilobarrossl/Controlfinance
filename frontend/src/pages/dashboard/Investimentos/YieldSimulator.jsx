import { useMemo, useState } from "react";
import Card from "../../../components/ui/Card/Card.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import AreaTrendChart from "../../../components/charts/AreaTrendChart.jsx";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { readCssVar } from "../../../utils/cssVar.js";
import { poupancaAnnualPct, simulateGrowth } from "../../../utils/investmentMath.js";
import {
  REFERENCE_CDI_PCT,
  REFERENCE_SELIC_PCT,
  flattenProducts,
} from "../../../utils/investmentProducts.js";

const PRODUCTS = flattenProducts();
const CUSTOM = "custom";
const POUPANCA = "poupanca";

function buildSeries() {
  return [
    { key: "invested", label: "O que você colocou", color: readCssVar("--color-text-tertiary", "#8b8b8b") },
    { key: "chosen", label: "Aplicação escolhida (líquido)", color: "#4ECDC4" },
    { key: "poupanca", label: "Poupança", color: "#C9923E" },
  ];
}

function monthLabel(month) {
  if (month === 0) return "Hoje";
  if (month % 12 === 0) return `${month / 12} ${month === 12 ? "ano" : "anos"}`;
  return `${month}m`;
}

// Quanto um valor inicial mais aportes mensais viram no tempo. Mostra o resultado já com imposto de
// renda, compara com a poupança e explica o cálculo em linguagem simples, pra quem nunca viu
// "juros compostos" entender de onde o número saiu.
export default function YieldSimulator() {
  const [form, setForm] = useState({
    initial: "1000",
    monthly: "200",
    years: "5",
    product: "nubank-caixinha",
    customPct: "100",
    cdi: String(REFERENCE_CDI_PCT),
  });

  const series = useMemo(() => buildSeries(), []);
  const set = (field) => (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }));

  const initial = Math.max(0, Number(form.initial) || 0);
  const monthly = Math.max(0, Number(form.monthly) || 0);
  const years = Math.min(40, Math.max(1, Number(form.years) || 1));
  const months = Math.round(years * 12);
  const cdiPct = Math.max(0, Number(form.cdi) || 0);

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

    // Um ponto por mês fica pesado de ler em prazos longos: no máximo ~24 pontos no eixo.
    const step = Math.max(1, Math.ceil(months / 24));
    const data = chosen.series
      .filter((point) => point.month % step === 0 || point.month === months)
      .map((point) => ({
        month: monthLabel(point.month),
        invested: Math.round(point.invested * 100) / 100,
        chosen: Math.round(point.net * 100) / 100,
        poupanca: Math.round(poupanca.series[point.month].net * 100) / 100,
      }));

    return { chosen: chosen.summary, poupanca: poupanca.summary, data };
  }, [initial, monthly, months, choice]);

  const diffVsPoupanca = result ? result.chosen.finalNet - result.poupanca.finalNet : 0;

  return (
    <Card title="Simulador de rendimento">
      <p className="investimentos__section-hint">
        Veja quanto seu dinheiro pode render guardando um valor agora e/ou um pouco todo mês. O gráfico compara a
        aplicação escolhida com a poupança, pra você enxergar a diferença no longo prazo. É uma projeção: o CDI muda
        com o tempo e nenhum rendimento é garantido.
      </p>

      <div className="investimentos__form">
        <label className="investimentos__field">
          <span className="investimentos__field-label">Quanto você tem pra começar</span>
          <CurrencyInput placeholder="Ex: 1000" value={form.initial} onChange={set("initial")} />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Quanto guarda por mês</span>
          <CurrencyInput placeholder="Ex: 200" value={form.monthly} onChange={set("monthly")} />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Por quantos anos</span>
          <input type="number" min="1" max="40" step="1" value={form.years} onChange={set("years")} />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Onde vai guardar</span>
          <select value={form.product} onChange={set("product")}>
            {PRODUCTS.map((product) => (
              <option key={product.id} value={product.id}>
                {product.bank} · {product.name} ({product.pctCdi}% do CDI)
              </option>
            ))}
            <option value={POUPANCA}>Poupança</option>
            <option value={CUSTOM}>Outro CDB (informar % do CDI)</option>
          </select>
        </label>
        {form.product === CUSTOM && (
          <label className="investimentos__field">
            <span className="investimentos__field-label">% do CDI que o CDB paga</span>
            <input type="number" min="0" step="1" value={form.customPct} onChange={set("customPct")} />
          </label>
        )}
        <label className="investimentos__field">
          <span className="investimentos__field-label">CDI hoje (% ao ano)</span>
          <input type="number" min="0" step="0.01" value={form.cdi} onChange={set("cdi")} />
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
              <span className="investimentos__stat-label">Rendimento líquido</span>
              <strong>{formatCurrency(result.chosen.incomeNet)}</strong>
            </div>
            <div className="investimentos__stat investimentos__stat--main">
              <span className="investimentos__stat-label">Você teria no fim</span>
              <strong>{formatCurrency(result.chosen.finalNet)}</strong>
            </div>
          </div>

          <h4 className="investimentos__subtitle">Como seu dinheiro cresce com o tempo</h4>
          <AreaTrendChart
            data={result.data}
            xKey="month"
            series={series}
            height={280}
            formatValue={(value) => formatCurrency(value)}
          />
          <ul className="investimentos__chart-key">
            <li>
              <span className="investimentos__legend-dot" style={{ background: series[0].color }} />
              <span>
                <strong>Linha cinza:</strong> o que saiu do seu bolso (valor inicial mais o que você guarda todo mês).
                Sem rendimento nenhum, seu dinheiro seguiria essa linha.
              </span>
            </li>
            <li>
              <span className="investimentos__legend-dot" style={{ background: series[1].color }} />
              <span>
                <strong>Linha verde:</strong> quanto você teria em <em>{choice.label}</em>, já descontado o imposto de
                renda.
              </span>
            </li>
            <li>
              <span className="investimentos__legend-dot" style={{ background: series[2].color }} />
              <span>
                <strong>Linha amarela:</strong> quanto você teria se deixasse tudo na poupança.
              </span>
            </li>
          </ul>
          <p className="investimentos__chart-takeaway">
            <strong>O que olhar:</strong> o espaço entre a linha cinza e a verde é o <strong>rendimento</strong>, o
            dinheiro que trabalhou por você ({formatCurrency(result.chosen.incomeNet)} ao final). Quanto mais tempo,
            mais esse espaço abre, porque o rendimento também passa a render.
          </p>

          <p className="investimentos__verdict">
            {Math.abs(diffVsPoupanca) < 0.5 ? (
              <>Nesse cenário o resultado é praticamente igual ao da poupança.</>
            ) : diffVsPoupanca > 0 ? (
              <>
                Em {years} {years === 1 ? "ano" : "anos"}, <strong>{choice.label}</strong> rende{" "}
                <strong>{formatCurrency(diffVsPoupanca)}</strong> a mais que a poupança.
              </>
            ) : (
              <>
                Em {years} {years === 1 ? "ano" : "anos"}, <strong>{choice.label}</strong> rende{" "}
                <strong>{formatCurrency(-diffVsPoupanca)}</strong> a menos que a poupança.
              </>
            )}
          </p>

          <details className="investimentos__explain">
            <summary>Como esse número foi calculado</summary>
            <ol>
              <li>
                O CDI de {formatPercentage(cdiPct)} ao ano
                {choice.pctCdi != null && <> × {choice.pctCdi}% que a aplicação paga</>} dá uma taxa de{" "}
                <strong>{formatPercentage(choice.annualPct)} ao ano</strong>
                {choice.taxed ? " (antes do imposto)" : " (a poupança segue a regra legal de 0,5% ao mês)"}.
              </li>
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
                <li>A poupança é isenta de imposto de renda pra pessoa física.</li>
              )}
            </ol>
          </details>
        </>
      )}
    </Card>
  );
}
