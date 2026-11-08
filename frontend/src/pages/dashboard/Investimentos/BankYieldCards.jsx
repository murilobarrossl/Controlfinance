import { useMemo } from "react";
import Card from "../../../components/ui/Card/Card.jsx";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import { CoinsIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { getInvestmentModality } from "../../../utils/accountKind.js";
import { formatCurrency } from "../../../utils/financeMath.js";
import { poupancaAnnualPct, simulateGrowth } from "../../../utils/investmentMath.js";
import { REFERENCE_SELIC_PCT, findBankByName } from "../../../utils/investmentProducts.js";
import { poupancaRuleLabel } from "./poupancaRule.js";

const EXAMPLE_AMOUNT = 1000;
// Só caixinha e porquinho seguem a taxa dos produtos de referência. CDB, Tesouro e fundos
// ("investimento") têm taxa própria que não conhecemos: somá-los aqui estimaria rendimento errado.
const REFERENCE_RATE_MODALITIES = new Set(["caixinha", "porquinho"]);
const RESTATES_RATE = /^(rende |até )?\d+% do cdi/i;

function liquidityLabel(liquidity) {
  if (liquidity === "Imediata") return "Resgate imediato";
  return `Resgate ${liquidity.charAt(0).toLowerCase()}${liquidity.slice(1)}`;
}

function normalizeColor(raw) {
  if (!raw) return undefined;
  return raw.startsWith("#") ? raw : `#${raw}`;
}

// Rendimento líquido médio por mês numa aplicação de 1 ano, com o IR desse prazo já descontado.
// Um valor "por mês" é mais fácil de sentir do que uma taxa anual, e 1 ano é o prazo em que a
// maior parte das caixinhas é usada.
function monthlyNetYield(amount, annualPct, taxed) {
  if (amount <= 0) return 0;
  return simulateGrowth({ initial: amount, monthly: 0, months: 12, annualPct, taxed }).summary.incomeNet / 12;
}

function ProductRow({ product, cdiPct, amount }) {
  const base = monthlyNetYield(amount, (cdiPct * product.pctCdi) / 100, true);
  const max = product.pctCdiMax ? monthlyNetYield(amount, (cdiPct * product.pctCdiMax) / 100, true) : null;

  return (
    <li className="yield-card__product">
      <span className="yield-card__product-name">{product.name}</span>

      <div className="yield-card__chips">
        <span className="yield-card__chip">{product.pctCdi}% do CDI</span>
        {product.liquidity && <span className="yield-card__chip">{liquidityLabel(product.liquidity)}</span>}
      </div>

      <div className="yield-card__estimate">
        <span className="yield-card__estimate-value">≈ {formatCurrency(base)}</span>
        <span className="yield-card__estimate-label">por mês, líquido</span>
      </div>

      {max != null && (
        <p className="yield-card__upgrade">
          Até {product.pctCdiMax}% do CDI com condições: ≈ {formatCurrency(max)} por mês
        </p>
      )}
      {product.note && (max != null || !RESTATES_RATE.test(product.note)) && (
        <p className="yield-card__note">{product.note}</p>
      )}
    </li>
  );
}

export default function BankYieldCards({ accounts, connectors, cdi, onCdiChange, isCompany }) {
  const cdiPct = Math.max(0, Number(cdi) || 0);

  const { cards, poupancaBalance, uncoveredNames } = useMemo(() => {
    const connectorsById = new Map(connectors.map((c) => [c.id, c]));
    const byBank = new Map(); // chave do banco de referência -> dados
    const uncovered = new Set();
    let poupanca = null;

    for (const account of accounts) {
      const connector = connectorsById.get(account.bankCode);
      const bank = findBankByName(connector?.name);
      const modality = getInvestmentModality(account);

      if (modality?.key === "poupanca" && account.balance != null) {
        poupanca = (poupanca ?? 0) + account.balance;
      }

      if (!bank) {
        if (connector?.name && !account.isCreditCard) uncovered.add(connector.name);
        continue;
      }
      if (!byBank.has(bank.key)) {
        byBank.set(bank.key, { bank, connector, saved: 0, hasSaved: false });
      }
      const entry = byBank.get(bank.key);
      if (modality && REFERENCE_RATE_MODALITIES.has(modality.key) && account.balance != null) {
        entry.saved += account.balance;
        entry.hasSaved = true;
      }
    }

    return {
      cards: [...byBank.values()].sort((a, b) => b.saved - a.saved),
      poupancaBalance: poupanca,
      uncoveredNames: [...uncovered].filter((name) => !findBankByName(name)),
    };
  }, [accounts, connectors]);

  const hasAnything = cards.length > 0 || poupancaBalance != null;

  return (
    <section className="yield-section">
      <header className="yield-section__header">
        <h3 className="yield-section__title">Quanto rende onde você guarda</h3>
        <label className="yield-section__cdi">
          <span>CDI considerado</span>
          <span className="yield-section__cdi-input">
            <input type="number" inputMode="decimal" min="0" step="0.01" value={cdi} onChange={(e) => onCdiChange(e.target.value)} />% ao ano
          </span>
        </label>
      </header>

      {!hasAnything ? (
        <Card>
          <p className="investimentos__hint">
            Nenhum banco conectado tem taxas de referência cadastradas.
            {uncoveredNames.length > 0 && ` Sem dados para: ${uncoveredNames.join(", ")}.`}
          </p>
        </Card>
      ) : (
        <div className="yield-grid">
          {cards.map(({ bank, connector, saved, hasSaved }) => (
            <Card key={bank.key} className="yield-card">
              <header className="yield-card__header">
                <BankLogo name={connector?.name ?? bank.bank} logoUrl={connector?.logoUrl} color={normalizeColor(connector?.color)} />
                <span className="yield-card__bank">{bank.bank}</span>
              </header>
              <p className="yield-card__basis">
                {hasSaved && saved > 0
                  ? `Cada estimativa considera todo o seu saldo de ${formatCurrency(saved)} em caixinhas e porquinhos aqui.`
                  : `Cada estimativa considera ${formatCurrency(EXAMPLE_AMOUNT)} guardados.`}
              </p>
              <ul className="yield-card__products">
                {bank.products.map((product) => (
                  <ProductRow
                    key={product.id}
                    product={product}
                    cdiPct={cdiPct}
                    amount={hasSaved && saved > 0 ? saved : EXAMPLE_AMOUNT}
                  />
                ))}
              </ul>
            </Card>
          ))}

          {poupancaBalance != null && (
            <Card className="yield-card">
              <header className="yield-card__header">
                <span className="yield-card__bank-icon" aria-hidden="true">
                  <CoinsIcon />
                </span>
                <span className="yield-card__bank">Poupança</span>
              </header>
              <ul className="yield-card__products">
                <li className="yield-card__product">
                  <div className="yield-card__chips">
                    <span className="yield-card__chip">{poupancaRuleLabel(REFERENCE_SELIC_PCT)}</span>
                    <span className="yield-card__chip">{isCompany ? "Tributada para CNPJ" : "Sem IR para pessoa física"}</span>
                  </div>
                  <div className="yield-card__estimate">
                    <span className="yield-card__estimate-value">
                      ≈ {formatCurrency(monthlyNetYield(poupancaBalance, poupancaAnnualPct(REFERENCE_SELIC_PCT), false))}
                    </span>
                    <span className="yield-card__estimate-label">{isCompany ? "por mês, antes do imposto" : "por mês"}</span>
                  </div>
                  <p className="yield-card__basis">Sobre o seu saldo de {formatCurrency(poupancaBalance)}, sem a TR.</p>
                </li>
              </ul>
            </Card>
          )}
        </div>
      )}

      {hasAnything && uncoveredNames.length > 0 && (
        <p className="investimentos__fineprint">
          Sem taxas de referência para {uncoveredNames.join(", ")}.
        </p>
      )}
      <p className="investimentos__fineprint">
        Estimativas, não garantias nem recomendação de investimento. Resgates em menos de 30 dias podem ter IOF.
      </p>
    </section>
  );
}
