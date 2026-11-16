import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import LimitBar from "../../../components/dashboard/LimitBar/LimitBar.jsx";
import { formatCurrency, formatDate, formatPercentage } from "../../../utils/financeMath.js";
import { formatDueDay } from "../../../utils/invoiceDue.js";
import { cardLimits, isInvoiceOverdue } from "../../../utils/cardLimits.js";

const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };

function formatDateTime(value) {
  return new Date(value).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });
}

function cycleNote(card) {
  if (card.closingDay && card.dueDay) return `Fecha no dia ${card.closingDay} e vence no dia ${card.dueDay} de cada mês`;
  if (card.dueDay) return `Vence no dia ${card.dueDay} de cada mês`;
  if (card.closingDay) return `Fecha no dia ${card.closingDay} de cada mês`;
  return null;
}

export default function CardDetail({ id, card, bankName }) {
  const { hasLimit, available, usagePercent, overLimit, overAmount } = cardLimits(card);
  const pastDue = isInvoiceOverdue(card);
  const cycle = cycleNote(card);
  const lastDigits = card.number ? String(card.number).replace(/\D/g, "").slice(-4) : null;
  const identity = [bankName, lastDigits && `final ${lastDigits}`].filter(Boolean).join(" · ");
  const titleId = `${id}-titulo`;

  return (
    <li id={id} className="cartoes__detail" aria-labelledby={titleId}>
      <header className="cartoes__detail-head">
        <div className="cartoes__detail-identity">
          <h3 id={titleId} className="cartoes__detail-title">{card.name}</h3>
          {identity && <p className="cartoes__detail-sub">{identity}</p>}
        </div>
        <p className="cartoes__detail-updated">
          {card.connectionLastSyncedAt
            ? `Atualizado em ${formatDateTime(card.connectionLastSyncedAt)}`
            : "Sem data de atualização"}
        </p>
      </header>

      <div className="cartoes__detail-body">
        <div className="cartoes__detail-group">
          <dl className="cartoes__detail-figure cartoes__detail-figure--lead">
            <dt>Fatura atual</dt>
            <dd className={pastDue ? "cartoes__detail-value--overdue" : undefined}>{card.currentInvoice != null ? formatCurrency(card.currentInvoice) : "—"}</dd>
          </dl>
          <dl className="cartoes__detail-figure">
            <dt>Próximo vencimento</dt>
            <dd className="cartoes__detail-due">
              <span className={pastDue ? "cartoes__detail-value--overdue" : undefined}>
                {!card.invoiceDueDate ? "—" : pastDue ? formatDueDay(card.invoiceDueDate) : formatDate(card.invoiceDueDate)}
              </span>
              {card.invoiceStatus && (
                <StatusPill status={card.invoiceStatus} pendingTone="neutral">{STATUS_LABELS[card.invoiceStatus] || card.invoiceStatus}</StatusPill>
              )}
            </dd>
          </dl>
          {cycle && <p className="cartoes__detail-note">{cycle}</p>}
        </div>

        <div className="cartoes__detail-group">
          <dl className="cartoes__detail-figure cartoes__detail-figure--lead">
            <dt>Limite usado</dt>
            <dd className={overLimit ? "cartoes__detail-value--overdue" : undefined}>
              {card.usedLimit != null ? formatCurrency(card.usedLimit) : "—"}
              {hasLimit && <span className="cartoes__detail-of"> de {formatCurrency(card.creditLimit)}</span>}
            </dd>
          </dl>
          {hasLimit ? (
            <div className="cartoes__detail-meter">
              <LimitBar usagePercent={usagePercent} />
              {usagePercent != null && (
                <p className={`cartoes__detail-note${overLimit ? " cartoes__detail-note--over" : ""}`}>
                  {overLimit
                    ? `Excedido em ${formatCurrency(overAmount)} · ${formatPercentage(usagePercent)} do limite`
                    : `${formatPercentage(usagePercent)} do limite usado`}
                </p>
              )}
            </div>
          ) : (
            <p className="cartoes__detail-note">Limite não informado pelo banco</p>
          )}
          <dl className="cartoes__detail-figure">
            <dt>Limite disponível</dt>
            <dd>{available != null ? formatCurrency(available) : "—"}</dd>
          </dl>
        </div>
      </div>
    </li>
  );
}
