import { useMemo, useState } from "react";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import InvestmentYieldChart from "./InvestmentYieldChart.jsx";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { simulateGrowth } from "../../../utils/investmentMath.js";
import { formatRateDate } from "../../../utils/investmentRates.js";
import { CHART_COLORS } from "../../../components/charts/chartTheme.js";
import { financialTone } from "../../../utils/financialTone.js";
import useIsNarrow from "./useIsNarrow.js";

const HORIZONS = [
  { months: 6, label: "6 meses" },
  { months: 12, label: "1 ano" },
  { months: 24, label: "2 anos" },
  { months: 60, label: "5 anos" },
];

function fineprint(rule) {
  const base = "Projeção, não rendimento garantido nem recomendação de investimento.";
  if (rule.source === "legal") return `${base} Não inclui TR${rule.beforeTax ? " nem a tributação da poupança para CNPJ" : ""}.`;
  const excluded = rule.profile?.kind === "SelicSpread" ? "IOF de resgates em menos de 30 dias nem a taxa de custódia da B3" : "IOF de resgates em menos de 30 dias";
  return `${base} Não inclui ${excluded}.`;
}

function RuleExplanation({ rule, isCompany, taxRate, tax }) {
  const basis = rule.basis;
  const basisText = basis && (
    <>
      {basis.name} de {formatPercentage(basis.annualPct)} ao ano, divulgada pelo Banco Central
      {basis.date ? ` (${formatRateDate(basis.date)})` : ""}
    </>
  );

  let first;
  if (rule.source === "legal") {
    first = (
      <>
        A poupança rende {rule.label}, pela regra legal. Com a {basisText}, isso dá{" "}
        <strong>{formatPercentage(rule.annualPct)} ao ano</strong>. A TR fica de fora da conta.
      </>
    );
  } else if (rule.profile.kind === "Prefixed") {
    first = (
      <>
        Taxa prefixada informada por você: <strong>{formatPercentage(rule.annualPct)} ao ano</strong>.
      </>
    );
  } else if (rule.profile.kind === "CdiPercent") {
    first = (
      <>
        Você informou que esta aplicação rende {rule.label}. Com o {basisText}, isso dá{" "}
        <strong>{formatPercentage(rule.annualPct)} ao ano</strong>.
      </>
    );
  } else {
    first = (
      <>
        O indexador é a Selic: {basisText}
        {rule.spreadMissing ? ", sem adicional informado" : `, com o adicional informado por você`}. Isso dá{" "}
        <strong>{formatPercentage(rule.annualPct)} ao ano</strong>.
      </>
    );
  }

  return (
    <ol>
      <li>{first}</li>
      <li>
        A taxa anual é convertida para mensal e aplicada sobre o saldo todo mês: o rendimento de um mês também rende no
        seguinte. Os aportes entram no fim de cada mês.
      </li>
      <li>
        {rule.taxed ? (
          <>
            O IR estimado incide apenas sobre o rendimento futuro de cada aporte, conforme seu prazo na simulação,
            usando meses de 30 dias. O saldo inicial é tratado como aplicado hoje, pois as datas originais não estão disponíveis.
            A estimativa total é de {formatCurrency(tax)}. A alíquota do saldo inicial ao final do prazo é de {formatPercentage(taxRate * 100)}.
            Isso não representa imposto já descontado pelo banco nem inclui IR sobre ganhos anteriores.
          </>
        ) : rule.source === "legal" ? (
          isCompany ? (
            "Para CNPJ o rendimento da poupança é tributado; esta conta não desconta esse imposto."
          ) : (
            "A poupança é isenta de imposto de renda para pessoa física."
          )
        ) : (
          "Você marcou esta aplicação como isenta de imposto de renda."
        )}
      </li>
      <li>
        A taxa informada é mantida por todo o período. Nas aplicações pós-fixadas, mudanças no indexador podem alterar
        o resultado. Datas de crédito, dias úteis e condições de resgate do produto também podem produzir diferenças.
      </li>
    </ol>
  );
}

