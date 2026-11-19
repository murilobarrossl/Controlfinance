import { useEffect, useMemo, useState } from "react";
import { useLocation, useSearchParams } from "react-router";
import { getCardCategoryExpenses } from "../../../api/creditCards.js";
import Pager from "../../../components/ui/Pager/Pager.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import CardPicker from "../../../components/dashboard/CardPicker/CardPicker.jsx";
import MonthNavigator from "../../../components/ui/MonthNavigator/MonthNavigator.jsx";
import useMonthPeriod, { periodParams } from "../../../hooks/useMonthPeriod.js";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { CategoryList } from "../Categorias/CategoryRows.jsx";
import { cardPickerOption } from "../../../utils/cardBrand.js";
import { donutSliceColors } from "../../../utils/categoryColors.js";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import CardTransactionRow from "./CardTransactionRow.jsx";
import "../Categorias/Categorias.css";
import "./CardCategories.css";

function useExpenses(query, refreshKey) {
  const key = query && JSON.stringify([query, refreshKey]);
  const [result, setResult] = useState(null);
  useEffect(() => {
    if (!key) return;
    const controller = new AbortController();
    const { signal } = controller;
    const [filters] = JSON.parse(key);
    getCardCategoryExpenses(filters, { signal })
      .then((data) => {
        if (!signal.aborted) setResult({ key, data });
      })
      .catch((err) => {
        if (!signal.aborted) setResult({ key, error: err.message || "Não foi possível carregar as despesas." });
      });
    return () => controller.abort();
  }, [key]);
  return result?.key === key ? result : null;
}

export default function CardCategories({ cards, brandOf }) {
  const [params, setParams] = useSearchParams();
  const location = useLocation();
  const { dataRefreshKey } = useAccount();
  const cardId = params.get("cartao") || "";
  const selected = params.get("categoria");
  const period = useMonthPeriod(params.get("mes"), params.get("periodo"), "2000-01");
  const available = cardId ? cards.some((card) => card.id === cardId) : cards.length > 0;
  const filters = { month: period.range.from, annual: period.annual, cardId };
  const summary = useExpenses(available ? filters : null, dataRefreshKey);
  const ready = useMemo(() => {
    if (!summary?.data) return summary;
    const total = summary.data.reduce((sum, c) => sum + Math.round(c.value * 100), 0) / 100;
    const colors = donutSliceColors(summary.data);
    const categories = summary.data.map((c, i) => ({
      ...c, color: colors[i], percentage: total ? c.value / total * 100 : 0,
    }));
    return { total, categories };
  }, [summary]);
  // Uma seleção vazia indica que o usuário recolheu a categoria manualmente.
  const active = selected === "" ? null
    : ready?.categories?.find((c) => c.name === selected) ?? ready?.categories?.[0];
  const activeName = active?.name;

  useEffect(() => {
    if (!activeName || activeName === selected) return;
    const next = new URLSearchParams(params);
    next.set("mes", period.key);
    next.set("categoria", activeName);
    setParams(next, { replace: true, state: location.state });
  }, [activeName, selected, params, setParams, location.state, period.key]);
  const requested = Number(params.get("pagina"));
  const page = Number.isInteger(requested) && requested > 0 && requested <= 2147483647 ? requested : 1;
  const details = useExpenses(active ? { ...filters, category: activeName, page } : null, dataRefreshKey);
  const pageData = details?.data;

  function changePage(value) {
    const next = new URLSearchParams(params);
    next.set("pagina", String(value));
    setParams(next, { replace: true, state: location.state });
  }

  function applyPeriodOrCard(next) {
    next.delete("categoria");
    next.delete("pagina");
    setParams(next, { replace: true, state: location.state });
  }

  function changePeriod(key, mode) {
    applyPeriodOrCard(periodParams(params, key, mode));
  }

  function changeCard(id) {
    const next = new URLSearchParams(params);
    if (id) next.set("cartao", id);
    else next.delete("cartao");
    applyPeriodOrCard(next);
  }

  function toggle(name) {
    const next = new URLSearchParams(params);
    next.set("categoria", activeName === name ? "" : name);
    next.delete("pagina");
    setParams(next, { replace: true, state: location.state });
  }

  const options = cards.map((card) => {
    const option = cardPickerOption(card, brandOf(card));
    const digits = card.number && String(card.number).replace(/\D/g, "").slice(-4);
    return { ...option, subtitle: [option.subtitle, digits && `final ${digits}`].filter(Boolean).join(" · ") };
  });
  const labels = options.map((o) => o.title + " · " + o.subtitle);
  options.forEach((option, index) => {
    if (labels.indexOf(labels[index]) !== labels.lastIndexOf(labels[index])) {
      option.subtitle += ` · Cartão ${index + 1}`;
    }
  });
  if (cardId && !available) options.unshift({ value: cardId, title: "Cartão indisponível" });

  if (!cards.length) return <p className="cartoes__hint">Nenhum cartão sincronizado.</p>;
  return (
    <section className="categorias card-categories" aria-label="Categorias dos cartões">
      <div className="categorias__period">
        <CardPicker id="categorias-cartao" ariaLabel="Filtrar por cartão" value={cardId}
          options={[{ value: "", title: "Todos os cartões" }, ...options]}
          onChange={changeCard} />
        <MonthNavigator period={period} onChange={changePeriod} />
      </div>
      {!available ? <p className="cartoes__hint">Cartão indisponível neste perfil.</p>
        : !ready ? <Skeleton width="100%" height="12rem" />
        : ready.error ? <p className="cartoes__error" role="alert">{ready.error}</p>
        : !ready.categories.length ? <p className="cartoes__hint">Nenhuma despesa em {period.label.toLowerCase()}.</p>
        : <>
          <div className="card-categories__distribution">
            <div className="card-categories__chart">
              <CategoryDonutChart height={224} data={ready.categories} showLegend={false} formatValue={formatCurrency}
                centerLabel={formatCurrency(ready.total)} onSliceClick={toggle} />
            </div>
            <CategoryList data={ready.categories} activeName={activeName} onSelect={toggle} />
          </div>
          {active && <section className="categorias__section" aria-label={active.name}>
            <header className="categorias__section-head">
              <h2 className="categorias__section-title">{active.name}</h2>
              <strong>{formatCurrency(active.value)}</strong>
            </header>
            {details?.error ? <p className="cartoes__error" role="alert">{details.error}</p>
              : !pageData ? <Skeleton width="100%" height="6rem" />
              : <>
                <ul className="cartoes__rows">
                  {pageData.items.map((t) => {
                    const option = options.find((o) => o.value === t.bankAccountId);
                    const meta = [formatDate(t.dueDate), option?.title, option?.subtitle].filter(Boolean).join(" · ");
                    return <CardTransactionRow key={t.id} transaction={t} meta={meta} fromCardCategories />;
                  })}
                </ul>
                <Pager page={pageData.page} totalPages={pageData.totalPages} totalCount={pageData.totalCount}
                  countLabel={pageData.totalCount === 1 ? "lançamento" : "lançamentos"}
                  onChange={changePage} label="Páginas dos lançamentos da categoria" />
            </>}
          </section>}
        </>}
    </section>
  );
}
