import { useEffect, useState } from "react";
import { getTransactionsReport } from "../../../api/transactions.js";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import "./Relatorios.css";

const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };
const TYPE_LABELS = { Income: "Receita", Expense: "Despesa" };
const PAGE_SIZE = 25;
const SEARCH_DEBOUNCE_MS = 350;

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
  const [report, setReport] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [typeFilter, setTypeFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState("all");
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState({ key: "dueDate", direction: "desc" });
  const [page, setPage] = useState(1);

  // Busca com debounce: sem isso, cada tecla digitada dispararia uma requisição nova.
  // Reseta a página junto, no mesmo callback (filtro/status/tipo/ordenação já resetam a
  // página direto nos próprios handlers).
  useEffect(() => {
    const timeout = setTimeout(() => {
      setSearch(searchInput);
      setPage(1);
    }, SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timeout);
  }, [searchInput]);

  useEffect(() => {
    let cancelled = false;

    getTransactionsReport({
      status: statusFilter === "all" ? undefined : statusFilter,
      type: typeFilter === "all" ? undefined : typeFilter,
      search: search || undefined,
      sortBy: sort.key,
      sortDir: sort.direction,
      page,
      pageSize: PAGE_SIZE,
    })
      .then((data) => {
        if (!cancelled) setReport(data);
      })
      .catch((err) => {
        if (!cancelled) setError(err.message || "Não foi possível carregar os relatórios.");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [statusFilter, typeFilter, search, sort, page]);

  function toggleSort(key) {
    setSort((prev) =>
      prev.key === key ? { key, direction: prev.direction === "asc" ? "desc" : "asc" } : { key, direction: "asc" }
    );
    setPage(1);
  }

  function handleTypeFilterChange(value) {
    setTypeFilter(value);
    setPage(1);
  }

  function handleStatusFilterChange(value) {
    setStatusFilter(value);
    setPage(1);
  }

  if (loading && !report) return <p className="relatorios__hint">Carregando...</p>;
  if (error) return <p className="relatorios__error">{error}</p>;

  const items = report?.items ?? [];
  const totalCount = report?.totalCount ?? 0;
  const totalIncome = report?.totalIncome ?? 0;
  const totalExpense = report?.totalExpense ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <div className="relatorios">
      <SectionHeading kicker="Histórico completo" title="Relatórios" align="left" />

      <Card>
        <div className="relatorios__filters">
          <input
            type="text"
            placeholder="Buscar por descrição..."
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            className="relatorios__search"
          />

          <select
            value={typeFilter}
            onChange={(e) => handleTypeFilterChange(e.target.value)}
            className="relatorios__select"
          >
            <option value="all">Todos os tipos</option>
            <option value="Income">Receitas</option>
            <option value="Expense">Despesas</option>
          </select>

          <select
            value={statusFilter}
            onChange={(e) => handleStatusFilterChange(e.target.value)}
            className="relatorios__select"
          >
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
              {items.map((t) => (
                <tr key={t.id}>
                  <td>{new Date(t.dueDate).toLocaleDateString("pt-BR")}</td>
                  <td>{t.name}</td>
                  <td>{t.categoryName || "Sem categoria"}</td>
                  <td>{t.bankAccountName || "-"}</td>
                  <td>{TYPE_LABELS[t.type] || t.type}</td>
                  <td>{STATUS_LABELS[t.status] || t.status}</td>
                  <td className={t.type === "Income" ? "relatorios__amount--income" : "relatorios__amount--expense"}>
                    {formatCurrency(t.amount)}
                  </td>
                </tr>
              ))}
              {items.length === 0 && (
                <tr>
                  <td colSpan={COLUMNS.length} className="relatorios__empty">
                    Nenhuma transação encontrada.
                  </td>
                </tr>
              )}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={4}>Totais ({totalCount} lançamentos)</td>
                <td colSpan={1} className="relatorios__amount--income">{formatCurrency(totalIncome)}</td>
                <td colSpan={1} className="relatorios__amount--expense">{formatCurrency(totalExpense)}</td>
                <td>{formatCurrency(totalIncome - totalExpense)}</td>
              </tr>
            </tfoot>
          </table>
        </div>

        <div className="relatorios__pagination">
          <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} aria-label="Página anterior">
            ‹
          </button>
          <span>Página {page} de {totalPages}</span>
          <button
            type="button"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page >= totalPages}
            aria-label="Próxima página"
          >
            ›
          </button>
        </div>
      </Card>
    </div>
  );
}
