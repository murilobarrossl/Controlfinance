import { useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router";
import { getCategories } from "../../../api/categories.js";
import { getTransactionAggregates } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import NoBankAccount from "../../../components/dashboard/NoBankAccount/NoBankAccount.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import MonthNavigator from "../../../components/ui/MonthNavigator/MonthNavigator.jsx";
import useMonthPeriod, { periodParams } from "../../../hooks/useMonthPeriod.js";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import { CategoryList, EstablishmentList } from "./CategoryRows.jsx";
import { loadPlanning } from "../../../api/planning.js";
import { calculateCutImpact } from "../../../utils/cutImpact.js";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { donutSliceColors, resolveCategoryColor } from "../../../utils/categoryColors.js";
import "./Categorias.css";
import usePageTitle from "../../../hooks/usePageTitle.js";

function CategorySection({ title, action, children, className = "" }) {
  return (
    <section className={`categorias__section ${className}`}>
      <header className="categorias__section-head">
        <h2 className="categorias__section-title">{title}</h2>
        {action}
      </header>
      {children}
    </section>
  );
}

const EMPTY_LIST = [];

const CUT_RATE_OPTIONS = [0.1, 0.2, 0.3, 0.5];

// Pagina a lista à direita para manter a leitura próxima do gráfico de rosca.
const CATEGORY_PAGE_SIZE = 5;

const CATEGORY_SORT_OPTIONS = [
  { value: "value-desc", label: "Maior gasto" },
  { value: "value-asc", label: "Menor gasto" },
  { value: "count-desc", label: "Mais ativas" },
];

function cutImpactSentence(categoryName, categoryAmount, cutRate, impact) {
  return `Você gastou ${formatCurrency(categoryAmount)} em ${categoryName} neste período. Cortar ${formatPercentage(
    cutRate * 100
  )} libera ${formatCurrency(impact.cutAmount)}.`;
}

function topExpenseCategoryName(categories) {
  let topName = null;
  let topValue = -Infinity;
  for (const c of categories) {
    if (c.type !== "Expense") continue;
    const name = c.categoryName || "Sem categoria";
    const value = categories
      .filter((o) => o.type === "Expense" && (o.categoryName || "Sem categoria") === name)
      .reduce((sum, o) => sum + o.amount, 0);
    if (value > topValue) {
      topValue = value;
      topName = name;
    }
  }
  return topName;
}

export default function Categorias() {
  usePageTitle("Categorias");
  const [searchParams, setSearchParams] = useSearchParams();
  const [categoryFromLink] = useState(() => searchParams.get("categoria"));
  const { scopeKey, scopeQuery, dataRefreshKey, accountsStatus } = useAccount();
  const [result, setResult] = useState(null);
  const [aggregateResult, setAggregateResult] = useState(null);
  const period = useMonthPeriod(searchParams.get("mes"), searchParams.get("periodo"));
  const { range } = period;
  const requestKey = `${scopeKey}|${dataRefreshKey}`;
  const aggregateKey = `${requestKey}|${range.from}|${range.to}`;
  const loading = accountsStatus !== "ready" || result?.key !== requestKey || aggregateResult?.key !== aggregateKey;
  const categories = result?.categories ?? EMPTY_LIST;
  const aggregates = aggregateResult?.key === aggregateKey ? aggregateResult.aggregates : null;
  const error =
    accountsStatus === "error"
      ? "Não foi possível carregar as contas."
      : (result?.key === requestKey && result.error) || (aggregateResult?.key === aggregateKey && aggregateResult.error) || "";
  const [selectedCategory, setSelectedCategory] = useState(null);
  const [categorySort, setCategorySort] = useState("value-desc");
  const [categoryPage, setCategoryPage] = useState(1);
  const [expandedEstablishments, setExpandedEstablishments] = useState(() => new Set());
  const [renderedScopeKey, setRenderedScopeKey] = useState(scopeKey);
  if (renderedScopeKey !== scopeKey) {
    setRenderedScopeKey(scopeKey);
    if (renderedScopeKey) {
      setSelectedCategory(null);
      setCategoryPage(1);
    }
  }
  const [cutRate, setCutRate] = useState(0.3);
  const [planning, setPlanning] = useState(null);
  const drilldownRef = useRef(null);
  const skipNextScrollRef = useRef(false);
  // A pré-seleção de categoria (via link ou maior gasto do mês) só deve acontecer uma vez, na
  // primeira carga. Trocar de conta depois não pode reabrir/trocar a categoria que o usuário já
  // escolheu.
  const hasAppliedInitialSelectionRef = useRef(false);

  useEffect(() => {
    if (accountsStatus !== "ready" || !scopeKey) return;

    const controller = new AbortController();
    const { signal } = controller;
    const key = `${scopeKey}|${dataRefreshKey}`;

    getCategories({ signal })
      .then((categoriesData) => {
        if (signal.aborted) return;
        setResult({ key, categories: categoriesData, error: "" });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({
          key,
          categories: EMPTY_LIST,
          error: err.message || "Não foi possível carregar as categorias.",
        });
      });

    return () => controller.abort();
  }, [scopeKey, dataRefreshKey, accountsStatus]);

  useEffect(() => {
    if (accountsStatus !== "ready" || !scopeKey) return;

    const controller = new AbortController();
    const { signal } = controller;
    const key = `${scopeKey}|${dataRefreshKey}|${range.from}|${range.to}`;

    getTransactionAggregates(
      {
        ...scopeQuery,
        from: range.from,
        to: range.to,
      },
      { signal }
    )
      .then((data) => {
        if (signal.aborted) return;
        setAggregateResult({ key, aggregates: data, error: "" });

        if (hasAppliedInitialSelectionRef.current) return;
        hasAppliedInitialSelectionRef.current = true;

        // Chegando de um link da aba Receitas e despesas (?categoria=Nome): abre direto nela,
        // com scroll até a seção, em vez de pré-selecionar a maior categoria do ano.
        if (categoryFromLink) {
          setSelectedCategory(categoryFromLink);
          return;
        }

        // Pré-seleciona a categoria com maior gasto do mês atual, pra seção de transações já
        // vir aberta sem precisar de clique. Só nessa carga inicial: dali em diante o usuário
        // controla a seleção normalmente, inclusive desmarcando.
        const topName = topExpenseCategoryName(data.categories ?? []);
        if (topName) {
          skipNextScrollRef.current = true;
          setSelectedCategory(topName);
        }
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setAggregateResult({
          key,
          aggregates: null,
          error: err.message || "Não foi possível carregar as categorias.",
        });
      });

    return () => controller.abort();
  }, [scopeKey, scopeQuery, dataRefreshKey, range, categoryFromLink, accountsStatus]);

  const breakdown = useMemo(() => {
    const totals = new Map();

    for (const c of aggregates?.categories ?? []) {
      if (c.type !== "Expense") continue;
      const name = c.categoryName || "Sem categoria";
      const entry = totals.get(name) || { name, value: 0, count: 0 };
      entry.value += c.amount;
      entry.count += c.count;
      totals.set(name, entry);
    }

    const total = [...totals.values()].reduce((sum, e) => sum + e.value, 0);

    const sorted = [...totals.values()]
      .map((e) => ({
        ...e,
        color: resolveCategoryColor(e.name, categories),
        percentage: total > 0 ? (e.value / total) * 100 : 0,
      }))
      .sort((a, b) => b.value - a.value);

    // A cor da rosca vem da posição na ordenação (tons do laranja), então as barras da lista e o
    // resumo usam a mesma cor em vez da cor cadastrada da categoria.
    const shades = donutSliceColors(sorted);
    return sorted.map((e, index) => ({ ...e, color: shades[index] }));
  }, [aggregates, categories]);

  const totalExpense = aggregates?.totalExpense;

  const sortedBreakdown = useMemo(() => {
    const sorted = [...breakdown];
    switch (categorySort) {
      case "value-asc":
        return sorted.sort((a, b) => a.value - b.value || a.name.localeCompare(b.name, "pt-BR"));
      case "count-desc":
        return sorted.sort((a, b) => b.count - a.count || a.name.localeCompare(b.name, "pt-BR"));
      case "value-desc":
      default:
        return sorted.sort((a, b) => b.value - a.value || a.name.localeCompare(b.name, "pt-BR"));
    }
  }, [breakdown, categorySort]);

  const categoryTotalPages = Math.max(1, Math.ceil(sortedBreakdown.length / CATEGORY_PAGE_SIZE));
  // Clampa em vez de resetar por efeito: se a página atual ficar fora do intervalo (ex.: trocou
  // de ano e sobraram menos categorias), já cai numa página válida no próprio render.
  const categoryPageSafe = Math.min(categoryPage, categoryTotalPages);
  const pagedBreakdown = useMemo(
    () => sortedBreakdown.slice((categoryPageSafe - 1) * CATEGORY_PAGE_SIZE, categoryPageSafe * CATEGORY_PAGE_SIZE),
    [sortedBreakdown, categoryPageSafe]
  );

  const establishmentBreakdown = useMemo(() => {
    const items = (aggregates?.establishments ?? []).filter(
      (e) => e.type === "Expense" && (e.categoryName || "Sem categoria") === selectedCategory
    );
    const total = items.reduce((sum, e) => sum + e.amount, 0);
    return items
      .map((e) => ({
        name: e.key,
        value: e.amount,
        count: e.count,
        isInstallmentGroup: e.isInstallmentGroup === true,
        percentage: total > 0 ? (e.amount / total) * 100 : 0,
      }))
      .sort((a, b) => b.value - a.value);
  }, [aggregates, selectedCategory]);

  const selectedCategoryAmount = useMemo(
    () => breakdown.find((c) => c.name === selectedCategory)?.value ?? 0,
    [breakdown, selectedCategory]
  );

  const cutImpact = useMemo(() => {
    if (!planning || !selectedCategory || selectedCategoryAmount <= 0) return null;
    return calculateCutImpact({
      categoryAmount: selectedCategoryAmount,
      cutRate,
      reserveAmount: planning.reserve?.currentAmount ?? 0,
      goals: planning.goals ?? [],
    });
  }, [selectedCategory, selectedCategoryAmount, cutRate, planning]);

  useEffect(() => {
    const controller = new AbortController();
    loadPlanning({ signal: controller.signal })
      .then((result) => setPlanning(result.planning))
      .catch(() => {});
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!selectedCategory || !drilldownRef.current) return;

    if (skipNextScrollRef.current) {
      skipNextScrollRef.current = false;
      return;
    }

    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    drilldownRef.current.scrollIntoView({ behavior: reduceMotion ? "auto" : "smooth", block: "start" });
  }, [selectedCategory]);


  function toggleCategory(name) {
    setSelectedCategory((prev) => (prev === name ? null : name));
    setExpandedEstablishments(new Set());
  }

  function toggleEstablishment(name) {
    setExpandedEstablishments((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  }


  function changePeriod(key, mode) {
    const next = periodParams(searchParams, key, mode);
    setSearchParams(next, { replace: true });
    setCategoryPage(1);
    setExpandedEstablishments(new Set());
  }

  const periodPhrase = `em ${period.label.toLowerCase()}`;
  const heading = (
    <header className="categorias__header">
      <SectionHeading title="Categorias" level={1} />
      <MonthNavigator period={period} onChange={changePeriod} />
    </header>
  );

  if (error) {
    return (
      <div className="categorias">
        {heading}
        <p className="categorias__error" role="alert">
          {error}
        </p>
      </div>
    );
  }
  if (accountsStatus === "ready" && !scopeKey) return <NoBankAccount />;

  const pageStart = (categoryPageSafe - 1) * CATEGORY_PAGE_SIZE + 1;
  const pageEnd = pageStart + pagedBreakdown.length - 1;
  const establishmentCount = establishmentBreakdown.length;

  return (
    <div className="categorias">
      {heading}

      <div className="categorias__grid">
        <CategorySection title="Despesas por categoria" className="categorias__card">
          {loading ? (
            <div className="categorias__donut-loading">
              <Skeleton width="15rem" height="15rem" style={{ borderRadius: "50%" }} />
              <Skeleton width="100%" height="6rem" />
            </div>
          ) : breakdown.length === 0 ? (
            <p className="categorias__hint">Nenhuma despesa {periodPhrase}.</p>
          ) : (
            <div className="categorias__donut-block">
              <div
                className="categorias__donut-wrap"
                role="img"
                aria-label={`Gráfico das despesas por categoria ${periodPhrase}. Os valores de cada categoria estão na lista Categorias.`}
              >
                <CategoryDonutChart
                  height={272}
                  formatValue={formatCurrency}
                  data={breakdown}
                  onSliceClick={toggleCategory}
                  centerLabel={formatCurrency(totalExpense)}
                  centerCaption="Total"
                  showLegend={false}
                />
              </div>

              <ul className="categorias__legend">
                {breakdown.slice(0, 3).map((c) => (
                  <li key={c.name} className="categorias__legend-item">
                    <span className="categorias__legend-dot" style={{ backgroundColor: c.color }} aria-hidden="true" />
                    <span className="categorias__legend-name">{c.name}</span>
                    <span className="categorias__legend-pct">{formatPercentage(c.percentage)}</span>
                    <span className="categorias__legend-value">{formatCurrency(c.value)}</span>
                  </li>
                ))}
              </ul>
              {breakdown.length > 3 && (
                <p className="categorias__legend-more">
                  Mais {breakdown.length - 3} {breakdown.length - 3 === 1 ? "categoria" : "categorias"} na lista
                </p>
              )}
            </div>
          )}
        </CategorySection>

        <CategorySection
          title="Categorias"
          className="categorias__card"
          action={
            !loading &&
            breakdown.length > 0 && (
              <label className="categorias__sort">
                <span>Ordenar</span>
                <select
                  value={categorySort}
                  onChange={(e) => {
                    setCategorySort(e.target.value);
                    setCategoryPage(1);
                  }}
                >
                  {CATEGORY_SORT_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </label>
            )
          }
        >
          {loading ? (
            <div className="categorias__list-loading">
              {Array.from({ length: 4 }, (_, i) => (
                <Skeleton key={i} width="100%" height="4.5rem" />
              ))}
            </div>
          ) : breakdown.length === 0 ? (
            <p className="categorias__hint">Nada para listar {periodPhrase}.</p>
          ) : (
            <>
              <CategoryList data={pagedBreakdown} activeName={selectedCategory} onSelect={toggleCategory} />

              {categoryTotalPages > 1 && (
                <nav className="categorias__pagination" aria-label="Páginas de categorias">
                  <span className="categorias__page-count">
                    {pageStart}–{pageEnd} de {sortedBreakdown.length}
                  </span>
                  <div className="categorias__pages">
                    {Array.from({ length: categoryTotalPages }, (_, i) => i + 1).map((pageNumber) => (
                      <button
                        key={pageNumber}
                        type="button"
                        className={`categorias__page-btn ${
                          pageNumber === categoryPageSafe ? "categorias__page-btn--active" : ""
                        }`}
                        onClick={() => setCategoryPage(pageNumber)}
                        aria-current={pageNumber === categoryPageSafe ? "page" : undefined}
                        aria-label={`Página ${pageNumber}`}
                      >
                        {pageNumber}
                      </button>
                    ))}
                  </div>
                </nav>
              )}
            </>
          )}
        </CategorySection>
      </div>

      {!loading && selectedCategory && (
        <section
          ref={drilldownRef}
          className={`categorias__detail${cutImpact ? " categorias__detail--with-aside" : ""}`}
          aria-labelledby="categorias-detail-title"
        >
          <div className="categorias__detail-main">
            <header className="categorias__detail-head">
              <h2 id="categorias-detail-title" className="categorias__detail-title">
                {selectedCategory}
              </h2>
              <p className="categorias__detail-note">
                {formatCurrency(selectedCategoryAmount)} {periodPhrase}
                {establishmentCount > 0 &&
                  `, em ${establishmentCount} ${establishmentCount === 1 ? "estabelecimento" : "estabelecimentos"}`}
              </p>
            </header>

            {establishmentCount === 0 ? (
              <p className="categorias__hint">Nenhuma transação encontrada.</p>
            ) : (
              <EstablishmentList
                data={establishmentBreakdown}
                expandedNames={expandedEstablishments}
                onToggle={toggleEstablishment}
                detailsQuery={{
                  ...scopeQuery,
                  from: range.from,
                  to: range.to,
                  type: "Expense",
                  category: selectedCategory,
                  refresh: dataRefreshKey,
                }}
              />
            )}
          </div>

          {cutImpact && (
            <CategorySection title="Simular um corte" className="categorias__impact">
              <div className="categorias__impact-rates" role="radiogroup" aria-label="Quanto cortar">
                {CUT_RATE_OPTIONS.map((rate) => (
                  <button
                    key={rate}
                    type="button"
                    role="radio"
                    aria-checked={cutRate === rate}
                    className={`categorias__segment ${cutRate === rate ? "categorias__segment--active" : ""}`}
                    onClick={() => setCutRate(rate)}
                  >
                    {formatPercentage(rate * 100)}
                  </button>
                ))}
              </div>

              <p className="categorias__impact-sentence">
                {cutImpactSentence(selectedCategory, selectedCategoryAmount, cutRate, cutImpact)}
              </p>

              <dl className="categorias__impact-targets">
                <div className="categorias__impact-target">
                  <dt>Reserva de emergência</dt>
                  <dd>
                    {formatCurrency(cutImpact.reserveImpact.current)}
                    <span className="categorias__impact-arrow" aria-hidden="true">
                      →
                    </span>
                    <span className="categorias__sr-only">passaria a</span>
                    <strong>{formatCurrency(cutImpact.reserveImpact.afterCut)}</strong>
                  </dd>
                </div>

                {cutImpact.goalImpact && (
                  <div className="categorias__impact-target">
                    <dt>Meta: {cutImpact.goalImpact.name}</dt>
                    <dd>
                      Faltam {formatCurrency(cutImpact.goalImpact.remainingBefore)}
                      <span className="categorias__impact-arrow" aria-hidden="true">
                        →
                      </span>
                      <span className="categorias__sr-only">passaria a</span>
                      <strong>faltam {formatCurrency(cutImpact.goalImpact.remainingAfter)}</strong>
                    </dd>
                  </div>
                )}
              </dl>

              {!cutImpact.goalImpact && (
                <p className="categorias__impact-hint">
                  Cadastre uma meta em Investimentos para ver quanto esse corte aproxima você dela.
                </p>
              )}
            </CategorySection>
          )}
        </section>
      )}
    </div>
  );
}
