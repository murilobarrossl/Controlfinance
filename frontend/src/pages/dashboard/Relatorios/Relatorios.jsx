import { useEffect, useMemo, useState } from "react";
import { getTransactionsReport, setTransactionDetails } from "../../../api/transactions.js";
import { getCategories } from "../../../api/categories.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import IconAvatar from "../../../components/ui/IconAvatar/IconAvatar.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { formatCurrency, roundMoney } from "../../../utils/financeMath.js";
import { formatMonthLong } from "../../../utils/monthLabel.js";
import { getMonthsWindow } from "../../../utils/monthlyTrend.js";
import { resolveCategoryColor, categoryInitials } from "../../../utils/categoryColors.js";
import "./Relatorios.css";
import usePageTitle from "../../../hooks/usePageTitle.js";

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

const PERIOD_HINTS = {
  all: "Todo o período: mostra todos os lançamentos desde a primeira sincronização da conta selecionada, sem filtro de data. Os totais abaixo somam o histórico inteiro.",
  month:
    "Por mês: mostra só os lançamentos do mês escolhido (use as setas para navegar entre meses). Os totais abaixo somam apenas aquele mês.",
};

// Etiqueta colorida por categoria: mesma cor da categoria no Dashboard e em Categorias, com as
// iniciais pra continuar distinguível mesmo entre duas cores parecidas.
function CategoryTag({ name, categories }) {
  if (!name) return <span className="relatorios__category relatorios__category--empty">Sem categoria</span>;
  const color = resolveCategoryColor(name, categories);
  return (
    <span
      className="relatorios__category"
      style={{ "--category-color": color }}
    >
      <span className="relatorios__category-badge" aria-hidden="true">
        {categoryInitials(name)}
      </span>
      <span className="relatorios__category-name">{name}</span>
    </span>
  );
}

// A Polp manda a descrição de transferências como "Transferência Recebida|Nome do titular"
// (ver TransferDetection no backend); separa em duas linhas legíveis em vez do texto cru.
function parseDescription(name) {
  const pipeIndex = name.indexOf("|");
  if (pipeIndex === -1) return { primary: name, secondary: null };
  return { primary: name.slice(0, pipeIndex).trim(), secondary: name.slice(pipeIndex + 1).trim() };
}

function formatGroupDate(dateStr) {
  const date = new Date(dateStr);
  const weekday = date.toLocaleDateString("pt-BR", { weekday: "short", timeZone: "UTC" }).replace(".", "");
  const full = date.toLocaleDateString("pt-BR", { timeZone: "UTC" });
  return `${weekday} · ${full}`;
}

// Agrupa linhas consecutivas do mesmo dia (a página já vem ordenada do backend) pra não repetir
// a data em cada linha e mostrar um subtotal de entradas/saídas por dia.
function groupByDate(items) {
  const groups = [];
  let current = null;

  for (const t of items) {
    const key = t.dueDate.slice(0, 10);
    if (!current || current.key !== key) {
      current = { key, dueDate: t.dueDate, items: [], income: 0, expense: 0 };
      groups.push(current);
    }
    current.items.push(t);
    if (!t.isTransfer) {
      if (t.type === "Income") current.income += t.amount;
      else current.expense += t.amount;
    }
  }

  return groups;
}

