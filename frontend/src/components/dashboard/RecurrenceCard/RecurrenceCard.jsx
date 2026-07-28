import { formatCurrency } from "../../../utils/financeMath.js";
import "./RecurrenceCard.css";

// "Há N meses" é só um rótulo aproximado pro olho humano — o valor em R$ que importa
// (totalSinceFirst) vem pronto do backend, nunca recalculado aqui (currentAmount × meses erraria
// toda vez que o valor mudou no meio do caminho, o próprio caso do "aumento silencioso").
function monthsSince(dateStr) {
  const first = new Date(dateStr);
  const now = new Date();
  const months = (now.getFullYear() - first.getFullYear()) * 12 + (now.getMonth() - first.getMonth());
  return Math.max(0, months);
}

export default function RecurrenceCard({ recurrence, onDecision }) {
  const {
    displayName,
    frequency,
    currentAmount,
    annualCost,
    nextExpectedDate,
    daysUntilNextCharge,
    totalSinceFirst,
    firstOccurrence,
    occurrenceCount,
    silentIncrease,
    isNew,
    annualFeeUpcoming,
    isManuallyConfirmed,
    reminderRequested,
  } = recurrence;

  const months = monthsSince(firstOccurrence);
  const nextDateLabel = new Date(nextExpectedDate).toLocaleDateString("pt-BR");

  return (
    <li className="recurrence-card">
      <div className="recurrence-card__top">
        <div className="recurrence-card__identity">
          <span className="recurrence-card__name">{displayName}</span>
          {(silentIncrease || isNew || annualFeeUpcoming) && (
            <div className="recurrence-card__badges">
              {silentIncrease && <span className="recurrence-card__badge recurrence-card__badge--warning">Aumento</span>}
              {isNew && <span className="recurrence-card__badge recurrence-card__badge--info">Nova</span>}
              {annualFeeUpcoming && (
                <span className="recurrence-card__badge recurrence-card__badge--warning">
                  Anuidade em {daysUntilNextCharge}d
                </span>
              )}
            </div>
          )}
        </div>

        <div className="recurrence-card__amounts">
          {frequency === "Monthly" && (
            <span className="recurrence-card__monthly">{formatCurrency(currentAmount)}/mês</span>
          )}
          <span className="recurrence-card__annual">{formatCurrency(annualCost)}/ano</span>
        </div>
      </div>

      <p className="recurrence-card__summary">
        {displayName} há {months} {months === 1 ? "mês" : "meses"} — já gastou{" "}
        <strong>{formatCurrency(totalSinceFirst)}</strong> em {occurrenceCount} cobranças. Próxima cobrança esperada em{" "}
        {nextDateLabel}.
      </p>

      <div className="recurrence-card__actions">
        <div className="recurrence-card__decision">
          <span className="recurrence-card__decision-label">Isso é uma assinatura?</span>
          <div className="recurrence-card__toggle">
            <button
              type="button"
              className={`recurrence-card__toggle-btn ${isManuallyConfirmed ? "recurrence-card__toggle-btn--active" : ""}`}
              onClick={() => onDecision({ status: "Confirmed" })}
            >
              Sim
            </button>
            <button type="button" className="recurrence-card__toggle-btn" onClick={() => onDecision({ status: "Dismissed" })}>
              Não
            </button>
          </div>
        </div>

        <button
          type="button"
          className={`recurrence-card__reminder ${reminderRequested ? "recurrence-card__reminder--active" : ""}`}
          onClick={() => onDecision({ reminderRequested: !reminderRequested })}
        >
          {reminderRequested ? "✓ Lembrete ativado" : "Quero cancelar isso"}
        </button>
      </div>

      {reminderRequested && (
        <p className="recurrence-card__reminder-note">
          Avisamos antes da cobrança de {formatCurrency(currentAmount)} em {nextDateLabel}, pra você decidir com tempo. O
          app não cancela por você.
        </p>
      )}
    </li>
  );
}
