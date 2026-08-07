import { useEffect, useMemo, useState } from "react";
import { getTransactionsReport, setTransactionDetails } from "../../../api/transactions.js";
import { getCategories } from "../../../api/categories.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import IconAvatar from "../../../components/ui/IconAvatar/IconAvatar.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import { TrendUpIcon, TrendDownIcon, WalletIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import { formatMonthLong } from "../../../utils/monthLabel.js";
import { getMonthsWindow } from "../../../utils/monthlyTrend.js";
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

// A Polp manda a descrição de transferências como "Transferência Recebida|Nome do titular"
// (ver TransferDetection no backend); separa em duas linhas legíveis em vez do texto cru.
function parseDescription(name) {
  const pipeIndex = name.indexOf("|");
  if (pipeIndex === -1) return { primary: name, secondary: null };
  return { primary: name.slice(0, pipeIndex).trim(), secondary: name.slice(pipeIndex + 1).trim() };
}

function formatGroupDate(dateStr) {
  const date = new Date(dateStr);
  const weekday = date.toLocaleDateString("pt-BR", { weekday: "short" }).replace(".", "");
  const full = date.toLocaleDateString("pt-BR");
  return `${weekday} · ${full}`;
}

// Agrupa linhas consecutivas do mesmo dia (a página já vem ordenada do backend) pra não repetir
// a data em cada linha e mostrar um subtotal de entradas/saídas por dia.
function groupByDate(items) {
  const groups = [];
  let current = null;

  for (const t of items) {
    const key = new Date(t.dueDate).toDateString();
    if (!current || current.key !== key) {
      current = { key, dueDate: t.dueDate, items: [], income: 0, expense: 0 };
      groups.push(current);
    }
    current.items.push(t);
    if (t.type === "Income") current.income += t.amount;
    else current.expense += t.amount;
  }

  return groups;
}

export default function Relatorios() {
  const { effectiveAccountId, dataRefreshKey } = useAccount();
  const [report, setReport] = useState(null);
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [typeFilter, setTypeFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState("all");
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState({ key: "dueDate", direction: "desc" });
  const [page, setPage] = useState(1);
  // "all" mantém o comportamento antigo (histórico completo, sem filtro de data). Sem isso os
  // cards de "Entradas/Saídas do período" somavam o histórico inteiro mesmo quando o rótulo
  // dizia "do período". Nada aqui limitava por mês.
  const [periodMode, setPeriodMode] = useState("all");
  const [monthOffset, setMonthOffset] = useState(0);
  const [editingId, setEditingId] = useState(null);
  const [editForm, setEditForm] = useState({ name: "", categoryId: "" });

  const currentMonth = useMemo(() => getMonthsWindow(1, monthOffset)[0], [monthOffset]);

  useEffect(() => {
    getCategories().then(setCategories).catch(() => {});
  }, []);

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

  function loadReport() {
    return getTransactionsReport({
      status: statusFilter === "all" ? undefined : statusFilter,
      type: typeFilter === "all" ? undefined : typeFilter,
      search: search || undefined,
      bankAccountId: effectiveAccountId,
      year: periodMode === "month" ? currentMonth.getUTCFullYear() : undefined,
      month: periodMode === "month" ? currentMonth.getUTCMonth() + 1 : undefined,
      sortBy: sort.key,
      sortDir: sort.direction,
      page,
      pageSize: PAGE_SIZE,
    });
  }

  useEffect(() => {
    let cancelled = false;

    loadReport()
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
    // loadReport não entra nas deps de propósito: ela fecha sobre os mesmos filtros já listados
    // aqui, mas é uma função nova a cada render: listar ela faria o efeito rodar de novo em
    // qualquer render (ex.: ao editar um lançamento), não só quando um filtro muda de verdade.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter, typeFilter, search, effectiveAccountId, dataRefreshKey, periodMode, currentMonth, sort, page]);

  function startEdit(t) {
    const match = categories.find((c) => c.name === t.categoryName);
    setEditForm({ name: t.name, categoryId: match ? match.id : "" });
    setEditingId(t.id);
  }

  function cancelEdit() {
    setEditingId(null);
  }

  async function saveEdit(id) {
    if (!editForm.name.trim()) return;
    try {
      await setTransactionDetails(id, { name: editForm.name.trim(), categoryId: editForm.categoryId || null });
      setEditingId(null);
      const data = await loadReport();
      setReport(data);
    } catch (err) {
      setError(err.message || "Não foi possível salvar a edição.");
    }
  }

  function handlePeriodModeChange(mode) {
    setPeriodMode(mode);
    setPage(1);
  }

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
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const totalIncome = report?.totalIncome ?? 0;
  const totalExpense = report?.totalExpense ?? 0;
  const balance = totalIncome - totalExpense;
  const groups = groupByDate(items);

  return (
    <div className="relatorios">
      <SectionHeading kicker="Histórico completo" title="Relatórios" align="left" />

      <div className="relatorios__period-filter">
        <button
          type="button"
          className={`relatorios__period-btn ${periodMode === "all" ? "relatorios__period-btn--active" : ""}`}
          onClick={() => handlePeriodModeChange("all")}
        >
          Todo o período
        </button>
        <button
          type="button"
          className={`relatorios__period-btn ${periodMode === "month" ? "relatorios__period-btn--active" : ""}`}
          onClick={() => handlePeriodModeChange("month")}
        >
          Por mês
        </button>
        {periodMode === "month" && (
          <RangePicker
            label={formatMonthLong(currentMonth)}
            onPrev={() => {
              setMonthOffset((prev) => prev + 1);
              setPage(1);
            }}
            onNext={() => {
              setMonthOffset((prev) => Math.max(0, prev - 1));
              setPage(1);
            }}
            nextDisabled={monthOffset === 0}
          />
        )}
      </div>

      <div className="relatorios__summary">
        <StatCard
          icon={<TrendUpIcon />}
          label={periodMode === "month" ? `Entradas em ${formatMonthLong(currentMonth)}` : "Entradas (todo o período)"}
          value={formatCurrency(totalIncome)}
          valueTone="income"
        />
        <StatCard
          icon={<TrendDownIcon />}
          label={periodMode === "month" ? `Saídas em ${formatMonthLong(currentMonth)}` : "Saídas (todo o período)"}
          value={formatCurrency(totalExpense)}
          valueTone="expense"
        />
        <StatCard
          icon={<WalletIcon />}
          label={periodMode === "month" ? `Saldo em ${formatMonthLong(currentMonth)}` : "Saldo (todo o período)"}
          value={formatCurrency(balance)}
          valueTone={balance >= 0 ? "income" : "expense"}
        />
      </div>

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
                <th aria-hidden="true"></th>
              </tr>
            </thead>
            <tbody>
              {groups.flatMap((group) => [
                <tr key={`group-${group.key}`} className="relatorios__group-row">
                  <td colSpan={COLUMNS.length + 1} className="relatorios__group-cell">
                    <span className="relatorios__group-date">{formatGroupDate(group.dueDate)}</span>
                    <span className="relatorios__group-totals">
                      {group.income > 0 && (
                        <span className="relatorios__amount--income">+{formatCurrency(group.income)}</span>
                      )}
                      {group.expense > 0 && (
                        <span className="relatorios__amount--expense">-{formatCurrency(group.expense)}</span>
                      )}
                    </span>
                  </td>
                </tr>,
                ...group.items.map((t) => {
                  const { primary, secondary } = parseDescription(t.name);
                  const isEditing = editingId === t.id;
                  return (
                    <tr key={t.id}>
                      <td className="relatorios__date-cell" aria-hidden="true"></td>
                      <td>
                        {isEditing ? (
                          <input
                            type="text"
                            className="relatorios__edit-input"
                            value={editForm.name}
                            onChange={(e) => setEditForm({ ...editForm, name: e.target.value })}
                            autoFocus
                          />
                        ) : (
                          <div className="relatorios__description">
                            <IconAvatar type={t.type === "Income" ? "income" : "expense"} />
                            <div className="relatorios__description-text">
                              <span className="relatorios__description-primary">
                                {primary}
                                {t.isTransfer && (
                                  <span
                                    className="relatorios__transfer-badge"
                                    title="Transferência entre suas próprias contas (mesmo titular nos dois lados): não entra nos totais de receita/despesa."
                                  >
                                    Transferência para mesma titularidade
                                  </span>
                                )}
                              </span>
                              {secondary && <span className="relatorios__description-secondary">{secondary}</span>}
                            </div>
                          </div>
                        )}
                      </td>
                      <td className="relatorios__truncate-cell" title={t.categoryName || "Sem categoria"}>
                        {isEditing ? (
                          <select
                            className="relatorios__edit-select"
                            value={editForm.categoryId}
                            onChange={(e) => setEditForm({ ...editForm, categoryId: e.target.value })}
                          >
                            <option value="">Sem categoria</option>
                            {categories.map((c) => (
                              <option key={c.id} value={c.id}>
                                {c.name}
                              </option>
                            ))}
                          </select>
                        ) : (
                          t.categoryName || "Sem categoria"
                        )}
                      </td>
                      <td className="relatorios__truncate-cell" title={t.bankAccountName || "-"}>
                        {t.bankAccountName || "-"}
                      </td>
                      <td>{TYPE_LABELS[t.type] || t.type}</td>
                      <td>
                        <StatusPill status={t.status}>{STATUS_LABELS[t.status] || t.status}</StatusPill>
                      </td>
                      <td
                        className={`relatorios__amount-cell ${
                          t.type === "Income" ? "relatorios__amount--income" : "relatorios__amount--expense"
                        }`}
                      >
                        {formatCurrency(t.amount)}
                      </td>
                      <td className="relatorios__actions-cell">
                        {isEditing ? (
                          <>
                            <button type="button" onClick={() => saveEdit(t.id)} aria-label="Salvar" title="Salvar">
                              ✓
                            </button>
                            <button type="button" onClick={cancelEdit} aria-label="Cancelar" title="Cancelar">
                              ×
                            </button>
                          </>
                        ) : (
                          <button type="button" onClick={() => startEdit(t)} aria-label="Editar" title="Editar nome/categoria">
                            ✎
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                }),
              ])}
              {items.length === 0 && (
                <tr>
                  <td colSpan={COLUMNS.length + 1} className="relatorios__empty">
                    Nenhuma transação encontrada.
                  </td>
                </tr>
              )}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={COLUMNS.length + 1}>Totais ({totalCount} lançamentos)</td>
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