export default function InvestmentProjection(props) {
  if (props.importedInvestment != null || props.rule?.source === "imported_contract") {
    return <p className="investimentos__hint" role="status">Projeção indisponível: faltam unidades ou convenções contratuais confirmadas para este investimento.</p>;
  }
  return <ManualInvestmentProjection {...props} />;
}

function ManualInvestmentProjection({ balance, rule, isCompany, idPrefix }) {
  const [months, setMonths] = useState(12);
  const [monthlyInput, setMonthlyInput] = useState("");
  const [view, setView] = useState("yield");
  const [startDate] = useState(() => new Date());
  const [chartRef, narrow] = useIsNarrow();

  const initial = Math.max(0, balance ?? 0);
  const monthly = Math.max(0, Number(monthlyInput) || 0);
  const beforeTax = rule.beforeTax;
  const netWord = beforeTax ? "antes do IR" : rule.taxed ? "líquido" : "";

  const result = useMemo(() => {
    if (initial === 0 && monthly === 0) return null;
    const params = { initial, months, annualPct: rule.annualPct, taxed: rule.taxed, source: rule.source };
    const base = simulateGrowth({ ...params, monthly: 0 });
    const withContributions = monthly > 0 ? simulateGrowth({ ...params, monthly }) : null;

    const data = base.series.slice(1).map((point) => {
      const date = new Date(startDate.getFullYear(), startDate.getMonth() + point.month, 1);
      const extra = withContributions?.series[point.month];
      const previousExtra = withContributions?.series[point.month - 1];
      return {
        month: date.toLocaleDateString("pt-BR", { month: "short", year: "2-digit" }),
        period: date.toLocaleDateString("pt-BR", { month: "long", year: "numeric" }),
        base: point.net,
        baseYield: point.gross - base.series[point.month - 1].gross,
        ...(extra && {
          contributions: extra.net,
          contributionsYield: extra.gross - previousExtra.gross - monthly,
        }),
      };
    });

    return { base: base.summary, withContributions: withContributions?.summary ?? null, data };
  }, [initial, monthly, months, rule.annualPct, rule.taxed, rule.source, startDate]);

  const series = useMemo(() => {
    const yieldColor = (value) => CHART_COLORS[financialTone(value)] ?? CHART_COLORS.neutral;
    const list = [{
      key: view === "yield" ? "baseYield" : "base",
      label: "Sem novos aportes",
      color: view === "yield" && monthly === 0 ? yieldColor(result?.base.incomeGross) : CHART_COLORS.neutral,
    }];
    if (monthly > 0) {
      list.push({
        key: view === "yield" ? "contributionsYield" : "contributions",
        label: `Com ${formatCurrency(monthly)} por mês`,
        color: view === "yield" ? yieldColor(result?.withContributions?.incomeGross) : CHART_COLORS.label,
      });
    }
    return list;
  }, [monthly, view, result]);

  const horizonLabel = HORIZONS.find((h) => h.months === months)?.label;
  const contributed = monthly * months;
  const contributionEffect = result?.withContributions ? result.withContributions.finalNet - result.base.finalNet : 0;

  return (
    <section className="inv-projection" aria-labelledby={`${idPrefix}-projection`}>
      <div className="inv-projection__head">
        <h4 id={`${idPrefix}-projection`} className="inv-detail__title">
          Projeções futuras
        </h4>
        <div className="investimentos__segmented" role="group" aria-label="Prazo da projeção">
          {HORIZONS.map((h) => (
            <button
              key={h.months}
              type="button"
              className={`investimentos__segment${months === h.months ? " investimentos__segment--active" : ""}`}
              aria-pressed={months === h.months}
              onClick={() => setMonths(h.months)}
            >
              {h.label}
            </button>
          ))}
        </div>
      </div>

      <div className="investimentos__form investimentos__form--compact">
        <label className="investimentos__field">
          <span className="investimentos__field-label">Novo aporte por mês (opcional)</span>
          <CurrencyInput placeholder="Ex: 200,00" value={monthlyInput} onChange={(e) => setMonthlyInput(e.target.value)} />
        </label>
      </div>

      {!result ? (
        <p className="investimentos__hint">Sem saldo hoje. Informe um aporte mensal para ver a projeção.</p>
      ) : (
        <>
          <div ref={chartRef} className="inv-projection__chart">
            <InvestmentYieldChart data={result.data} series={series} view={view} onViewChange={setView}
              idPrefix={idPrefix} narrow={narrow} />
          </div>

          <dl className="inv-projection__stats">
            <div className="inv-projection__stat">
              <dt>Saldo de referência</dt>
              <dd>{formatCurrency(initial)}</dd>
            </div>
            <div className="inv-projection__stat">
              <dt>Saldo estimado em {horizonLabel}, sem novos aportes{beforeTax ? " (antes do IR)" : ""}</dt>
              <dd>{formatCurrency(result.base.finalNet)}</dd>
              <span className="inv-projection__note">
                {formatCurrency(result.base.incomeNet)} de rendimento estimado{netWord ? ` ${netWord}` : ""}
              </span>
            </div>
            {result.withContributions && (
              <>
                <div className="inv-projection__stat inv-projection__stat--main">
                  <dt>Saldo estimado com {formatCurrency(monthly)} por mês{beforeTax ? " (antes do IR)" : ""}</dt>
                  <dd>{formatCurrency(result.withContributions.finalNet)}</dd>
                  <span className="inv-projection__note">Você coloca mais {formatCurrency(contributed)}</span>
                </div>
                <div className="inv-projection__stat">
                  <dt>Efeito dos aportes</dt>
                  <dd>+{formatCurrency(contributionEffect)}</dd>
                  <span className="inv-projection__note">
                    {formatCurrency(Math.max(0, contributionEffect - contributed))} é rendimento estimado sobre os aportes
                  </span>
                </div>
              </>
            )}
          </dl>

          <div className="inv-projection__tax-summary">
            <span>Rendimentos estimados em {horizonLabel}{monthly > 0 ? ", com aportes" : ""}</span>
            <dl>
              <div><dt>Bruto acumulado</dt><dd>{formatCurrency((result.withContributions ?? result.base).incomeGross)}</dd></div>
              <div>
                <dt>{rule.taxed ? "IR estimado" : "Imposto de renda"}</dt>
                <dd>{beforeTax ? "Não calculado" : rule.taxed ? formatCurrency((result.withContributions ?? result.base).tax) : "Isento"}</dd>
              </div>
              <div><dt>{beforeTax ? "Rendimento antes do IR" : "Rendimento após IR estimado"}</dt><dd className={`inv-financial--${financialTone((result.withContributions ?? result.base).incomeNet)}`}>{formatCurrency((result.withContributions ?? result.base).incomeNet)}</dd></div>
            </dl>
            {rule.taxed && <p>IR sobre os ganhos futuros. Sem as datas originais, o saldo atual é tratado como uma nova aplicação.</p>}
          </div>

          <details className="investimentos__explain">
            <summary>Ver os valores do gráfico</summary>
            <div className="investimentos__table-wrap">
              <table className="investimentos__table investimentos__table--wide">
                <caption className="investimentos__visually-hidden">{view === "yield" ? "Rendimento bruto mensal estimado" : "Saldo estimado"} por período: {series.map((s) => s.label).join(", ")}</caption>
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

          <details className="investimentos__explain">
            <summary>Como a projeção é calculada</summary>
            <RuleExplanation
              rule={rule}
              isCompany={isCompany}
              taxRate={(result.withContributions ?? result.base).taxRate}
              tax={(result.withContributions ?? result.base).tax}
            />
          </details>
        </>
      )}

      <p className="investimentos__fineprint">{fineprint(rule)}</p>
    </section>
  );
}
