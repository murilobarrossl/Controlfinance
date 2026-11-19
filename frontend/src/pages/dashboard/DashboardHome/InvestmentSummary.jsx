import { dateLabel, formatMoney, normalizeInvestment } from "../../../utils/importedInvestments.js";

export default function InvestmentSummary({ data }) {
  if (!data.length) return <p className="dashboard-home__hint">Nenhum investimento sincronizado nesta seleção.</p>;

  return (
    <ul className="dh-mini__list dh-mini__investments">
      {data.map((resource) => {
        const item = normalizeInvestment(resource, resource.reserved, []);
        const amounts = item.amounts.filter(Boolean);
        const reference = item.referenceValue ? dateLabel(item.referenceValue) : null;
        return (
          <li key={item.key} className="dh-mini__row dh-mini__investment">
            <div>
              <strong>{item.name}</strong>
              <small>{[item.modality.label, resource.institutionName || "Instituição não informada"].filter(Boolean).join(" · ")}</small>
            </div>
            <div className="dh-mini__amount">
              {amounts.length ? <>
                {amounts.map((value, index) => <span key={index}>{formatMoney(value)}</span>)}
                <small>{item.balanceLabel}{reference && ` · ${reference}`}</small>
              </> : <small>Saldo não informado</small>}
            </div>
          </li>
        );
      })}
    </ul>
  );
}
