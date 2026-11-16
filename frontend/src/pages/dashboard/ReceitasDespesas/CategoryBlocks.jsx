import { transactionTone } from "../../../utils/financialTone.js";
import { Link } from "react-router";
import TransactionDetails from "../../../components/dashboard/TransactionDetails/TransactionDetails.jsx";
import { ChevronDownIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatDate, formatPercentage } from "../../../utils/financeMath.js";

// A Polp manda a descrição de transferências como "Transferência Recebida|Nome do titular";
// mesma leitura de Relatórios, para a linha não mostrar o texto cru.
function parseDescription(name) {
  const pipeIndex = name.indexOf("|");
  if (pipeIndex === -1) return { primary: name, secondary: null };
  return { primary: name.slice(0, pipeIndex).trim(), secondary: name.slice(pipeIndex + 1).trim() };
}

function formatSigned(value, negative) {
  return `${negative ? "−" : "+"}${formatCurrency(Math.abs(value))}`;
}

// O bloco inteiro expande os lançamentos; "Ver categoria" fica fora do botão (HTML não permite
// link dentro de botão), sobreposto no canto, para que um clique nunca dispare as duas ações.
export default function CategoryBlocks({ data, tone, expandedNames, onToggle, detailsQuery, categoryHref, onCategoryOpen }) {
  const isExpense = tone === "expense";

  return (
    <ul className={`rd-cats rd-cats--${tone}`}>
      {data.map((item) => {
        const isExpanded = expandedNames.has(item.name);
        const panelId = `rd-${tone}-${encodeURIComponent(item.name)}`;
        const href = categoryHref?.(item.name);
        return (
          <li key={item.name} className={`rd-cat${isExpanded ? " rd-cat--open" : ""}${href ? " rd-cat--linked" : ""}`}>
            <div className="rd-cat__summary">
              <button
                type="button"
                className="rd-cat__toggle"
                onClick={() => onToggle(item.name)}
                aria-expanded={isExpanded}
                aria-controls={isExpanded ? panelId : undefined}
              >
                <span className="rd-cat__head">
                  <span className="rd-cat__name">{item.name}</span>
                  <span className={`rd-cat__value rd-cat__value--${transactionTone(isExpense ? "Expense" : "Income", item.value)}`}>{formatCurrency(item.value)}</span>
                  <span className="rd-cat__chevron" aria-hidden="true">
                    <ChevronDownIcon />
                  </span>
                </span>
                <span className="rd-cat__track" aria-hidden="true">
                  <span className="rd-cat__fill" style={{ width: `${Math.min(100, item.percentage)}%` }} />
                </span>
                <span className="rd-cat__meta">
                  <span>{formatPercentage(item.percentage)} do total</span>
                  <span className="rd-cat__dot" aria-hidden="true" />
                  <span>
                    {item.count} lançamento{item.count === 1 ? "" : "s"}
                  </span>
                </span>
              </button>

              {href && (
                <Link to={href} className="rd-cat__link" onClick={onCategoryOpen ? () => onCategoryOpen(item.name) : undefined}>
                  Ver categoria
                  <span className="rd-cat__link-icon" aria-hidden="true">
                    <ChevronDownIcon />
                  </span>
                </Link>
              )}
            </div>

            {isExpanded && (
              <div id={panelId} className="rd-cat__panel">
                <TransactionDetails
                  className="rd-tx"
                  query={{ ...detailsQuery, category: item.name }}
                  renderItem={(t) => {
                    const { primary, secondary } = parseDescription(t.name);
                    return (
                      <li key={t.id} className="rd-tx__item">
                        <span className="rd-tx__desc">
                          <span className="rd-tx__name">{primary}</span>
                          {secondary && <span className="rd-tx__secondary">{secondary}</span>}
                        </span>
                        <span className="rd-tx__date">{formatDate(t.dueDate)}</span>
                        <span className={`rd-tx__amount rd-tx__amount--${transactionTone(t.type, t.amount, t.isTransfer)}`}>
                          {formatSigned(t.amount, isExpense)}
                        </span>
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
