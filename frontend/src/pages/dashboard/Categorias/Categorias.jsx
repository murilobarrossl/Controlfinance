import { useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router";
import { getCategories } from "../../../api/categories.js";
import { getTransactions } from "../../../api/transactions.js";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import BreakdownRows from "../../../components/dashboard/BreakdownRows/BreakdownRows.jsx";
import { getGoals } from "../../../utils/investmentStorage.js";
import { getReserve } from "../../../utils/emergencyReserveStorage.js";
import { calculateCutImpact } from "../../../utils/cutImpact.js";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import "./Categorias.css";

const CUT_RATE_OPTIONS = [0.1, 0.2, 0.3, 0.5];

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

// Só pro pré-select da carga inicial (ver useEffect abaixo): acha a categoria de despesa com
// maior gasto no ano, sem precisar da lista de categorias (cor/percentual), que ainda não
// chegou nesse ponto.
function topExpenseCategoryName(transactions, year) {
  const totals = new Map();
  for (const t of transactions) {
    if (t.type !== "Expense" || t.isTransfer) continue;
    if (new Date(t.dueDate).getUTCFullYear() !== year) continue;
    const name = t.categoryName || "Sem categoria";
    totals.set(name, (totals.get(name) || 0) + t.amount);
  }

  let topName = null;
  let topValue = -Infinity;
  for (const [name, value] of totals) {
    if (value > topValue) {
      topValue = value;
      topName = name;
    }
  }
  return topName;
}

// Parcela de compra no cartão vem da Polp com o número da parcela grudado no fim do nome da
// compra original (ex.: "MERCADOLIVRE*OTHPR01/12", "MERCADOLIVRE*OTHPR02/12"...). Sem tratar
// isso, cada parcela virava um "estabelecimento" separado no agrupamento — 12 linhas pra uma
// compra só. Extrai o nome base (sem o "01/12") pra juntar todas as parcelas na mesma linha.
const INSTALLMENT_SUFFIX = /(\d{2}\/\d{2})$/;

function establishmentKey(name) {
  return INSTALLMENT_SUFFIX.test(name) ? name.replace(INSTALLMENT_SUFFIX, "").trim() : name;
}

// "01/12" etc., ou null se o nome não tiver o sufixo de parcela.
function installmentLabel(name) {
  return name.match(INSTALLMENT_SUFFIX)?.[1] ?? null;
}

function groupByEstablishment(transactions) {
  const totals = new Map();
  for (const t of transactions) {
    const isInstallment = INSTALLMENT_SUFFIX.test(t.name);
    const key = establishmentKey(t.name);
    const entry = totals.get(key) || { name: key, value: 0, count: 0, isInstallmentGroup: false };
    entry.value += t.amount;
    entry.count += 1;
    if (isInstallment) entry.isInstallmentGroup = true;
    totals.set(key, entry);
  }
  const total = [...totals.values()].reduce((sum, e) => sum + e.value, 0);
  return [...totals.values()]
    .map((e) => ({ ...e, percentage: total > 0 ? (e.value / total) * 100 : 0 }))
    .sort((a, b) => b.value - a.value);
}

export default function Categorias() {
  const [searchParams] = useSearchParams();
  const [categories, setCategories] = useState([]);
  const [transactions, setTransactions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [selectedCategory, setSelectedCategory] = useState(null);
  const [expandedEstablishments, setExpandedEstablishments] = useState(() => new Set());
  const [yearOffset, setYearOffset] = useState(0); // 0 = ano atual; 1 = ano anterior; e por aí vai.
  const [cutRate, setCutRate] = useState(0.3);
  const [goals] = useState(() => getGoals());
  const [reserve] = useState(() => getReserve());
  const drilldownRef = useRef(null);
  const skipNextScrollRef = useRef(false);

  useEffect(() => {
    Promise.all([getCategories(), getTransactions()])
      .then(([categoriesData, transactionsData]) => {
        setCategories(categoriesData);
        setTransactions(transactionsData);

        // Chegando de um link da aba Receitas e despesas (?categoria=Nome): abre direto nela,
        // com scroll até a seção, em vez de pré-selecionar a maior categoria do ano.
        const categoryFromLink = searchParams.get("categoria");
        if (categoryFromLink) {
          setSelectedCategory(categoryFromLink);
          return;
        }

        // Pré-seleciona a categoria com maior gasto do ano atual, pra seção de transações já
        // vir aberta sem precisar de clique. Só nessa carga inicial: dali em diante o usuário
        // controla a seleção normalmente, inclusive desmarcando.
        const topName = topExpenseCategoryName(transactionsData, new Date().getUTCFullYear());
        if (topName) {
          skipNextScrollRef.current = true;
          setSelectedCategory(topName);
        }
      })
      .catch((err) => setError(err.message || "Não foi possível carregar as categorias."))
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps -- só lê searchParams na carga inicial
  }, []);

  const selectedYear = new Date().getUTCFullYear() - yearOffset;

  const scopedTransactions = useMemo(() => {
    return transactions.filter((t) => new Date(t.dueDate).getUTCFullYear() === selectedYear);
  }, [transactions, selectedYear]);

  // Só despesas reais entram aqui: a aba é exclusivamente sobre gastos (a receita já tem seu
  // próprio lado, autossuficiente, na aba Receitas e despesas), e auto-transferências não são
  // gasto de verdade.
  const expenseTransactions = useMemo(
    () => scopedTransactions.filter((t) => t.type === "Expense" && !t.isTransfer),
    [scopedTransactions]
  );

  const breakdown = useMemo(() => {
    const totals = new Map();

    for (const t of expenseTransactions) {
      const name = t.categoryName || "Sem categoria";
      const entry = totals.get(name) || { name, value: 0, count: 0 };
      entry.value += t.amount;
      entry.count += 1;
      totals.set(name, entry);
    }

    const total = [...totals.values()].reduce((sum, e) => sum + e.value, 0);

    return [...totals.values()]
      .map((e) => {
        const category = categories.find((c) => c.name === e.name);
        return { ...e, color: category?.color, percentage: total > 0 ? (e.value / total) * 100 : 0 };
      })
      .sort((a, b) => b.value - a.value);
  }, [expenseTransactions, categories]);

  const totalExpense = useMemo(() => breakdown.reduce((sum, c) => sum + c.value, 0), [breakdown]);

  const categoryTransactions = useMemo(() => {
    if (!selectedCategory) return [];
    return expenseTransactions.filter((t) => (t.categoryName || "Sem categoria") === selectedCategory);
  }, [expenseTransactions, selectedCategory]);

  const establishmentBreakdown = useMemo(() => groupByEstablishment(categoryTransactions), [categoryTransactions]);

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

  if (loading) return <p className="categorias__hint">Carregando...</p>;
  if (error) return <p className="categorias__error">{error}</p>;

  return (
    <div className="categorias">
      <SectionHeading kicker="Onde seu dinheiro vai" title="Categorias" align="left" />

      <RangePicker
        label={String(selectedYear)}
        onPrev={() => setYearOffset((prev) => prev + 1)}
        onNext={() => setYearOffset((prev) => Math.max(0, prev - 1))}
        nextDisabled={yearOffset === 0}
      />

      <p className="categorias__section-hint">
        Considerando todas as contas conectadas. Só despesas — receitas ficam em Receitas e despesas.
      </p>

      <div className="categorias__grid">
        <Card title="Despesas por categoria">
          {breakdown.length === 0 ? (
            <p className="categorias__hint">Nenhuma despesa categorizada ainda.</p>
          ) : (
            <CategoryDonutChart
              height={280}
              formatValue={formatCurrency}
              data={breakdown}
              onSliceClick={toggleCategory}
              centerLabel={formatCurrency(totalExpense)}
              showLegend={false}
            />
          )}
        </Card>

        <Card title="Categorias">
          {breakdown.length === 0 ? (
            <p className="categorias__hint">Nenhuma despesa categorizada ainda.</p>
          ) : (
            <BreakdownRows data={breakdown} tone="expense" activeName={selectedCategory} onRowClick={toggleCategory} />
          )}
        </Card>
      </div>

      {selectedCategory && (
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
                  <ul className="categorias__visits">
                    {categoryTransactions
                      .filter((t) => establishmentKey(t.name) === name)
                      .sort((a, b) => new Date(b.dueDate) - new Date(a.dueDate))
                      .map((t) => (
                        <li key={t.id} className="categorias__visits-item">
                          <span className="categorias__visits-date">
                            {installmentLabel(t.name) && (
                              <span className="categorias__visits-installment">Parcela {installmentLabel(t.name)} · </span>
                            )}
                            {new Date(t.dueDate).toLocaleDateString("pt-BR")}
                          </span>
                          <span className="categorias__visits-amount">{formatCurrency(t.amount)}</span>
                        </li>
                      ))}
                  </ul>
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
                    Você ainda não cadastrou metas — crie uma em Investimentos para ver o quanto esse corte te
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
