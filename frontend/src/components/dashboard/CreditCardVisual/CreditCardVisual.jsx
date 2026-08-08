import { CardIcon } from "../../ui/icons/FeatureIcons.jsx";
import Button from "../../ui/Button/Button.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import "./CreditCardVisual.css";

// `card` é o RecognizedCardDto do backend, já achatado — sem entidade separada por trás: quando
// `isSynced` é true, isto É a BankAccount sincronizada da Polp, não um cadastro à parte. Campos
// que a Polp não mandou (creditLimit/closingDay/dueDay) vêm null; mostra só o que faltar, em vez
// de um bloco genérico "complete tudo". Sem número de cartão fabricado — `number` só aparece
// quando a própria Polp manda um (mascarado); do contrário fica só o chip decorativo.
export default function CreditCardVisual({ card, cardholderName, onClick, onCompleteFields }) {
  const usagePercent =
    card.creditLimit && card.creditLimit > 0 ? Math.min(100, ((card.usedLimit ?? 0) / card.creditLimit) * 100) : null;
  const FaceTag = onClick ? "button" : "div";
  const missingFields = [
    !card.creditLimit && "limite",
    !card.closingDay && "dia de fechamento",
    !card.dueDay && "dia de vencimento",
  ].filter(Boolean);

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
            {card.brand || (card.isSynced ? "Sincronizado via Polp" : "Cartão de crédito")}
          </span>
        </div>

        {card.number && <span className="credit-card-visual__number">{card.number}</span>}

        <span className="credit-card-visual__name">{card.name}</span>

        <div className="credit-card-visual__bottom">
          <span className="credit-card-visual__holder">{cardholderName || "Titular"}</span>
          <span className="credit-card-visual__due">{card.dueDay ? `Vence dia ${card.dueDay}` : "Vencimento a completar"}</span>
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
          <span className="credit-card-visual__stat-value">{card.installments?.length ?? 0}</span>
        </div>

        {usagePercent != null ? (
          <div className="credit-card-visual__limit">
            <div className="credit-card-visual__limit-labels">
              <span>
                Limite usado: <strong>{formatCurrency(card.usedLimit ?? 0)}</strong> de {formatCurrency(card.creditLimit)}
              </span>
              <span>{Math.round(usagePercent)}%</span>
            </div>
            <div className="credit-card-visual__progress">
              <div className="credit-card-visual__progress-fill" style={{ width: `${usagePercent}%` }} />
            </div>
          </div>
        ) : (
          <div className="credit-card-visual__limit">
            <span className="credit-card-visual__limit-labels">
              <span>Limite não informado</span>
            </span>
          </div>
        )}
      </div>

      {missingFields.length > 0 && (
        <div className="credit-card-visual__incomplete">
          <p>
            {card.isSynced ? "A Polp não mandou " : "Ainda falta "}
            {missingFields.join(", ")} desse cartão.
          </p>
          {onCompleteFields && (
            <Button as="button" type="button" variant="primary" size="sm" onClick={onCompleteFields}>
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
