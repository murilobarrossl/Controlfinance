import { useMemo } from "react";
import { StarIcon } from "../../ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatDate, formatPercentage } from "../../../utils/financeMath.js";
import { getCardBase, cardTextColor } from "../../../utils/cardColors.js";
import { formatDueDay, formatDataAge } from "../../../utils/invoiceDue.js";
import { cardLimits, isInvoiceOverdue } from "../../../utils/cardLimits.js";
import LimitBar from "../LimitBar/LimitBar.jsx";
import "./CreditCardVisual.css";

// `card` é o RecognizedCardDto do backend, já achatado — sem entidade separada por trás: quando
// `isSynced` é true, isto É a BankAccount sincronizada da Polp, não um cadastro à parte. Campos
// que a Polp não mandou (creditLimit/closingDay/dueDay) vêm null e aparecem como "não informado":
// nada é preenchido à mão. Sem número de cartão fabricado — `number` só aparece
// quando a própria Polp manda um (mascarado).
export default function CreditCardVisual({
  card,
  cardholderName,
  onClick,
  onSetPrimary,
  showKeyStats = true,
  showFace = true,
}) {
  const { hasLimit, available: availableLimit, usagePercent, overLimit, overAmount } = cardLimits(card);
  const FaceTag = onClick ? "button" : "div";
  const faceStyle = useMemo(() => {
    const base = getCardBase({ name: card.name, productType: card.productType, institutionColor: card.institutionColor });
    return { "--card-base": base, "--card-text": cardTextColor(base) };
  }, [card.name, card.productType, card.institutionColor]);
  const pastDue = isInvoiceOverdue(card);
  const dataAge = pastDue ? formatDataAge(card.connectionLastSyncedAt) : null;

  const primaryButton = (
    <button
      type="button"
      className={`credit-card-visual__star ${card.isPrimary ? "credit-card-visual__star--active" : ""}`}
      onClick={card.isPrimary ? undefined : onSetPrimary}
      aria-pressed={Boolean(card.isPrimary)}
      aria-disabled={card.isPrimary || undefined}
      aria-label="Cartão principal"
      title={card.isPrimary ? "Cartão principal" : "Definir como cartão principal"}
    >
      <StarIcon filled={card.isPrimary} />
    </button>
  );

  return (
    <div className="credit-card-visual">
      {showFace && (
      <FaceTag
        type={onClick ? "button" : undefined}
        className={`credit-card-visual__face ${onClick ? "credit-card-visual__face--interactive" : ""}`}
        style={faceStyle}
        onClick={onClick}
      >
        <div className="credit-card-visual__top">
          <span className="credit-card-visual__name">{card.name}</span>
          <div className="credit-card-visual__top-right">
            <span className="credit-card-visual__brand">
              {card.brand || (card.isSynced ? "Sincronizado via Polp" : "Cartão de crédito")}
            </span>
            {onSetPrimary && primaryButton}
          </div>
        </div>

        {card.number && <span className="credit-card-visual__number">{card.number}</span>}

        <div className="credit-card-visual__bottom">
          <span className="credit-card-visual__holder">{cardholderName || "Titular"}</span>
          <span className="credit-card-visual__due">{card.dueDay ? `Vence dia ${card.dueDay}` : "Vencimento não informado"}</span>
        </div>
      </FaceTag>
      )}

      <div className="credit-card-visual__stats">
        {!showFace && onSetPrimary && (
          <div className="credit-card-visual__minor credit-card-visual__primary">
            <span className="credit-card-visual__stat-label">Cartão principal</span>
            {primaryButton}
          </div>
        )}
        {showKeyStats && (
        <div className="credit-card-visual__key">
          <div className="credit-card-visual__stat">
            <span className="credit-card-visual__stat-label">Fatura atual</span>
            <span className="credit-card-visual__stat-value">
              {card.currentInvoice != null ? formatCurrency(card.currentInvoice) : "—"}
            </span>
          </div>
          <div className="credit-card-visual__stat">
            <span className="credit-card-visual__stat-label">Próximo vencimento</span>
            <span className="credit-card-visual__stat-value">
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
                Limite usado: <strong>{formatCurrency(card.usedLimit)}</strong> de {formatCurrency(card.creditLimit)}
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

        <div className="credit-card-visual__minor">
          <span className="credit-card-visual__stat-label">Parcelas ativas</span>
          <span className="credit-card-visual__minor-value">{card.installments ? card.installments.length : "—"}</span>
        </div>
      </div>
    </div>
  );
}
