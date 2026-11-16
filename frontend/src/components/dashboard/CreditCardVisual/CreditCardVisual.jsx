import { formatCurrency, formatDate, formatPercentage } from "../../../utils/financeMath.js";
import CardFace from "../CardFace/CardFace.jsx";
import { formatDueDay, formatDataAge } from "../../../utils/invoiceDue.js";
import { cardLimits, isInvoiceOverdue } from "../../../utils/cardLimits.js";
import LimitBar from "../LimitBar/LimitBar.jsx";
import "./CreditCardVisual.css";

// `card` é o RecognizedCardDto do backend: a própria BankAccount sincronizada da Polp. Campos
// que a Polp não mandou (creditLimit/closingDay/dueDay) vêm null e aparecem como "não informado":
// nada é preenchido à mão. Sem número de cartão fabricado — `number` só aparece
// quando a própria Polp manda um (mascarado).
export default function CreditCardVisual({
  card,
  cardholderName,
  onClick,
  showKeyStats = true,
  showFace = true,
}) {
  const { hasLimit, available: availableLimit, usagePercent, overLimit, overAmount } = cardLimits(card);
  const FaceTag = onClick ? "button" : "div";
  const pastDue = isInvoiceOverdue(card);
  const dataAge = pastDue ? formatDataAge(card.connectionLastSyncedAt) : null;

  return (
    <div className="credit-card-visual">
      {showFace && (
        <FaceTag
          type={onClick ? "button" : undefined}
          className={`credit-card-visual__face ${onClick ? "credit-card-visual__face--interactive" : ""}`}
          onClick={onClick}
          aria-label={onClick ? `${card.name}: ver detalhes em Cartões` : undefined}
        >
          <CardFace card={card} holder={cardholderName} />
        </FaceTag>
      )}

      <div className="credit-card-visual__stats">
        {showKeyStats && (
        <div className="credit-card-visual__key">
          <div className="credit-card-visual__stat">
            <span className="credit-card-visual__stat-label">Fatura atual</span>
            <span className={`credit-card-visual__stat-value${pastDue ? " credit-card-visual__stat-value--expense" : ""}`}>
              {card.currentInvoice != null ? formatCurrency(card.currentInvoice) : "—"}
            </span>
          </div>
          <div className="credit-card-visual__stat">
            <span className="credit-card-visual__stat-label">Próximo vencimento</span>
            <span className={`credit-card-visual__stat-value${pastDue ? " credit-card-visual__stat-value--expense" : ""}`}>
              {!card.invoiceDueDate ? "—" : pastDue ? formatDueDay(card.invoiceDueDate) : formatDate(card.invoiceDueDate)}
            </span>
          </div>
          {dataAge && <span className="credit-card-visual__stat-note">{dataAge}</span>}
        </div>
        )}

        {hasLimit ? (
          <div className="credit-card-visual__limit">
            <div className="credit-card-visual__stat">
              <span className="credit-card-visual__stat-label">Limite disponível</span>
              <span className="credit-card-visual__stat-value">
                {availableLimit != null ? formatCurrency(availableLimit) : "—"}
              </span>
            </div>
            <div className="credit-card-visual__limit-labels">
              <span>
                Limite usado: <strong className={overLimit ? "credit-card-visual__used--over" : undefined}>{formatCurrency(card.usedLimit)}</strong> de {formatCurrency(card.creditLimit)}
              </span>
              {usagePercent != null && <span>{formatPercentage(usagePercent)}</span>}
            </div>
            {overLimit && (
              <span className="credit-card-visual__over">
                Limite excedido em <strong>{formatCurrency(overAmount)}</strong>
              </span>
            )}
            <LimitBar usagePercent={usagePercent} />
          </div>
        ) : (
          <div className="credit-card-visual__limit">
            <span className="credit-card-visual__limit-labels">
              <span>Limite não informado</span>
            </span>
          </div>
        )}
      </div>
    </div>
  );
}
