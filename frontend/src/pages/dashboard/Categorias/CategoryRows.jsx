import TransactionDetails from "../../../components/dashboard/TransactionDetails/TransactionDetails.jsx";
import { ChevronDownIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatDate, formatPercentage } from "../../../utils/financeMath.js";

function countLabel(count) {
  return `${count} lançamento${count === 1 ? "" : "s"}`;
}

const INSTALLMENT_SUFFIX = /(\d{2}\/\d{2})$/;

function installmentLabel(name) {
  return name.match(INSTALLMENT_SUFFIX)?.[1] ?? null;
}

// Linhas de categorias. A barra usa a mesma cor da fatia na rosca para ligar as duas leituras.
export function CategoryList({ data, activeName, onSelect }) {
  return (
    <ul className="cat-list">
      {data.map((item) => {
        const isActive = activeName === item.name;
        return (
          <li key={item.name}>
            <button
              type="button"
              className={`cat-row${isActive ? " cat-row--active" : ""}`}
              onClick={() => onSelect(item.name)}
              aria-pressed={isActive}
            >
              <span className="cat-row__head">
                <span className="cat-row__name">{item.name}</span>
                <span className="cat-row__value">{formatCurrency(item.value)}</span>
              </span>
              <span className="cat-row__track" aria-hidden="true">
                <span
                  className="cat-row__fill"
                  style={{ width: `${Math.min(100, item.percentage)}%`, backgroundColor: item.color }}
                />
              </span>
              <span className="cat-row__meta">
                <span className="cat-row__pct">{formatPercentage(item.percentage)} do total</span>
                <span className="cat-row__dot" aria-hidden="true" />
                <span>{countLabel(item.count)}</span>
              </span>
            </button>
          </li>
        );
      })}
    </ul>
  );
}

// Estabelecimentos da categoria aberta: linhas com divisórias, sem caixa por item, e os lançamentos
// no mesmo desenho de Receitas e despesas.
export function EstablishmentList({ data, expandedNames, onToggle, detailsQuery }) {
  return (
    <ul className="est-list">
      {data.map((item) => {
        const isExpanded = expandedNames.has(item.name);
        const panelId = `est-${encodeURIComponent(item.name)}`;
        return (
          <li key={item.name} className={`est${isExpanded ? " est--open" : ""}`}>
            <button
              type="button"
              className="est__toggle"
              onClick={() => onToggle(item.name)}
              aria-expanded={isExpanded}
              aria-controls={isExpanded ? panelId : undefined}
            >
              <span className="est__head">
                <span className="est__name">
                  {item.name}
                  {item.isInstallmentGroup && <span className="est__tag">Parcelada</span>}
                </span>
                <span className="est__value">{formatCurrency(item.value)}</span>
                <span className="est__chevron" aria-hidden="true">
                  <ChevronDownIcon />
                </span>
              </span>
              <span className="est__sub">
                <span className="est__track" aria-hidden="true">
                  <span className="est__fill" style={{ width: `${Math.min(100, item.percentage)}%` }} />
                </span>
                <span className="est__meta">
                  {formatPercentage(item.percentage)} da categoria · {countLabel(item.count)}
                </span>
              </span>
            </button>

            {isExpanded && (
              <div id={panelId} className="est__panel">
                <TransactionDetails
                  className="est-tx"
                  query={{ ...detailsQuery, establishment: item.name }}
                  renderItem={(t) => {
                    const installment = installmentLabel(t.name);
                    return (
                      <li key={t.id} className="est-tx__item">
                        <span className="est-tx__date">{formatDate(t.dueDate)}</span>
                        <span className="est-tx__note">{installment ? `Parcela ${installment}` : ""}</span>
                        <span className="est-tx__amount">−{formatCurrency(Math.abs(t.amount))}</span>
                      </li>
                    );
                  }}
                />
              </div>
            )}
          </li>
        );
      })}
    </ul>
  );
}