export default function Relatorios() {
  usePageTitle("Relatórios");
  const { effectiveAccountId, dataRefreshKey, accountsStatus } = useAccount();
  const [result, setResult] = useState(null);
  const [categories, setCategories] = useState([]);
  const [saveError, setSaveError] = useState("");
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
  const [reloadKey, setReloadKey] = useState(0);
  const [editingId, setEditingId] = useState(null);
  const [editForm, setEditForm] = useState({ name: "", categoryId: "" });

  const currentMonth = useMemo(() => getMonthsWindow(1, monthOffset)[0], [monthOffset]);

  useEffect(() => {
    const controller = new AbortController();
    getCategories({ signal: controller.signal })
      .then(setCategories)
      .catch(() => {});
    return () => controller.abort();
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

  const filters = useMemo(
    () => ({
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
    }),
    [statusFilter, typeFilter, search, effectiveAccountId, periodMode, currentMonth, sort, page]
  );
  const requestKey = `${JSON.stringify(filters)}|${dataRefreshKey}|${reloadKey}`;
  const loading = accountsStatus !== "ready" || result?.key !== requestKey;

  useEffect(() => {
    if (accountsStatus !== "ready") return;

    const controller = new AbortController();
    const { signal } = controller;

    getTransactionsReport(filters, { signal })
      .then((data) => {
        if (signal.aborted) return;
        setResult({ key: requestKey, report: data, error: "" });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ key: requestKey, report: null, error: err.message || "Não foi possível carregar os relatórios." });
      });

    return () => controller.abort();
  }, [filters, requestKey, accountsStatus]);

  function startEdit(t) {
    const match = categories.find((c) => c.name === t.categoryName);
    setEditForm({ name: t.name, categoryId: match ? match.id : "" });
    setEditingId(t.id);
    setSaveError("");
  }

  function cancelEdit() {
    setEditingId(null);
  }

  async function saveEdit(id) {
    if (!editForm.name.trim()) return;
    try {
      await setTransactionDetails(id, { name: editForm.name.trim(), categoryId: editForm.categoryId || null });
      setEditingId(null);
      setSaveError("");
      setReloadKey((k) => k + 1);
    } catch (err) {
      setSaveError(err.message || "Não foi possível salvar a edição.");
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

  const error =
    accountsStatus === "error"
      ? "Não foi possível carregar as contas."
      : result?.key === requestKey
        ? result.error
        : "";
  if (error) return <p className="relatorios__error">{error}</p>;

  const report = loading ? null : result.report;
  const items = report?.items ?? [];
  const totalCount = report?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const totalIncome = report?.totalIncome;
  const totalExpense = report?.totalExpense;
  const balance = loading ? null : roundMoney(totalIncome - totalExpense);
  const groups = groupByDate(items);
  const amountSkeleton = <Skeleton width="7rem" height="1.5rem" />;

  return (
    <div className="relatorios">
      <SectionHeading title="Relatórios" level={1}>Histórico completo</SectionHeading>

      {saveError && <p className="relatorios__error">{saveError}</p>}

      <div className="relatorios__period-filter">
        <button
          type="button"
          className={`relatorios__chip ${periodMode === "all" ? "relatorios__chip--active" : ""}`}
          onClick={() => handlePeriodModeChange("all")}
          title={PERIOD_HINTS.all}
          aria-describedby="relatorios-period-hint"
        >
          Todo o período
        </button>
        <button
          type="button"
          className={`relatorios__chip ${periodMode === "month" ? "relatorios__chip--active" : ""}`}
          onClick={() => handlePeriodModeChange("month")}
          title={PERIOD_HINTS.month}
          aria-describedby="relatorios-period-hint"
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
      <p id="relatorios-period-hint" className="relatorios__period-hint">
        {PERIOD_HINTS[periodMode]}
      </p>

      <div className="relatorios__totals-bar">
        <div className="relatorios__totals-item">
          <span className="relatorios__totals-label">
            {periodMode === "month" ? `Entradas em ${formatMonthLong(currentMonth)}` : "Entradas (todo o período)"}
          </span>
          <span className="relatorios__totals-value relatorios__amount--income">
            {loading ? amountSkeleton : formatCurrency(totalIncome)}
          </span>
        </div>
        <div className="relatorios__totals-divider" aria-hidden="true" />
        <div className="relatorios__totals-item">
          <span className="relatorios__totals-label">
            {periodMode === "month" ? `Saídas em ${formatMonthLong(currentMonth)}` : "Saídas (todo o período)"}
          </span>
          <span className="relatorios__totals-value relatorios__amount--expense">
            {loading ? amountSkeleton : formatCurrency(totalExpense)}
          </span>
        </div>
        <div className="relatorios__totals-divider" aria-hidden="true" />
        <div className="relatorios__totals-item">
          <span className="relatorios__totals-label">
            {periodMode === "month" ? `Saldo em ${formatMonthLong(currentMonth)}` : "Saldo (todo o período)"}
          </span>
          <span
            className={`relatorios__totals-value ${
              balance === null || balance >= 0 ? "relatorios__amount--income" : "relatorios__amount--expense"
            }`}
          >
            {loading ? amountSkeleton : formatCurrency(balance)}
          </span>
        </div>
      </div>

      <div className="relatorios__table-panel">
        <div className="relatorios__toolbar">
          <input
            type="text"
            placeholder="Buscar por descrição..."
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            className="relatorios__search"
          />

          <div className="relatorios__chip-group" role="group" aria-label="Filtrar por tipo">
            {[
              { value: "Income", label: "Receitas" },
              { value: "Expense", label: "Despesas" },
            ].map((opt) => (
              <button
                key={opt.value}
                type="button"
                className={`relatorios__chip ${typeFilter === opt.value ? "relatorios__chip--active" : ""}`}
                onClick={() => handleTypeFilterChange(typeFilter === opt.value ? "all" : opt.value)}
              >
                {opt.label}
              </button>
            ))}
          </div>

          <div className="relatorios__chip-group" role="group" aria-label="Filtrar por status">
            {[
              { value: "Pending", label: "Pendente" },
              { value: "Paid", label: "Pago" },
              { value: "Overdue", label: "Atrasado" },
            ].map((opt) => (
              <button
                key={opt.value}
                type="button"
                className={`relatorios__chip ${statusFilter === opt.value ? "relatorios__chip--active" : ""}`}
                onClick={() => handleStatusFilterChange(statusFilter === opt.value ? "all" : opt.value)}
              >
                {opt.label}
              </button>
            ))}
          </div>
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
                          <CategoryTag name={t.categoryName} categories={categories} />
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
              {loading && (
                <tr>
                  <td colSpan={COLUMNS.length + 1} className="relatorios__empty">
                    <Skeleton width="100%" height="8rem" />
                  </td>
                </tr>
              )}
              {!loading && items.length === 0 && (
                <tr>
                  <td colSpan={COLUMNS.length + 1} className="relatorios__empty">
                    Nenhuma transação encontrada.
                  </td>
                </tr>
              )}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={COLUMNS.length + 1}>
                  Totais ({loading ? <Skeleton width="2rem" /> : totalCount} lançamentos)
                </td>
              </tr>
            </tfoot>
          </table>
        </div>

        <div className="relatorios__pagination">
          <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} aria-label="Página anterior">
            ‹
          </button>
          <span>
            Página {page} de {loading ? <Skeleton width="1.5rem" /> : totalPages}
          </span>
          <button
            type="button"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={loading || page >= totalPages}
            aria-label="Próxima página"
          >
            ›
          </button>
        </div>
      </div>
    </div>
  );
}
