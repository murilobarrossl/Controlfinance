import { Link } from "react-router";
import MerchantLogo from "../../../components/ui/MerchantLogo/MerchantLogo.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import { transactionTone } from "../../../utils/financialTone.js";

const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };

export default function CardTransactionRow({ transaction: t, meta, onOpen, fromCardCategories = false }) {
  return (
    <li>
      <Link
        to={`/dashboard/cartoes/lancamentos/${t.id}`}
        state={{ fromCardStatement: true, fromCardCategories }}
        onClick={onOpen}
        className="cartoes__row cartoes__row--statement cartoes__statement-link"
        aria-label={`Ver detalhes de ${t.name}`}
      >
        <span className="cartoes__statement-description">
          <MerchantLogo name={t.name} isTransfer={t.isTransfer} />
          <span className="cartoes__row-info">
            <span className="cartoes__row-name" title={t.name}>
              {t.name}
            </span>
            <span className="cartoes__row-meta">
              {meta ?? (t.categoryName || "Sem categoria")}
              {t.isTransfer && " · Transferência"}
            </span>
          </span>
        </span>
        <span className="cartoes__row-end">
          {t.status !== "Paid" && (
            <StatusPill status={t.status} pendingTone="neutral">{STATUS_LABELS[t.status] || t.status}</StatusPill>
          )}
          <span className={`cartoes__row-amount cartoes__row-amount--${transactionTone(t.type, t.amount, t.isTransfer)}`}>
            {t.type === "Income" ? "+" : "\u2212"}
            {formatCurrency(t.amount)}
          </span>
        </span>
      </Link>
    </li>
  );
}
