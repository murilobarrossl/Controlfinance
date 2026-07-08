import { useEffect, useMemo, useState } from "react";
import { getCategories } from "../../../api/categories.js";
import { getTransactions } from "../../../api/transactions.js";
import Card from "../../../components/ui/Card/Card.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import "./Categorias.css";

export default function Categorias() {
  const [categories, setCategories] = useState([]);
  const [transactions, setTransactions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [selectedCategory, setSelectedCategory] = useState(null);

  useEffect(() => {
    Promise.all([getCategories(), getTransactions()])
      .then(([categoriesData, transactionsData]) => {
        setCategories(categoriesData);
        setTransactions(transactionsData);
      })
      .catch((err) => setError(err.message || "Não foi possível carregar as categorias."))
      .finally(() => setLoading(false));
  }, []);

  const breakdown = useMemo(() => {
    const expenses = transactions.filter((t) => t.type === "Expense");
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
  }, [transactions, categories]);

  const drillDownTransactions = useMemo(() => {
    if (!selectedCategory) return [];
    return transactions
      .filter((t) => (t.categoryName || "Sem categoria") === selectedCategory)
      .sort((a, b) => new Date(b.dueDate) - new Date(a.dueDate));
  }, [transactions, selectedCategory]);

  if (loading) return <p className="categorias__hint">Carregando...</p>;
  if (error) return <p className="categorias__error">{error}</p>;

  return (
    <div className="categorias">
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

      {selectedCategory && (
        <Card title={`Transações — ${selectedCategory}`}>
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
      )}
    </div>
  );
}
