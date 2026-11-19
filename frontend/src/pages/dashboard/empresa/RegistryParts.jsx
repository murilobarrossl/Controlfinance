import { formatCurrency } from "../../../utils/financeMath.js";

export function SummaryItem({ label, value }) {
  return (
    <div className="registry-summary__item">
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

export function StatusFilter({ options, value, onChange }) {
  return (
    <div className="registry__segmented" role="group" aria-label="Filtrar por situação">
      {options.map((opt) => (
        <button
          key={opt.value || "all"}
          type="button"
          aria-pressed={value === opt.value}
          className={value === opt.value ? "registry__segment registry__segment--active" : "registry__segment"}
          onClick={() => onChange(opt.value)}
        >
          {opt.label}
        </button>
      ))}
    </div>
  );
}

// "Pago" = decisão manual do mês ou pagamento identificado pelo banco; "não pago" não significa em atraso.
export function PaymentFilter({ label, options, value, onChange }) {
  return (
    <label className="registry__select">
      <span className="visually-hidden">{label}</span>
      <select value={value} onChange={(e) => onChange(e.target.value)}>
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </label>
  );
}

// Check verde para o estado final do mês. A origem fica no rótulo acessível e na dica: marcação
// manual nunca é apresentada como pagamento confirmado pelo banco.
const PAID_LABELS = {
  Bank: "Pago neste mês: identificado pelo banco",
  Manual: "Pago neste mês: marcado manualmente",
};

export function PaymentStatusMark({ status }) {
  const label = PAID_LABELS[status];
  if (!label) return null;
  return (
    <svg className="registry__paid" viewBox="0 0 16 16" width="14" height="14" role="img" aria-label={label}>
      <title>{label}</title>
      <path d="M3.5 8.5l3 3 6-7" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

// Valor pago no mês = só o que o banco identificou. Marcação manual nunca gera valor: aparece como
// não confirmado. Identificado mas desmarcado: valor esmaecido, com a origem na dica.
export function PaidAmountCell({ status, amount }) {
  if (amount != null) {
    const unmarked = status == null;
    return (
      <td
        className={`biz-table__numeric registry__paid-amount${unmarked ? " registry__paid-amount--unmarked" : ""}`}
        title={unmarked ? "Identificado pelo banco, desmarcado por você" : "Identificado pelo banco"}
      >
        {formatCurrency(amount)}
      </td>
    );
  }
  if (status === "Manual") {
    return <td className="biz-table__numeric registry__paid-amount registry__paid-amount--unconfirmed">Não confirmado pelo banco</td>;
  }
  return <td className="biz-table__numeric registry__muted">—</td>;
}
