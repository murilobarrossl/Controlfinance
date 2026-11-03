import { useMemo } from "react";
import { CardIcon, StarIcon } from "../../ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import { getCardGradient } from "../../../utils/cardColors.js";
import "./CreditCardVisual.css";

// `card` é o RecognizedCardDto do backend, já achatado — sem entidade separada por trás: quando
// `isSynced` é true, isto É a BankAccount sincronizada da Polp, não um cadastro à parte. Campos
// que a Polp não mandou (creditLimit/closingDay/dueDay) vêm null e aparecem como "não informado":
// nada é preenchido à mão. Sem número de cartão fabricado — `number` só aparece
// quando a própria Polp manda um (mascarado); do contrário fica só o chip decorativo.
export default function CreditCardVisual({ card, cardholderName, onClick, onSetPrimary }) {
  const hasLimit = card.creditLimit != null && card.creditLimit > 0;
  const usagePercent = hasLimit && card.usedLimit != null ? Math.min(100, (card.usedLimit / card.creditLimit) * 100) : null;
  const FaceTag = onClick ? "button" : "div";
  const gradient = useMemo(
    () =>
      getCardGradient({
        name: card.name,
        productType: card.productType,
        institutionColor: card.institutionColor,
      }),
    [card.name, card.productType, card.institutionColor],
  );
  const faceStyle = useMemo(
    () => ({
      "--card-light": gradient.light,
      "--card-base": gradient.base,
      "--card-dark": gradient.dark,
      "--card-text": gradient.text,
      "--card-text-muted": gradient.textMuted,
      "--card-text-shadow-large": gradient.textShadowLarge,
      "--card-text-shadow-small": gradient.textShadowSmall,
      "--card-gloss": gradient.gloss,
      "--card-edge": gradient.edge,
      "--card-sheen": gradient.sheen,
      "--card-shade": gradient.shade,
      "--card-footer": gradient.footer,
      "--card-noise": `var(--card-noise-${gradient.noise})`,
    }),
    [gradient],
  );

  return (
    <div className="credit-card-visual">
      <FaceTag
        type={onClick ? "button" : undefined}
        className={`credit-card-visual__face ${onClick ? "credit-card-visual__face--interactive" : ""}`}
        style={faceStyle}
        onClick={onClick}
      >
        <div className="credit-card-visual__top">
          <span className={`credit-card-visual__chip credit-card-visual__chip--${gradient.chip}`} aria-hidden="true" />
          <div className="credit-card-visual__top-right">
            <span className="credit-card-visual__brand">
              {card.brand || (card.isSynced ? "Sincronizado via Polp" : "Cartão de crédito")}
            </span>
            {onSetPrimary && (
              <button
                type="button"
                className={`credit-card-visual__star ${card.isPrimary ? "credit-card-visual__star--active" : ""}`}
                onClick={card.isPrimary ? undefined : onSetPrimary}
                aria-pressed={card.isPrimary}
                aria-label={card.isPrimary ? "Cartão principal" : "Definir como cartão principal"}
                title={card.isPrimary ? "Cartão principal" : "Definir como cartão principal"}
              >
                <StarIcon filled={card.isPrimary} />
              </button>
            )}
          </div>
        </div>

        {card.number && <span className="credit-card-visual__number">{card.number}</span>}

        <span className="credit-card-visual__name">{card.name}</span>

        <div className="credit-card-visual__bottom">
          <span className="credit-card-visual__holder">{cardholderName || "Titular"}</span>
          <span className="credit-card-visual__due">{card.dueDay ? `Vence dia ${card.dueDay}` : "Vencimento não informado"}</span>
        </div>
      </FaceTag>

      <div className="credit-card-visual__stats">
        <div className="credit-card-visual__stat">
          <span className="credit-card-visual__stat-label">Fatura atual</span>
          <span className="credit-card-visual__stat-value">
            {card.currentInvoice != null ? formatCurrency(card.currentInvoice) : "—"}
          </span>
        </div>
        <div className="credit-card-visual__stat">
          <span className="credit-card-visual__stat-label">Próximo vencimento</span>
          <span className="credit-card-visual__stat-value">
            {card.invoiceDueDate ? formatDate(card.invoiceDueDate) : "—"}
          </span>
        </div>
        <div className="credit-card-visual__stat">
          <span className="credit-card-visual__stat-label">Parcelas ativas</span>
          <span className="credit-card-visual__stat-value">{card.installments ? card.installments.length : "—"}</span>
        </div>

        {hasLimit ? (
          <div className="credit-card-visual__limit">
            <div className="credit-card-visual__limit-labels">
              <span>
                Limite usado: <strong>{formatCurrency(card.usedLimit)}</strong> de {formatCurrency(card.creditLimit)}
              </span>
              {usagePercent != null && <span>{Math.round(usagePercent)}%</span>}
            </div>
            {usagePercent != null && (
              <div className="credit-card-visual__progress">
                <div className="credit-card-visual__progress-fill" style={{ width: `${usagePercent}%` }} />
              </div>
            )}
          </div>
        ) : (
          <div className="credit-card-visual__limit">
            <span className="credit-card-visual__limit-labels">
              <span>Limite não informado</span>
            </span>
          </div>
        )}
      </div>

      {onClick && (
        <span className="credit-card-visual__hint">
          <CardIcon /> Clique pra ver todos os seus cartões
        </span>
      )}
    </div>
  );
}
