import { transactionTone } from "../../../utils/financialTone.js";
import { useEffect, useState } from "react";
import { Link } from "react-router";
import { getRecentTransactions } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import CreditCardVisual from "../../../components/dashboard/CreditCardVisual/CreditCardVisual.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";

const RECENT_LIMIT = 5;
const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };

function formatShortDate(value) {
  const date = new Date(value);
  if (!value || Number.isNaN(date.getTime())) return "—";
  const sameYear = date.getUTCFullYear() === new Date().getUTCFullYear();
  return date
    .toLocaleDateString("pt-BR", {
      day: "2-digit",
      month: "short",
      year: sameYear ? undefined : "numeric",
      timeZone: "UTC",
    })
    .replace(/\./g, "")
    .replace(/ de /g, " ");
}

function formatSyncedAt(value) {
  if (!value || Number.isNaN(new Date(value).getTime())) return null;
  return new Date(value).toLocaleString("pt-BR", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function RecentTransactions({ card, refreshKey }) {
  const requestKey = `${card.id}|${refreshKey}`;
  const [result, setResult] = useState(null);
  const loading = result?.key !== requestKey;

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    getRecentTransactions({ bankAccountId: card.id, limit: RECENT_LIMIT }, { signal })
      .then((items) => {
        if (!signal.aborted) setResult({ key: requestKey, items: items ?? [], error: "" });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ key: requestKey, items: [], error: err.message || "Não foi possível carregar os lançamentos." });
      });
    return () => controller.abort();
  }, [card.id, requestKey]);

  if (loading) {
    return (
      <ul className="card-overview__rows" aria-busy="true">
        {Array.from({ length: RECENT_LIMIT }, (_, i) => (
          <li key={i} className="card-overview__row">
            <Skeleton width="3rem" />
            <Skeleton width="70%" />
            <Skeleton width="4.5rem" />
          </li>
        ))}
      </ul>
    );
  }

  if (result.error) {
    return (
      <p className="card-overview__error" role="alert">
        {result.error}
      </p>
    );
  }

  if (result.items.length === 0) {
    return <p className="card-overview__hint">Nenhum lançamento neste cartão.</p>;
  }

  return (
    <ul className="card-overview__rows">
      {result.items.map((t) => {
        const isIncome = t.type === "Income";
        return (
          <li key={t.id} className="card-overview__row">
            <time className="card-overview__date" dateTime={String(t.dueDate).slice(0, 10)}>
              {formatShortDate(t.dueDate)}
            </time>
            <span className="card-overview__info">
              <span className="card-overview__name" title={t.name}>
                {t.name}
              </span>
              <span className="card-overview__meta">
                {t.categoryName || "Sem categoria"}
                {t.isTransfer && " · Transferência"}
              </span>
            </span>
            <span className="card-overview__end">
              <span className={`card-overview__amount card-overview__amount--${transactionTone(t.type, t.amount, t.isTransfer)}`}>
                {isIncome ? "+ " : "− "}
                {formatCurrency(t.amount)}
              </span>
              {t.status !== "Paid" && (
                <StatusPill status={t.status} pendingTone="neutral">{STATUS_LABELS[t.status] || t.status}</StatusPill>
              )}
            </span>
          </li>
        );
      })}
    </ul>
  );
}

export default function CardOverview({ card, cardholderName, onOpenCards, refreshKey }) {
  const syncedAt = formatSyncedAt(card.connectionLastSyncedAt);

  return (
    <div className="card-overview">
      <div className="card-overview__wallet">
        <CreditCardVisual card={card} cardholderName={cardholderName} onClick={onOpenCards} />
      </div>

      <section className="card-overview__section card-overview__section--recent" aria-labelledby="card-overview-recent">
        <header className="card-overview__head">
          <h4 id="card-overview-recent" className="card-overview__title">
            Últimos lançamentos
          </h4>
          {syncedAt && <span className="card-overview__note">Banco sincronizado em {syncedAt}</span>}
        </header>
        <RecentTransactions card={card} refreshKey={refreshKey} />
        <Link to="/dashboard/cartoes" className="card-overview__link">
          Ver extrato completo
        </Link>
      </section>
    </div>
  );
}
