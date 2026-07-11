import { useEffect, useMemo, useState } from "react";
import { getTransactions } from "../../../api/transactions.js";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import "./Relatorios.css";

const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };
const TYPE_LABELS = { Income: "Receita", Expense: "Despesa" };

const COLUMNS = [
  { key: "dueDate", label: "Data" },
  { key: "name", label: "Descrição" },
  { key: "categoryName", label: "Categoria" },
  { key: "bankAccountName", label: "Conta" },
  { key: "type", label: "Tipo" },
  { key: "status", label: "Status" },
  { key: "amount", label: "Valor" },
];

export default function Relatorios() {
  const [transactions, setTransactions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [typeFilter, setTypeFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState("all");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState({ key: "dueDate", direction: "desc" });

  useEffect(() => {
    getTransactions()
      .then(setTransactions)
      .catch((err) => setError(err.message || "Não foi possível carregar os relatórios."))
      .finally(() => setLoading(false));
  }, []);

  const filtered = useMemo(() => {
    return transactions
      .filter((t) => typeFilter === "all" || t.type === typeFilter)
      .filter((t) => statusFilter === "all" || t.status === statusFilter)
      .filter((t) => t.name.toLowerCase().includes(search.toLowerCase()))
      .sort((a, b) => {
        const dir = sort.direction === "asc" ? 1 : -1;
        const valueA = a[sort.key] ?? "";
        const valueB = b[sort.key] ?? "";
        if (sort.key === "dueDate") return (new Date(valueA) - new Date(valueB)) * dir;
        if (sort.key === "amount") return (valueA - valueB) * dir;
        return String(valueA).localeCompare(String(valueB), "pt-BR") * dir;
      });
  }, [transactions, typeFilter, statusFilter, search, sort]);

  const totals = filtered.reduce(
    (acc, t) => {
      if (t.type === "Income") acc.income += t.amount;
      else acc.expense += t.amount;
      return acc;
    },
    { income: 0, expense: 0 }
  );

  function toggleSort(key) {
    setSort((prev) =>
      prev.key === key ? { key, direction: prev.direction === "asc" ? "desc" : "asc" } : { key, direction: "asc" }
    );
  }

  if (loading) return <p className="relatorios__hint">Carregando...</p>;
  if (error) return <p className="relatorios__error">{error}</p>;

  return (
    <div className="relatorios">
      <SectionHeading kicker="Histórico completo" title="Relatórios" align="left" />

      <Card>
        <div className="relatorios__filters">
          <input
            type="text"
            placeholder="Buscar por descrição..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="relatorios__search"
          />

          <select value={typeFilter} onChange={(e) => setTypeFilter(e.target.value)} className="relatorios__select">
            <option value="all">Todos os tipos</option>
            <option value="Income">Receitas</option>
            <option value="Expense">Despesas</option>
          </select>

          <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} className="relatorios__select">
            <option value="all">Todos os status</option>
            <option value="Pending">Pendente</option>
            <option value="Paid">Pago</option>
            <option value="Overdue">Atrasado</option>
          </select>
        </div>

        <div className="relatorios__table-wrapper">
          <table className="relatorios__table">
            <thead>
              <tr>
                {COLUMNS.map((col) => (
                  <th key={col.key} onClick={() => toggleSort(col.key)}>
                    {col.label}
                    {sort.key === col.key && (sort.direction === "asc" ? " ▲" : " ▼")}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {filtered.map((t) => (
                <tr key={t.id}>
                  <td>{new Date(t.dueDate).toLocaleDateString("pt-BR")}</td>
                  <td>{t.name}</td>
                  <td>{t.categoryName || "Sem categoria"}</td>
                  <td>{t.bankAccountName || "—"}</td>
                  <td>{TYPE_LABELS[t.type] || t.type}</td>
                  <td>{STATUS_LABELS[t.status] || t.status}</td>
                  <td className={t.type === "Income" ? "relatorios__amount--income" : "relatorios__amount--expense"}>
                    {formatCurrency(t.amount)}
                  </td>
                </tr>
              ))}
              {filtered.length === 0 && (
                <tr>
                  <td colSpan={COLUMNS.length} className="relatorios__empty">
                    Nenhuma transação encontrada.
                  </td>
                </tr>
              )}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={4}>Totais ({filtered.length} lançamentos)</td>
                <td colSpan={1} className="relatorios__amount--income">{formatCurrency(totals.income)}</td>
                <td colSpan={1} className="relatorios__amount--expense">{formatCurrency(totals.expense)}</td>
                <td>{formatCurrency(totals.income - totals.expense)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </Card>
    </div>
  );
}
