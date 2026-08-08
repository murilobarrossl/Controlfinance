import { CardIcon } from "../../ui/icons/FeatureIcons.jsx";
import Button from "../../ui/Button/Button.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import "./CreditCardVisual.css";

// `recognized` é o RecognizedCardDto do backend: { bankAccountId, bankAccountName, number,
// hasDetails, details }. Quando vem de uma conta sincronizada da Polp (bankAccountName presente),
// a identidade (nome, e o número — se a Polp mandar um mascarado) é real, sem inventar nada. Só
// limite/fatura/vencimento/parcelas (details) dependem de cadastro manual complementar — enquanto
// não existirem (hasDetails=false), o cartão já aparece, só com um convite pra completar.
export default function CreditCardVisual({ recognized, cardholderName, onClick, onComplete }) {
  const { bankAccountName, number, hasDetails, details } = recognized;
  const card = details?.card;
  const isSynced = Boolean(bankAccountName);
  const displayName = bankAccountName || card?.name || "Cartão";
  const usagePercent =
    hasDetails && card.creditLimit > 0 ? Math.min(100, (card.usedLimit / card.creditLimit) * 100) : 0;
  const FaceTag = onClick ? "button" : "div";

  return (
    <div className="credit-card-visual">
      <FaceTag
        type={onClick ? "button" : undefined}
        className={`credit-card-visual__face ${onClick ? "credit-card-visual__face--interactive" : ""}`}
        onClick={onClick}
      >
        <div className="credit-card-visual__top">
          <span className="credit-card-visual__chip" aria-hidden="true" />
          <span className="credit-card-visual__brand">
            {card?.brand || (isSynced ? "Sincronizado via Polp" : "Cartão de crédito")}
          </span>
        </div>

        {number && <span className="credit-card-visual__number">{number}</span>}

        <span className="credit-card-visual__name">{displayName}</span>

        <div className="credit-card-visual__bottom">
          <span className="credit-card-visual__holder">{cardholderName || "Titular"}</span>
          <span className="credit-card-visual__due">
            {hasDetails ? `Vence dia ${card.dueDay}` : "Dados a completar"}
          </span>
        </div>
      </FaceTag>

      {hasDetails ? (
        <div className="credit-card-visual__stats">
          <div className="credit-card-visual__stat">
            <span className="credit-card-visual__stat-label">Fatura atual</span>
            <span className="credit-card-visual__stat-value">{formatCurrency(details.currentInvoice)}</span>
          </div>
          <div className="credit-card-visual__stat">
            <span className="credit-card-visual__stat-label">Próximo vencimento</span>
            <span className="credit-card-visual__stat-value">{formatDate(details.invoiceDueDate)}</span>
          </div>
          <div className="credit-card-visual__stat">
            <span className="credit-card-visual__stat-label">Parcelas ativas</span>
            <span className="credit-card-visual__stat-value">{details.installments?.length ?? 0}</span>
          </div>

          <div className="credit-card-visual__limit">
            <div className="credit-card-visual__limit-labels">
              <span>
                Limite usado: <strong>{formatCurrency(card.usedLimit)}</strong> de {formatCurrency(card.creditLimit)}
              </span>
              <span>{Math.round(usagePercent)}%</span>
            </div>
            <div className="credit-card-visual__progress">
              <div className="credit-card-visual__progress-fill" style={{ width: `${usagePercent}%` }} />
            </div>
          </div>
        </div>
      ) : (
        <div className="credit-card-visual__incomplete">
          <p>
            {isSynced
              ? "Reconhecemos esse cartão pela sua conta sincronizada, mas ainda faltam limite, fechamento e vencimento."
              : "Ainda faltam limite, fechamento e vencimento desse cartão."}
          </p>
          {onComplete && (
            <Button as="button" type="button" variant="primary" size="sm" onClick={onComplete}>
              Completar dados do cartão
            </Button>
          )}
        </div>
      )}

      {onClick && (
        <span className="credit-card-visual__hint">
          <CardIcon /> Clique pra ver todos os seus cartões
        </span>
      )}
    </div>
  );
}
