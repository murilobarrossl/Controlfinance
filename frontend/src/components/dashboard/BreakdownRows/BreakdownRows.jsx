import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import "./BreakdownRows.css";

// Lista de decomposição reutilizada por Receitas e despesas (fontes de receita / categorias de
// despesa) e por Categorias (categorias / estabelecimentos dentro de uma categoria): cada linha
// mostra nome, valor, barra proporcional, % do total e nº de lançamentos. `tone` define a cor da
// barra quando a linha não traz sua própria `color` (ex.: cor cadastrada da categoria).
export default function BreakdownRows({
  data,
  tone = "expense",
  activeName,
  onRowClick,
  expandedNames,
  renderExpanded,
  showLinkHint = false,
}) {
  return (
    <ul className="breakdown-rows">
      {data.map((item) => {
        const isExpanded = expandedNames?.has(item.name);
        const isActive = activeName === item.name;
        return (
          <li key={item.name} className="breakdown-rows__item">
            <button
              type="button"
              className={`breakdown-rows__row breakdown-rows__row--${tone}${
                isActive ? " breakdown-rows__row--active" : ""
              }`}
              onClick={() => onRowClick(item.name)}
              aria-expanded={renderExpanded ? isExpanded : undefined}
            >
              <div className="breakdown-rows__row-top">
                <span className="breakdown-rows__name">{item.name}</span>
                <span className="breakdown-rows__value">{formatCurrency(item.value)}</span>
              </div>
              <div className="breakdown-rows__bar-track">
                <div
                  className="breakdown-rows__bar-fill"
                  style={{ width: `${Math.min(100, item.percentage)}%`, backgroundColor: item.color }}
                />
              </div>
              <div className="breakdown-rows__row-meta">
                <span>{formatPercentage(item.percentage)} do total</span>
                <span>
                  {item.count} lançamento{item.count === 1 ? "" : "s"}
                </span>
                {showLinkHint && <span className="breakdown-rows__row-link">Ver categoria ›</span>}
              </div>
            </button>
            {renderExpanded && isExpanded && <div className="breakdown-rows__expanded">{renderExpanded(item.name)}</div>}
          </li>
        );
      })}
    </ul>
  );
}
