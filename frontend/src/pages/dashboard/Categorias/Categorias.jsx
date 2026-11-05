import { useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router";
import { getCategories } from "../../../api/categories.js";
import { getTransactionAggregates } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import CardPicker from "../../../components/dashboard/CardPicker/CardPicker.jsx";
import { getAccountKindLabel, isCreditCardAccount } from "../../../utils/accountKind.js";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import TransactionDetails from "../../../components/dashboard/TransactionDetails/TransactionDetails.jsx";
import BreakdownRows from "../../../components/dashboard/BreakdownRows/BreakdownRows.jsx";
import { getGoals } from "../../../utils/investmentStorage.js";
import { getReserve } from "../../../utils/emergencyReserveStorage.js";
import { calculateCutImpact } from "../../../utils/cutImpact.js";
import { formatCurrency, formatDate, formatPercentage } from "../../../utils/financeMath.js";
import { donutSliceColors, resolveCategoryColor } from "../../../utils/categoryColors.js";
import { formatMonthLong, toIsoDate } from "../../../utils/monthLabel.js";
import "./Categorias.css";
import usePageTitle from "../../../hooks/usePageTitle.js";

const EMPTY_LIST = [];

const CUT_RATE_OPTIONS = [0.1, 0.2, 0.3, 0.5];

// Quantas categorias por página na lista à direita: sem paginar, uma conta com muitas categorias
// deixava essa lista muito mais alta que o gráfico de rosca ao lado, sobrando espaço vazio embaixo
// do gráfico (o grid estica os dois cards pra mesma altura, a mais alta dos dois).
const CATEGORY_PAGE_SIZE = 5;

const CATEGORY_SORT_OPTIONS = [
  { value: "value-desc", label: "Maior gasto" },
  { value: "value-asc", label: "Menor gasto" },
  { value: "count-desc", label: "Mais ativas" },
];

const PERIOD_MODES = [
  { value: "month", label: "Mês" },
  { value: "year", label: "Ano" },
  { value: "all", label: "Todo o período" },
];

// Intervalo [from, to) e rótulo do período escolhido. offset = quantos meses/anos pra trás a partir
// do atual (0 = o atual). "Todo o período" não tem navegação: pega do começo de 2000 até o fim do
// ano atual, o que cobre qualquer histórico real (o backend aceita intervalos de até 50 anos).
function resolvePeriod({ mode, offset }) {
  const now = new Date();
  if (mode === "month") {
    const start = new Date(Date.UTC(now.getFullYear(), now.getMonth() - offset, 1));
    const end = new Date(Date.UTC(now.getFullYear(), now.getMonth() - offset + 1, 1));
    return { key: `month:${offset}`, from: toIsoDate(start), to: toIsoDate(end), label: formatMonthLong(start) };
  }
  if (mode === "year") {
    const year = now.getFullYear() - offset;
    return { key: `year:${offset}`, from: `${year}-01-01`, to: `${year + 1}-01-01`, label: String(year) };
  }
  return { key: "all", from: "2000-01-01", to: `${now.getFullYear() + 1}-01-01`, label: "Todo o período" };
}

function cutImpactSentence(categoryName, categoryAmount, cutRate, impact) {
  const destinations = [
    `sua reserva de emergência, que iria de ${formatCurrency(impact.reserveImpact.current)} para ${formatCurrency(
      impact.reserveImpact.afterCut
    )}`,
  ];
  if (impact.goalImpact) {
    destinations.push(
      `aproximar a meta "${impact.goalImpact.name}" (faltariam ${formatCurrency(
        impact.goalImpact.remainingAfter
      )} em vez de ${formatCurrency(impact.goalImpact.remainingBefore)})`
    );
  }
  return `Você gastou ${formatCurrency(categoryAmount)} em ${categoryName} neste período. Se cortar ${formatPercentage(
    cutRate * 100
  )} (${formatCurrency(impact.cutAmount)}), esse valor poderia ir para ${destinations.join(" ou ")}.`;
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

const INSTALLMENT_SUFFIX = /(\d{2}\/\d{2})$/;

// "01/12" etc., ou null se o nome não tiver o sufixo de parcela.
function installmentLabel(name) {
  return name.match(INSTALLMENT_SUFFIX)?.[1] ?? null;
}

export default function Categorias() {
  usePageTitle("Categorias");
  const [searchParams] = useSearchParams();
  const [categoryFromLink] = useState(() => searchParams.get("categoria"));
  const { accounts, connectors, effectiveAccountId, setSelectedAccountId, dataRefreshKey, accountsStatus } = useAccount();
  const [result, setResult] = useState(null);
  const [aggregateResult, setAggregateResult] = useState(null);
  // Período mostrado: um mês, um ano ou tudo. offset 0 = o atual, 1 = o anterior, e assim por diante.
  const [period, setPeriod] = useState({ mode: "year", offset: 0 });
  const range = useMemo(() => resolvePeriod(period), [period]);
  const requestKey = `${effectiveAccountId ?? ""}|${dataRefreshKey}`;
  const aggregateKey = `${requestKey}|${range.key}`;
  const loading = accountsStatus !== "ready" || result?.key !== requestKey || aggregateResult?.key !== aggregateKey;
  const categories = result?.categories ?? EMPTY_LIST;
  const aggregates = aggregateResult?.aggregates ?? null;
  const error =
    accountsStatus === "error"
      ? "Não foi possível carregar as contas."
      : (result?.key === requestKey && result.error) || (aggregateResult?.key === aggregateKey && aggregateResult.error) || "";
  const [selectedCategory, setSelectedCategory] = useState(null);
  const [categorySort, setCategorySort] = useState("value-desc");
  const [categoryPage, setCategoryPage] = useState(1);
  const [expandedEstablishments, setExpandedEstablishments] = useState(() => new Set());
  const [cutRate, setCutRate] = useState(0.3);
  const [goals] = useState(() => getGoals());
  const [reserve] = useState(() => getReserve());
  const drilldownRef = useRef(null);
  const skipNextScrollRef = useRef(false);
  // A pré-seleção de categoria (via link ou maior gasto do ano) só deve acontecer uma vez, na
  // primeira carga. Trocar de conta depois não pode reabrir/trocar a categoria que o usuário já
  // escolheu.
  const hasAppliedInitialSelectionRef = useRef(false);

  useEffect(() => {
    if (accountsStatus !== "ready") return;

    const controller = new AbortController();
    const { signal } = controller;
    const key = `${effectiveAccountId ?? ""}|${dataRefreshKey}`;

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
  }, [effectiveAccountId, dataRefreshKey, accountsStatus]);

  useEffect(() => {
    if (accountsStatus !== "ready") return;

    const controller = new AbortController();
    const { signal } = controller;
    const key = `${effectiveAccountId ?? ""}|${dataRefreshKey}|${range.key}`;

    getTransactionAggregates(
      {
        bankAccountId: effectiveAccountId,
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

        // Pré-seleciona a categoria com maior gasto do ano atual, pra seção de transações já
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
  }, [effectiveAccountId, dataRefreshKey, range, categoryFromLink, accountsStatus]);

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
        return sorted.sort((a, b) => a.value - b.value);
      case "count-desc":
        return sorted.sort((a, b) => b.count - a.count);
      case "value-desc":
      default:
        return sorted.sort((a, b) => b.value - a.value);
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
    if (!selectedCategory || selectedCategoryAmount <= 0) return null;
    return calculateCutImpact({
      categoryAmount: selectedCategoryAmount,
      cutRate,
      reserveAmount: reserve.currentAmount,
      goals,
    });
  }, [selectedCategory, selectedCategoryAmount, cutRate, reserve, goals]);

  useEffect(() => {
    if (!selectedCategory || !drilldownRef.current) return;

    if (skipNextScrollRef.current) {
      skipNextScrollRef.current = false;
      return;
    }

    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    drilldownRef.current.scrollIntoView({ behavior: reduceMotion ? "auto" : "smooth", block: "start" });
  }, [selectedCategory]);

  const connectorsById = useMemo(() => new Map(connectors.map((c) => [c.id, c])), [connectors]);
  const accountOptions = useMemo(
    () =>
      [...accounts]
        .map((account) => {
          const connector = connectorsById.get(account.bankCode);
          const bankName = connector?.name ?? "Conta cadastrada manualmente";
          const kind = getAccountKindLabel(account);
          const seen = new Set([bankName.toLowerCase(), "banco"]);
          const detail = [...String(account.name ?? "").split(" · "), kind].filter((part) => {
            const key = part.trim().toLowerCase();
            if (!key || seen.has(key)) return false;
            seen.add(key);
            return true;
          });
          return {
            account,
            bankName,
            detail: detail.join(" · ") || kind,
            logoUrl: connector?.logoUrl,
            color: connector?.color ? (connector.color.startsWith("#") ? connector.color : `#${connector.color}`) : undefined,
            kind,
            isCard: isCreditCardAccount(account),
          };
        })
        .sort(
          (a, b) =>
            Number(a.isCard) - Number(b.isCard) || a.bankName.localeCompare(b.bankName, "pt-BR"),
        ),
    [accounts, connectorsById],
  );
  const activeOption = accountOptions.find((o) => o.account.id === effectiveAccountId) ?? null;

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

  if (error) return <p className="categorias__error">{error}</p>;

  return (
    <div className="categorias">
      <SectionHeading title="Categorias" level={1}>Onde seu dinheiro vai</SectionHeading>

      <div className="categorias__period">
        <div className="categorias__period-modes" role="radiogroup" aria-label="Período">
          {PERIOD_MODES.map((option) => (
            <button
              key={option.value}
              type="button"
              role="radio"
              aria-checked={period.mode === option.value}
              className={`categorias__period-chip ${period.mode === option.value ? "categorias__period-chip--active" : ""}`}
              onClick={() => setPeriod({ mode: option.value, offset: 0 })}
            >
              {option.label}
            </button>
          ))}
        </div>
        {period.mode === "all" ? (
          <span className="categorias__period-label">{range.label}</span>
        ) : (
          <RangePicker
            label={range.label}
            onPrev={() => setPeriod((prev) => ({ ...prev, offset: prev.offset + 1 }))}
            onNext={() => setPeriod((prev) => ({ ...prev, offset: Math.max(0, prev.offset - 1) }))}
            nextDisabled={period.offset === 0}
          />
        )}
      </div>

      {activeOption &&
        (accountOptions.length > 1 ? (
          <div className="categorias__scope">
            <span className="categorias__scope-title" id="categorias-conta-rotulo">
              Mostrando as despesas de
            </span>
            <CardPicker
              id="categorias-conta"
              labelId="categorias-conta-rotulo"
              options={accountOptions.map((option) => ({
                value: option.account.id,
                title: option.bankName,
                subtitle: option.detail,
                color: option.color,
                logo: { name: option.bankName, logoUrl: option.logoUrl, color: option.color },
              }))}
              value={effectiveAccountId}
              onChange={(id) => {
                setSelectedAccountId(id);
                setSelectedCategory(null);
                setCategoryPage(1);
              }}
            />
          </div>
        ) : (
          <p className="categorias__scope-title">
            Mostrando as despesas de:{" "}
            <strong>
              {activeOption.bankName} · {activeOption.detail}
            </strong>
          </p>
        ))}

      <p className="categorias__section-hint">Só despesas. Receitas ficam em Receitas e despesas.</p>


      <div className="categorias__grid">
        <Card title="Despesas por categoria">
          {loading ? (
            <Skeleton width="100%" height="340px" />
          ) : breakdown.length === 0 ? (
            <p className="categorias__hint">Nenhuma despesa categorizada em {range.label}.</p>
          ) : (
            <div className="categorias__donut-block">
              <div className="categorias__donut-wrap">
                <CategoryDonutChart
                  height={340}
                  formatValue={formatCurrency}
                  data={breakdown}
                  onSliceClick={toggleCategory}
                  centerLabel={formatCurrency(totalExpense)}
                  showLegend={false}
                />
              </div>

              <ul className="categorias__donut-summary">
                {breakdown.slice(0, 3).map((c) => (
                  <li key={c.name} className="categorias__donut-summary-item">
                    <span className="categorias__donut-summary-dot" style={{ backgroundColor: c.color }} />
                    <span className="categorias__donut-summary-name">{c.name}</span>
                    <span className="categorias__donut-summary-percentage">{formatPercentage(c.percentage)}</span>
                    <span className="categorias__donut-summary-value">{formatCurrency(c.value)}</span>
                  </li>
                ))}
                {breakdown.length > 3 && (
                  <li className="categorias__donut-summary-more">
                    + {breakdown.length - 3} {breakdown.length - 3 === 1 ? "categoria" : "categorias"} na lista ao lado
                  </li>
                )}
              </ul>
            </div>
          )}
        </Card>

        <Card
          title="Categorias"
          action={
            !loading &&
            breakdown.length > 0 && (
              <select
                value={categorySort}
                onChange={(e) => {
                  setCategorySort(e.target.value);
                  setCategoryPage(1);
                }}
                className="categorias__select"
                aria-label="Ordenar categorias"
              >
                {CATEGORY_SORT_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            )
          }
        >
          {loading ? (
            <Skeleton width="100%" height="340px" />
          ) : breakdown.length === 0 ? (
            <p className="categorias__hint">Nenhuma despesa categorizada ainda.</p>
          ) : (
            <>
              <BreakdownRows
                data={pagedBreakdown}
                tone="expense"
                activeName={selectedCategory}
                onRowClick={toggleCategory}
              />

              {categoryTotalPages > 1 && (
                <div className="categorias__pagination" role="navigation" aria-label="Páginas de categorias">
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
              )}
            </>
          )}
        </Card>
      </div>

      {!loading && selectedCategory && (
        <div ref={drilldownRef} className="categorias__drilldown-section">
          <Card title={`${selectedCategory}: onde foi o dinheiro`}>
            {establishmentBreakdown.length === 0 ? (
              <p className="categorias__hint">Nenhuma transação encontrada.</p>
            ) : (
              <BreakdownRows
                data={establishmentBreakdown}
                tone="expense"
                expandedNames={expandedEstablishments}
                onRowClick={toggleEstablishment}
                renderExpanded={(name) => (
                  <TransactionDetails
                    className="categorias__visits"
                    query={{
                      bankAccountId: effectiveAccountId,
                      from: range.from,
                      to: range.to,
                      type: "Expense",
                      category: selectedCategory,
                      establishment: name,
                      refresh: dataRefreshKey,
                    }}
                    renderItem={(t) => (
                      <li key={t.id} className="categorias__visits-item">
                        <span className="categorias__visits-date">
                          {installmentLabel(t.name) && (
                            <span className="categorias__visits-installment">Parcela {installmentLabel(t.name)} · </span>
                          )}
                          {formatDate(t.dueDate)}
                        </span>
                        <span className="categorias__visits-amount">{formatCurrency(t.amount)}</span>
                      </li>
                    )}
                  />
                )}
              />
            )}
          </Card>

          {cutImpact && (
            <Card title={`Impacto de cortar ${selectedCategory}`}>
              <div className="categorias__impact-rates">
                {CUT_RATE_OPTIONS.map((rate) => (
                  <button
                    key={rate}
                    type="button"
                    className={`categorias__impact-rate ${
                      cutRate === rate ? "categorias__impact-rate--active" : ""
                    }`}
                    onClick={() => setCutRate(rate)}
                  >
                    {formatPercentage(rate * 100)}
                  </button>
                ))}
              </div>

              <p className="categorias__impact-sentence">
                {cutImpactSentence(selectedCategory, selectedCategoryAmount, cutRate, cutImpact)}
              </p>

              <div className="categorias__impact-targets">
                <div className="categorias__impact-target">
                  <span className="categorias__impact-target-label">Reserva de emergência</span>
                  <span className="categorias__impact-target-values">
                    {formatCurrency(cutImpact.reserveImpact.current)} →{" "}
                    <span className="categorias__impact-target-after">
                      {formatCurrency(cutImpact.reserveImpact.afterCut)}
                    </span>
                  </span>
                </div>

                {cutImpact.goalImpact ? (
                  <div className="categorias__impact-target">
                    <span className="categorias__impact-target-label">Meta: {cutImpact.goalImpact.name}</span>
                    <span className="categorias__impact-target-values">
                      faltam {formatCurrency(cutImpact.goalImpact.remainingBefore)} →{" "}
                      <span className="categorias__impact-target-after">
                        faltam {formatCurrency(cutImpact.goalImpact.remainingAfter)}
                      </span>
                    </span>
                  </div>
                ) : (
                  <p className="categorias__hint">
                    Você ainda não cadastrou metas. Crie uma em Investimentos para ver o quanto esse corte te
                    aproximaria dela.
                  </p>
                )}
              </div>
            </Card>
          )}
        </div>
      )}
    </div>
  );
}
