import { useMemo, useState } from "react";
import Card from "../../../components/ui/Card/Card.jsx";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import InfoTip from "../../../components/ui/InfoTip/InfoTip.jsx";
import { PiggyBankIcon, BoxIcon, CoinsIcon, ChartIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { getInvestmentModality } from "../../../utils/accountKind.js";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
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

function ProductRow({ product, cdiPct, amount, usingOwnBalance }) {
  const base = monthlyNetYield(amount, (cdiPct * product.pctCdi) / 100, true);
  const max = product.pctCdiMax ? monthlyNetYield(amount, (cdiPct * product.pctCdiMax) / 100, true) : null;

  return (
    <li className="yield-card__product">
      <div className="yield-card__product-top">
        <span className="yield-card__product-icon" aria-hidden="true">
          <ProductIcon name={product.name} />
        </span>
        <span className="yield-card__product-name">{product.name}</span>
        <InfoTip label={`O que é ${product.name}?`} align="left">
          Por trás, costuma ser um CDB: um empréstimo que você faz ao banco e que ele devolve com juros. Tem garantia
          do FGC até R$ 250 mil por CPF e por instituição.
        </InfoTip>
      </div>

      <div className="yield-card__chips">
        <span className="yield-card__chip">
          {product.pctCdi}% do CDI
          <InfoTip label="O que é % do CDI?" align="left">
            O CDI é a taxa de referência da renda fixa, hoje cerca de {formatPercentage(cdiPct)} ao ano. “{product.pctCdi}%
            do CDI” quer dizer que seu dinheiro rende {product.pctCdi === 100 ? "exatamente isso" : `${product.pctCdi}% disso`}.
            Acima de 100% rende mais que o CDI; abaixo, menos.
          </InfoTip>
        </span>
        {product.liquidity === "Imediata" && (
          <span className="yield-card__chip">
            Resgate imediato
            <InfoTip label="O que é resgate imediato?" align="left">
              Você pode tirar o dinheiro quando quiser, sem perder o que já rendeu.
            </InfoTip>
          </span>
        )}
      </div>

      <div className="yield-card__estimate">
        <span className="yield-card__estimate-value">≈ {formatCurrency(base)}</span>
        <span className="yield-card__estimate-label">
          por mês, líquido
          <InfoTip label="Como esse valor é calculado?" align="right">
            Média mensal de um ano de rendimento, já com o imposto de renda descontado (22,5% até 180 dias, 20% até 360,
            17,5% até 720 e 15% acima; o banco desconta sozinho no resgate). É uma estimativa: o CDI muda.
          </InfoTip>
        </span>
      </div>
      <p className="yield-card__basis">
        {usingOwnBalance ? `Sobre o seu saldo de ${formatCurrency(amount)} aqui.` : `A cada ${formatCurrency(amount)} guardados.`}
      </p>

      {max != null && (
        <p className="yield-card__upgrade">
          Com as condições do banco, até {product.pctCdiMax}% do CDI: ≈ {formatCurrency(max)} por mês
          <InfoTip label="Que condições são essas?" align="left">
            Percentuais mais altos costumam exigir plano pago, movimentar a conta ou ter um limite de valor. Confirme no
            app do banco se você se enquadra.
          </InfoTip>
        </p>
      )}
      <p className="yield-card__note">{product.note}</p>
    </li>
  );
}

// Uma caixa por banco que o usuário realmente usa, com as caixinhas/cofrinhos/porquinhos dele
// dentro. Nada é mostrado de bancos que o usuário não conectou: os termos técnicos aparecem só aqui,
// no contexto, explicados no "!" (ver InfoTip), em vez de um glossário genérico.
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
          <span>
            CDI considerado
            <InfoTip label="O que é o CDI?" align="right">
              Taxa de referência da renda fixa, sempre perto da Selic (referência: {formatPercentage(REFERENCE_SELIC_PCT)} ao
              ano). Se mudou, ajuste aqui e todos os valores abaixo são recalculados.
            </InfoTip>
          </span>
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
                {bank.products.map((product) => (
                  <ProductRow
                    key={product.id}
                    product={product}
                    cdiPct={cdiPct}
                    amount={hasSaved && saved > 0 ? saved : EXAMPLE_AMOUNT}
                    usingOwnBalance={hasSaved && saved > 0}
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
                      <InfoTip label="Como a poupança rende?" align="left">
                        Com a Selic acima de 8,5% ao ano, a poupança rende 0,5% ao mês (cerca de 6,2% ao ano), bem abaixo
                        do CDI. Não tem imposto de renda.
                      </InfoTip>
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
