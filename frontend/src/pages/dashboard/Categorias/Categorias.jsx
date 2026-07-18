import { useEffect, useMemo, useRef, useState } from "react";
import { getCategories } from "../../../api/categories.js";
import { getTransactions } from "../../../api/transactions.js";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import CategorySpendBarsChart from "../../../components/charts/CategorySpendBarsChart.jsx";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import "./Categorias.css";

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

export default function Categorias() {
  const [categories, setCategories] = useState([]);
  const [transactions, setTransactions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [selectedCategory, setSelectedCategory] = useState(null);
  const [yearOffset, setYearOffset] = useState(0); // 0 = ano atual; 1 = ano anterior; e por aí vai.
  const drilldownRef = useRef(null);
  const skipNextScrollRef = useRef(false);

  useEffect(() => {
    Promise.all([getCategories(), getTransactions()])
      .then(([categoriesData, transactionsData]) => {
        setCategories(categoriesData);
        setTransactions(transactionsData);

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
  }, []);

  const selectedYear = new Date().getUTCFullYear() - yearOffset;

  const scopedTransactions = useMemo(() => {
    return transactions.filter((t) => new Date(t.dueDate).getUTCFullYear() === selectedYear);
  }, [transactions, selectedYear]);

  const breakdown = useMemo(() => {
    const expenses = scopedTransactions.filter((t) => t.type === "Expense" && !t.isTransfer);
    const totals = new Map();

    for (const t of expenses) {
      const name = t.categoryName || "Sem categoria";
      totals.set(name, (totals.get(name) || 0) + t.amount);
    }

    const total = [...totals.values()].reduce((sum, v) => sum + v, 0);

    return [...totals.entries()]
      .map(([name, amount]) => {
        const category = categories.find((c) => c.name === name);
        return {
          name,
          value: amount,
          color: category?.color,
          percentage: total > 0 ? (amount / total) * 100 : 0,
        };
      })
      .sort((a, b) => b.value - a.value);
  }, [scopedTransactions, categories]);

  const totalExpense = useMemo(() => breakdown.reduce((sum, c) => sum + c.value, 0), [breakdown]);

  const drillDownTransactions = useMemo(() => {
    if (!selectedCategory) return [];
    return scopedTransactions
      .filter((t) => (t.categoryName || "Sem categoria") === selectedCategory)
      .sort((a, b) => new Date(b.dueDate) - new Date(a.dueDate));
  }, [scopedTransactions, selectedCategory]);

  useEffect(() => {
    if (!selectedCategory || !drilldownRef.current) return;

    if (skipNextScrollRef.current) {
      skipNextScrollRef.current = false;
      return;
    }

    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    drilldownRef.current.scrollIntoView({ behavior: reduceMotion ? "auto" : "smooth", block: "start" });
  }, [selectedCategory]);

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

      <p className="categorias__section-hint">Considerando todas as contas conectadas.</p>

      <div className="categorias__grid">
        <Card title="Despesas por categoria">
          {breakdown.length === 0 ? (
            <p className="categorias__hint">Nenhuma despesa categorizada ainda.</p>
          ) : (
            <CategoryDonutChart
              height={280}
              formatValue={formatCurrency}
              data={breakdown}
              onSliceClick={setSelectedCategory}
              centerLabel={formatCurrency(totalExpense)}
              showLegend={false}
            />
          )}
        </Card>

        <Card title="Categorias">
          <ul className="categorias__list">
            {breakdown.map((c) => (
              <li key={c.name}>
                <button
                  type="button"
                  className={`categorias__list-item ${
                    selectedCategory === c.name ? "categorias__list-item--active" : ""
                  }`}
                  onClick={() => setSelectedCategory(c.name === selectedCategory ? null : c.name)}
                >
                  <span className="categorias__list-dot" style={{ backgroundColor: c.color || "#808080" }} />
                  <span className="categorias__list-name">{c.name}</span>
                  <span className="categorias__list-percentage">{formatPercentage(c.percentage)}</span>
                  <span className="categorias__list-amount">{formatCurrency(c.value)}</span>
                </button>
              </li>
            ))}
          </ul>
        </Card>
      </div>

      <Card title="Comparativo por categoria">
        {breakdown.length === 0 ? (
          <p className="categorias__hint">Nenhuma despesa categorizada ainda.</p>
        ) : (
          <>
            <p className="categorias__section-hint">
              Categorias em ordem alfabética. Clique numa barra para ver as transações.
            </p>
            <CategorySpendBarsChart
              data={breakdown}
              formatValue={formatCurrency}
              onBarClick={setSelectedCategory}
            />
          </>
        )}
      </Card>

      {selectedCategory && (
        <div ref={drilldownRef}>
          <Card title={`Transações: ${selectedCategory}`}>
            {drillDownTransactions.length === 0 ? (
              <p className="categorias__hint">Nenhuma transação encontrada.</p>
            ) : (
              <ul className="categorias__drilldown">
                {drillDownTransactions.map((t) => (
                  <li key={t.id} className="categorias__drilldown-item">
                    <div>
                      <span className="categorias__drilldown-name">{t.name}</span>
                      <span className="categorias__drilldown-date">
                        {new Date(t.dueDate).toLocaleDateString("pt-BR")}
                      </span>
                    </div>
                    <span className={t.type === "Income" ? "categorias__amount--income" : "categorias__amount--expense"}>
                      {formatCurrency(t.amount)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
      )}
    </div>
  );
}
