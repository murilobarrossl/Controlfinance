import { useMemo, useState } from "react";
import Card from "../../../components/ui/Card/Card.jsx";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import { PiggyBankIcon, BoxIcon, CoinsIcon, ChartIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { getInvestmentModality } from "../../../utils/accountKind.js";
import { formatCurrency } from "../../../utils/financeMath.js";
import { poupancaAnnualPct, simulateGrowth } from "../../../utils/investmentMath.js";
import {
  REFERENCE_CDI_PCT,
  REFERENCE_DATE_LABEL,
  REFERENCE_SELIC_PCT,
  findBankByName,
} from "../../../utils/investmentProducts.js";

const EXAMPLE_AMOUNT = 1000;

function normalizeColor(raw) {
  if (!raw) return undefined;
  return raw.startsWith("#") ? raw : `#${raw}`;
}

function ProductIcon({ name }) {
  if (/caixinha/i.test(name)) return <BoxIcon />;
  if (/porquinho|cofrinho|cofre/i.test(name)) return <PiggyBankIcon />;
  return <ChartIcon />;
}

// Rendimento líquido médio por mês numa aplicação de 1 ano, com o IR desse prazo já descontado.
// Um valor "por mês" é mais fácil de sentir do que uma taxa anual, e 1 ano é o prazo em que a
// maior parte das caixinhas é usada.
function monthlyNetYield(amount, annualPct, taxed) {
  if (amount <= 0) return 0;
  return simulateGrowth({ initial: amount, monthly: 0, months: 12, annualPct, taxed }).summary.incomeNet / 12;
}

function ProductRow({ product, cdiPct, amount, usingOwnBalance, showBasis }) {
  const base = monthlyNetYield(amount, (cdiPct * product.pctCdi) / 100, true);
  const max = product.pctCdiMax ? monthlyNetYield(amount, (cdiPct * product.pctCdiMax) / 100, true) : null;

  return (
    <li className="yield-card__product">
      <div className="yield-card__product-top">
        <span className="yield-card__product-icon" aria-hidden="true">
          <ProductIcon name={product.name} />
        </span>
        <span className="yield-card__product-name">{product.name}</span>
      </div>

      <div className="yield-card__chips">
        <span className="yield-card__chip">{product.pctCdi}% do CDI</span>
        {product.liquidity === "Imediata" && (
          <span className="yield-card__chip">Resgate imediato</span>
        )}
      </div>

      <div className="yield-card__estimate">
        <span className="yield-card__estimate-value">≈ {formatCurrency(base)}</span>
        <span className="yield-card__estimate-label">por mês, líquido</span>
      </div>
      {showBasis && (
        <p className="yield-card__basis">
          {usingOwnBalance ? `Sobre o seu saldo de ${formatCurrency(amount)} aqui.` : `A cada ${formatCurrency(amount)} guardados.`}
        </p>
      )}

      {max != null && (
        <p className="yield-card__upgrade">
          Com as condições do banco, até {product.pctCdiMax}% do CDI: ≈ {formatCurrency(max)} por mês
        </p>
      )}
      <p className="yield-card__note">{product.note}</p>
    </li>
  );
}

export default function BankYieldCards({ accounts, connectors }) {
  const [cdi, setCdi] = useState(String(REFERENCE_CDI_PCT));
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
      if (modality && modality.key !== "poupanca" && account.balance != null) {
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
        <div>
          <h3 className="yield-section__title">Quanto rende onde você guarda</h3>
          <p className="investimentos__section-hint yield-section__hint">
            Só os bancos que você conectou, com as caixinhas, cofrinhos e porquinhos de cada um. Percentuais de referência
            de {REFERENCE_DATE_LABEL}: os bancos mudam isso sem aviso, então confirme no app.
          </p>
        </div>
        <label className="yield-section__cdi">
          <span>CDI considerado</span>
          <span className="yield-section__cdi-input">
            <input type="number" min="0" step="0.01" value={cdi} onChange={(e) => setCdi(e.target.value)} />% ao ano
          </span>
        </label>
      </header>

      {!hasAnything ? (
        <Card>
          <p className="investimentos__hint">
            Conecte um banco pra ver aqui quanto as caixinhas e cofrinhos dele rendem.
            {uncoveredNames.length > 0 && ` Ainda não temos dados de rendimento de: ${uncoveredNames.join(", ")}.`}
          </p>
        </Card>
      ) : (
        <div className="yield-grid">
          {cards.map(({ bank, connector, saved, hasSaved }) => (
            <Card key={bank.key} className="yield-card">
              <header className="yield-card__header">
                <BankLogo name={connector?.name ?? bank.bank} logoUrl={connector?.logoUrl} color={normalizeColor(connector?.color)} />
                <span className="yield-card__bank">{bank.bank}</span>
                {hasSaved && (
                  <span className="yield-card__saved">
                    Guardado aqui <strong>{formatCurrency(saved)}</strong>
                  </span>
                )}
              </header>
              <ul className="yield-card__products">
                {bank.products.map((product, index) => (
                  <ProductRow
                    key={product.id}
                    product={product}
                    cdiPct={cdiPct}
                    amount={hasSaved && saved > 0 ? saved : EXAMPLE_AMOUNT}
                    usingOwnBalance={hasSaved && saved > 0}
                    showBasis={index === 0}
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
                <span className="yield-card__saved">
                  Guardado <strong>{formatCurrency(poupancaBalance)}</strong>
                </span>
              </header>
              <ul className="yield-card__products">
                <li className="yield-card__product">
                  <div className="yield-card__product-top">
                    <span className="yield-card__product-icon" aria-hidden="true">
                      <CoinsIcon />
                    </span>
                    <span className="yield-card__product-name">Poupança</span>
                  </div>
                  <div className="yield-card__chips">
                    <span className="yield-card__chip">
                      0,5% ao mês
                    </span>
                    <span className="yield-card__chip">Sem imposto</span>
                  </div>
                  <div className="yield-card__estimate">
                    <span className="yield-card__estimate-value">
                      ≈ {formatCurrency(monthlyNetYield(poupancaBalance, poupancaAnnualPct(REFERENCE_SELIC_PCT), false))}
                    </span>
                    <span className="yield-card__estimate-label">por mês</span>
                  </div>
                  <p className="yield-card__basis">Sobre o seu saldo de {formatCurrency(poupancaBalance)}.</p>
                </li>
              </ul>
            </Card>
          )}
        </div>
      )}

      {hasAnything && uncoveredNames.length > 0 && (
        <p className="investimentos__fineprint">
          Ainda não temos dados de rendimento de: {uncoveredNames.join(", ")}. Confira no app do banco.
        </p>
      )}
      <p className="investimentos__fineprint">
        Estimativas, não garantias, e não são recomendação de investimento. Resgates em menos de 30 dias podem ter IOF.
      </p>
    </section>
  );
}
