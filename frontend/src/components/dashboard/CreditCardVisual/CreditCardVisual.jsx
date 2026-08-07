import { CardIcon, SyncIcon } from "../../ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import "./CreditCardVisual.css";

// Não existe número de cartão (nem validade) em lugar nenhum do sistema hoje — nem nas contas
// sincronizadas da Polp, nem no cadastro manual de cartão. Em vez de inventar dígitos, a faixa
// onde um cartão de verdade mostraria o número vira só o chip decorativo + nome/bandeira, e a
// faixa de "validade" mostra o dia de vencimento por extenso ("Vence dia 10"), que não tem como
// ser confundido com uma validade MM/AA forjada.
export default function CreditCardVisual({
  card,
  cardholderName,
  currentInvoice,
  invoiceDueDate,
  activeInstallmentsCount = 0,
  linkedAccountName,
  onClick,
}) {
  const usagePercent = card.creditLimit > 0 ? Math.min(100, (card.usedLimit / card.creditLimit) * 100) : 0;
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
          <span className="credit-card-visual__brand">{card.brand || "Cartão de crédito"}</span>
        </div>

        <span className="credit-card-visual__name">{card.name}</span>

        <div className="credit-card-visual__bottom">
          <span className="credit-card-visual__holder">{cardholderName || "Titular"}</span>
          <span className="credit-card-visual__due">Vence dia {card.dueDay}</span>
        </div>
      </FaceTag>

      <div className="credit-card-visual__stats">
        <div className="credit-card-visual__stat">
          <span className="credit-card-visual__stat-label">Fatura atual</span>
          <span className="credit-card-visual__stat-value">{formatCurrency(currentInvoice)}</span>
        </div>
        <div className="credit-card-visual__stat">
          <span className="credit-card-visual__stat-label">Próximo vencimento</span>
          <span className="credit-card-visual__stat-value">{formatDate(invoiceDueDate)}</span>
        </div>
        <div className="credit-card-visual__stat">
          <span className="credit-card-visual__stat-label">Parcelas ativas</span>
          <span className="credit-card-visual__stat-value">{activeInstallmentsCount}</span>
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

      {linkedAccountName && (
        <span className="credit-card-visual__synced">
          <SyncIcon /> Sincronizado com {linkedAccountName}
        </span>
      )}

      {onClick && (
        <span className="credit-card-visual__hint">
          <CardIcon /> Clique pra ver todos os seus cartões
        </span>
      )}
    </div>
  );
}
