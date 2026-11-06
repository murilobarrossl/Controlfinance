import { useEffect, useMemo, useState } from "react";
import { getTransactionsReport, setTransactionDetails } from "../../../api/transactions.js";
import { getCategories } from "../../../api/categories.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { CloseIcon, ChevronDownIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, roundMoney } from "../../../utils/financeMath.js";
import { formatMonthLong } from "../../../utils/monthLabel.js";
import { getMonthsWindow } from "../../../utils/monthlyTrend.js";
import { resolveCategoryColor, categoryInitials } from "../../../utils/categoryColors.js";
import "./Relatorios.css";
import usePageTitle from "../../../hooks/usePageTitle.js";

const ICON_PROPS = {
  width: 16,
  height: 16,
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 1.6,
  strokeLinecap: "round",
  strokeLinejoin: "round",
  "aria-hidden": "true",
};

function PencilIcon() {
  return (
    <svg {...ICON_PROPS}>
      <path d="M4 20h4L19 9a2.8 2.8 0 0 0-4-4L4 16v4z" />
      <path d="M13.5 6.5l4 4" />
    </svg>
  );
}

function CheckIcon() {
  return (
    <svg {...ICON_PROPS}>
      <path d="M5 12.5l4.5 4.5L19 7.5" />
    </svg>
  );
}

const UNKNOWN_CATEGORY = "__unknown__";

const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };
const PAGE_SIZE = 25;
const SEARCH_DEBOUNCE_MS = 350;

const COLUMNS = [
  { key: "dueDate", label: "Data" },
  { key: "name", label: "Descrição" },
  { key: "categoryName", label: "Categoria" },
  { key: "bankAccountName", label: "Conta" },
  { key: "status", label: "Status" },
  { key: "amount", label: "Valor" },
];

// A ordenação clicando no cabeçalho some quando a tabela vira lista (telas estreitas); este seletor
// a substitui ali. Status fica de fora: a ordem dele segue o enum do servidor, não algo legível, e o
// filtro de status já cobre o uso.
const SORT_OPTIONS = [
  { value: "dueDate:desc", label: "Data: mais recentes" },
  { value: "dueDate:asc", label: "Data: mais antigos" },
  { value: "amount:desc", label: "Valor: maior primeiro" },
  { value: "amount:asc", label: "Valor: menor primeiro" },
  { value: "name:asc", label: "Descrição: A–Z" },
  { value: "name:desc", label: "Descrição: Z–A" },
  { value: "categoryName:asc", label: "Categoria: A–Z" },
  { value: "categoryName:desc", label: "Categoria: Z–A" },
  { value: "bankAccountName:asc", label: "Conta: A–Z" },
  { value: "bankAccountName:desc", label: "Conta: Z–A" },
];

const PERIOD_HINTS = {
  all: "Todos os lançamentos desde a primeira sincronização da conta selecionada.",
  month: "Só os lançamentos do mês escolhido. Use as setas para trocar de mês.",
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

// Sinal de menos tipográfico (U+2212), da mesma largura do "+": os valores alinham na coluna e o
// leitor de tela lê "menos" em vez de "hífen".
function formatSigned(value, negative) {
  return `${negative ? "\u2212" : "+"}${formatCurrency(Math.abs(value))}`;
}

function formatSyncedAt(value) {
  return new Date(value).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });
}

function formatRowDate(dateStr) {
  return new Date(dateStr).toLocaleDateString("pt-BR", { timeZone: "UTC" });
}

// Só agrupa com a lista em ordem de data; o subtotal vem do servidor (dayTotals), somado sobre o
// filtro inteiro. Somar aqui usaria só as linhas desta página e erraria o dia que continua na seguinte.
function groupByDate(items, dayTotals) {
  const totalsByDay = new Map((dayTotals ?? []).map((d) => [d.date.slice(0, 10), d]));
  const groups = [];
  let current = null;

  for (const t of items) {
    const key = t.dueDate.slice(0, 10);
    if (!current || current.key !== key) {
      current = { key, dueDate: t.dueDate, items: [], totals: totalsByDay.get(key) ?? null };
      groups.push(current);
    }
    current.items.push(t);
  }

  return groups;
}

export default function Relatorios() {
  usePageTitle("Relatórios");
  const { accounts, effectiveAccountId, dataRefreshKey, accountsStatus, bumpDataRefresh } = useAccount();
  const [result, setResult] = useState(null);
  const [categories, setCategories] = useState([]);
  const [categoriesStatus, setCategoriesStatus] = useState("loading");
  const [categoriesReloadKey, setCategoriesReloadKey] = useState(0);
  const [saveError, setSaveError] = useState("");
  const [saving, setSaving] = useState(false);
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
  const [editForm, setEditForm] = useState({ name: "", suffix: "", categoryId: "", categoryKnown: true, categoryTouched: false });

  const currentMonth = useMemo(() => getMonthsWindow(1, monthOffset)[0], [monthOffset]);

  // Sem a lista de categorias o seletor da edição mostraria "Sem categoria" e salvar só o nome
  // apagaria a categoria do lançamento (o PUT grava o categoryId que receber). Por isso a edição
  // fica bloqueada até a lista chegar, e falha não é engolida.
  useEffect(() => {
    const controller = new AbortController();
    setCategoriesStatus("loading");
    getCategories({ signal: controller.signal })
      .then((list) => {
        setCategories(list);
        setCategoriesStatus("ready");
      })
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setCategoriesStatus("error");
      });
    return () => controller.abort();
  }, [categoriesReloadKey]);

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
  const baseKey = `${JSON.stringify(filters)}|${dataRefreshKey}`;
  const requestKey = `${baseKey}|${reloadKey}`;
  const loading = accountsStatus !== "ready" || result?.key !== requestKey;

  useEffect(() => {
    if (accountsStatus !== "ready") return;

    const controller = new AbortController();
    const { signal } = controller;

    getTransactionsReport(filters, { signal })
      .then((data) => {
        if (signal.aborted) return;
        setResult({ key: requestKey, baseKey, report: data, error: "" });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ key: requestKey, baseKey, report: null, error: err.message || "Não foi possível carregar o extrato." });
      });

    return () => controller.abort();
  }, [filters, baseKey, requestKey, accountsStatus]);

  function startEdit(t) {
    if (categoriesStatus !== "ready") return;
    const match = categories.find((c) => c.name === t.categoryName);
    // "Tipo|Titular" (transferências): só a parte principal é editável; o "|Titular" volta intacto
    // ao salvar, pra a pessoa não apagar o nome do titular sem saber que ele estava ali.
    const pipeIndex = t.name.indexOf("|");
    setEditForm({
      name: pipeIndex === -1 ? t.name : t.name.slice(0, pipeIndex).trim(),
      suffix: pipeIndex === -1 ? "" : t.name.slice(pipeIndex),
      categoryId: match ? match.id : t.categoryName ? UNKNOWN_CATEGORY : "",
      categoryKnown: !t.categoryName || Boolean(match),
      categoryTouched: false,
    });
    setEditingId(t.id);
    setSaveError("");
  }

  function cancelEdit() {
    if (saving) return;
    setEditingId(null);
    setSaveError("");
  }

  async function saveEdit(id) {
    if (saving) return;
    const name = editForm.name.trim();
    if (!name) {
      setSaveError("O nome não pode ficar vazio.");
      return;
    }
    if (name.includes("|")) {
      setSaveError("O nome não pode ter o caractere |.");
      return;
    }
    if (!editForm.categoryKnown && !editForm.categoryTouched) {
      setSaveError("Não encontramos a categoria atual deste lançamento. Escolha a categoria antes de salvar.");
      return;
    }
    setSaving(true);
    try {
      await setTransactionDetails(id, { name: `${name}${editForm.suffix}`, categoryId: editForm.categoryId || null });
      setEditingId(null);
      setSaveError("");
      setReloadKey((k) => k + 1);
    } catch (err) {
      setSaveError(err.message || "Não foi possível salvar a edição. Tente de novo.");
    } finally {
      setSaving(false);
    }
  }

  function handleEditKeyDown(e, id) {
    if (e.key === "Enter") {
      e.preventDefault();
      saveEdit(id);
    } else if (e.key === "Escape") {
      e.preventDefault();
      cancelEdit();
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

  function handleSortSelect(value) {
    const [key, direction] = value.split(":");
    setSort({ key, direction });
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

  function clearFilters() {
    setSearchInput("");
    setSearch("");
    setTypeFilter("all");
    setStatusFilter("all");
    setPage(1);
  }

  // Na tabela o lápis tem coluna própria na borda esquerda; na lista do celular fica colado ao nome.
  // O CSS mostra só um dos dois conforme a largura (o outro some também para o leitor de tela).
  function renderEditTrigger(t, primary, extraClass = "") {
    return (
      <button
        type="button"
        className={`relatorios__row-action relatorios__edit-trigger ${extraClass}`.trim()}
        onClick={() => startEdit(t)}
        disabled={editBlocked}
        aria-label={`Editar nome e categoria de ${primary}`}
        title={
          categoriesStatus === "loading"
            ? "Carregando categorias…"
            : categoriesStatus === "error"
              ? "Edição indisponível: as categorias não carregaram"
              : "Editar nome e categoria"
        }
      >
        <PencilIcon />
      </button>
    );
  }

  // A última palavra vai junto com o lápis (sem quebra entre os dois), pra o lápis nunca cair sozinho
  // numa linha. Palavra longa demais (código de banco) continua podendo quebrar, senão estoura a tela.
  function renderNameWithTrigger(t, primary) {
    const cut = primary.lastIndexOf(" ");
    const head = cut === -1 ? "" : primary.slice(0, cut + 1);
    const last = cut === -1 ? primary : primary.slice(cut + 1);
    const trigger = renderEditTrigger(t, primary, "relatorios__edit-trigger--inline");
    return (
      <span className="relatorios__name">
        {head}
        {last.length <= 20 ? (
          <span className="relatorios__name-tail">
            {last}
            {trigger}
          </span>
        ) : (
          <>
            {last}
            {trigger}
          </>
        )}
      </span>
    );
  }

  function renderFailure(message, onRetry) {
    return (
      <div className="relatorios__failure" role="alert">
        <p className="relatorios__error">{message}</p>
        <p className="relatorios__failure-hint">Verifique sua conexão com a internet e tente de novo.</p>
        <button type="button" className="relatorios__retry" onClick={onRetry}>
          Tentar de novo
        </button>
      </div>
    );
  }

  if (accountsStatus === "error") return renderFailure("Não foi possível carregar suas contas.", bumpDataRefresh);
  // Falha do extrato fica no lugar da tabela, não da página: o seletor de período continua à mão
  // pra sair de um mês que falhou sem precisar recarregar tudo.
  const reportError = result?.key === requestKey ? result.error : "";

  const monthLabel = formatMonthLong(currentMonth);
  const periodSuffix = periodMode === "month" ? `em ${monthLabel}` : "(todo o período)";
  const hasFilters = Boolean(search) || typeFilter !== "all" || statusFilter !== "all";
  const incomeFiltered = typeFilter === "Expense";
  const expenseFiltered = typeFilter === "Income";
  const totalsNote = [
    hasFilters ? "Somas só dos lançamentos que aparecem com a busca e os filtros atuais." : null,
    statusFilter === "all" ? "Lançamentos pendentes e atrasados entram nas somas." : null,
    "Transferências entre suas próprias contas não entram.",
  ]
    .filter(Boolean)
    .join(" ");
  const account = accounts.find((a) => a.id === effectiveAccountId);
  const syncNote = !account?.polpIntegrationId
    ? null
    : account.lastSyncedAt
      ? `Dados do banco atualizados em ${formatSyncedAt(account.lastSyncedAt)}.`
      : "Aguardando a primeira sincronização com o banco.";

  const report = loading ? null : result.report;
  // Recarga da mesma consulta (só depois de salvar uma edição, que não muda valores): mantém as
  // linhas visíveis, esmaecidas. Página ou filtro novos nunca reaproveitam linhas de outra consulta.
  const refreshingSameQuery = loading && !result?.error && result?.baseKey === baseKey && Boolean(result?.report);
  const visibleReport = refreshingSameQuery ? result.report : report;
  const items = visibleReport?.items ?? [];
  const placeholderRows = Math.min(PAGE_SIZE, Math.max(3, result?.report?.items?.length ?? 8));
  const totalCount = report?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const totalIncome = report?.totalIncome;
  const totalExpense = report?.totalExpense;
  const balance = loading ? null : roundMoney(totalIncome - totalExpense);
  const groupedByDay = sort.key === "dueDate";
  const groups = groupedByDay ? groupByDate(items, report?.dayTotals) : [{ key: "all", items, totals: null }];
  const editBlocked = loading || categoriesStatus !== "ready";
  const amountSkeleton = <Skeleton width="7rem" height="1.5rem" />;

  return (
    <div className="relatorios">
      <SectionHeading title="Relatórios" level={1}>O extrato da sua conta, lançamento por lançamento.</SectionHeading>

      <div className="relatorios__scope">
        <div className="relatorios__period-filter">
          <div className="relatorios__segmented" role="group" aria-label="Período">
            <button
              type="button"
              className={`relatorios__segment ${periodMode === "all" ? "relatorios__segment--active" : ""}`}
              aria-pressed={periodMode === "all"}
              onClick={() => handlePeriodModeChange("all")}
              aria-describedby="relatorios-period-hint"
            >
              Todo o período
            </button>
            <button
              type="button"
              className={`relatorios__segment ${periodMode === "month" ? "relatorios__segment--active" : ""}`}
              aria-pressed={periodMode === "month"}
              onClick={() => handlePeriodModeChange("month")}
              aria-describedby="relatorios-period-hint"
            >
              Por mês
            </button>
          </div>
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
          {syncNote && ` ${syncNote}`}
        </p>
      </div>

      {reportError ? (
        renderFailure(reportError, () => setReloadKey((k) => k + 1))
      ) : (
        <div className="relatorios__table-panel">
          {categoriesStatus === "error" && (
            <div className="relatorios__notice" role="alert">
              <p>Não foi possível carregar suas categorias, por isso a edição de lançamentos está pausada.</p>
              <button type="button" className="relatorios__retry" onClick={() => setCategoriesReloadKey((k) => k + 1)}>
                Tentar de novo
              </button>
            </div>
          )}
          <div className="relatorios__toolbar">
            <input
              type="text"
              placeholder="Buscar por descrição..."
              aria-label="Buscar lançamentos pela descrição"
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
                  aria-pressed={typeFilter === opt.value}
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
                  aria-pressed={statusFilter === opt.value}
                  onClick={() => handleStatusFilterChange(statusFilter === opt.value ? "all" : opt.value)}
                >
                  {opt.label}
                </button>
              ))}
            </div>

            <label className="relatorios__sort">
              <span>Ordenar por</span>
              <select value={`${sort.key}:${sort.direction}`} onChange={(e) => handleSortSelect(e.target.value)}>
                {!SORT_OPTIONS.some((o) => o.value === `${sort.key}:${sort.direction}`) && (
                  <option value={`${sort.key}:${sort.direction}`}>Status</option>
                )}
                {SORT_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="relatorios__totals">
            <div className="relatorios__totals-bar">
              <div className="relatorios__totals-item">
                <span className="relatorios__totals-label">Entradas {periodSuffix}</span>
                {incomeFiltered ? (
                  <span className="relatorios__totals-value relatorios__totals-value--muted">Fora do filtro</span>
                ) : (
                  <span className="relatorios__totals-value relatorios__amount--income">
                    {loading ? amountSkeleton : formatCurrency(totalIncome)}
                  </span>
                )}
              </div>
              <div className="relatorios__totals-item">
                <span className="relatorios__totals-label">Saídas {periodSuffix}</span>
                {expenseFiltered ? (
                  <span className="relatorios__totals-value relatorios__totals-value--muted">Fora do filtro</span>
                ) : (
                  <span className="relatorios__totals-value relatorios__amount--expense">
                    {loading ? amountSkeleton : formatCurrency(totalExpense)}
                  </span>
                )}
              </div>
              <div className="relatorios__totals-item">
                <span className="relatorios__totals-label">Entradas − saídas {periodSuffix}</span>
                {incomeFiltered || expenseFiltered ? (
                  <span className="relatorios__totals-value relatorios__totals-value--muted">Fora do filtro</span>
                ) : (
                  <span
                    className={`relatorios__totals-value ${
                      balance === null || balance >= 0 ? "relatorios__amount--income" : "relatorios__amount--expense"
                    }`}
                  >
                    {loading ? amountSkeleton : balance < 0 ? formatSigned(balance, true) : formatCurrency(balance)}
                  </span>
                )}
              </div>
            </div>
            <p className="relatorios__totals-note">{totalsNote}</p>
          </div>

          <div className="relatorios__table-wrapper">
            <table className="relatorios__table" role="table">
              <thead role="rowgroup">
                <tr role="row">
                  <th className="relatorios__edit-head" aria-hidden="true"></th>
                  {COLUMNS.map((col) => {
                    const active = sort.key === col.key;
                    return (
                      <th
                        role="columnheader"
                        key={col.key}
                        aria-sort={active ? (sort.direction === "asc" ? "ascending" : "descending") : "none"}
                        className={col.key === "amount" ? "relatorios__amount-head" : undefined}
                      >
                        <button type="button" className="relatorios__sort-button" onClick={() => toggleSort(col.key)}>
                          {col.label}
                          <span
                            className={`relatorios__sort-icon${active ? " relatorios__sort-icon--active" : ""}${
                              active && sort.direction === "asc" ? " relatorios__sort-icon--asc" : ""
                            }`}
                          >
                            <ChevronDownIcon />
                          </span>
                        </button>
                      </th>
                    );
                  })}
                </tr>
              </thead>
              <tbody
                role="rowgroup"
                aria-busy={loading}
                className={refreshingSameQuery ? "relatorios__tbody--refreshing" : undefined}
              >
                {groups.flatMap((group) => [
                  groupedByDay && (
                    <tr key={`group-${group.key}`} className="relatorios__group-row" role="row">
                      <td colSpan={COLUMNS.length} className="relatorios__group-date" role="cell">
                        {formatGroupDate(group.dueDate)}
                      </td>
                      <td className="relatorios__group-totals" role="cell">
                        {loading && <Skeleton width="5rem" />}
                        {group.totals?.income > 0 && (
                          <span className="relatorios__amount--income">{formatSigned(group.totals.income, false)}</span>
                        )}
                        {group.totals?.expense > 0 && (
                          <span className="relatorios__amount--expense">{formatSigned(group.totals.expense, true)}</span>
                        )}
                      </td>
                    </tr>
                  ),
                  ...group.items.map((t) => {
                    const { primary, secondary } = parseDescription(t.name);
                    const isEditing = editingId === t.id;
                    return (
                      <tr
                        key={t.id}
                        className={`relatorios__row${isEditing ? " relatorios__row--editing" : ""}${
                          groupedByDay ? "" : " relatorios__row--dated"
                        }`}
                        role="row"
                      >
                        <td className="relatorios__edit-cell" role="cell">
                          {!isEditing && renderEditTrigger(t, primary)}
                        </td>
                        {groupedByDay ? (
                          <td className="relatorios__date-cell" aria-hidden="true"></td>
                        ) : (
                          <td className="relatorios__date-cell relatorios__date-cell--filled" role="cell">
                            {formatRowDate(t.dueDate)}
                          </td>
                        )}
                        <td className="relatorios__cell-desc" role="cell">
                          {isEditing ? (
                            <div className="relatorios__edit-field">
                              <div className="relatorios__edit-line">
                                <input
                                  type="text"
                                  className="relatorios__edit-input"
                                  aria-label="Nome do lançamento"
                                  aria-invalid={Boolean(saveError) || undefined}
                                  aria-describedby={saveError ? `relatorios-edit-error-${t.id}` : undefined}
                                  value={editForm.name}
                                  onChange={(e) => setEditForm({ ...editForm, name: e.target.value })}
                                  onKeyDown={(e) => handleEditKeyDown(e, t.id)}
                                  disabled={saving}
                                  autoFocus
                                />
                                <button
                                  type="button"
                                  className="relatorios__row-action"
                                  onClick={() => saveEdit(t.id)}
                                  disabled={saving}
                                  aria-label={saving ? "Salvando" : "Salvar edição"}
                                  title="Salvar (Enter)"
                                >
                                  <CheckIcon />
                                </button>
                                <button
                                  type="button"
                                  className="relatorios__row-action relatorios__icon-close"
                                  onClick={cancelEdit}
                                  disabled={saving}
                                  aria-label="Cancelar edição"
                                  title="Cancelar (Esc)"
                                >
                                  <CloseIcon />
                                </button>
                              </div>
                              {editForm.suffix && (
                                <span className="relatorios__description-secondary">{editForm.suffix.slice(1).trim()}</span>
                              )}
                              {saveError && (
                                <p id={`relatorios-edit-error-${t.id}`} className="relatorios__edit-error" role="alert">
                                  {saveError}
                                </p>
                              )}
                            </div>
                          ) : (
                            <div className="relatorios__description">
                              <span className="relatorios__description-primary">
                                {renderNameWithTrigger(t, primary)}
                                {t.isTransfer && (
                                  <span
                                    className="relatorios__transfer-badge"
                                    title="Transferência entre suas próprias contas (mesmo titular nos dois lados): não entra nos totais de receita/despesa."
                                  >
                                    Entre suas contas
                                  </span>
                                )}
                              </span>
                              {secondary && <span className="relatorios__description-secondary">{secondary}</span>}
                            </div>
                          )}
                        </td>
                        <td
                          className="relatorios__truncate-cell relatorios__cell-cat"
                          role="cell"
                          title={t.categoryName || "Sem categoria"}
                        >
                          {isEditing ? (
                            <select
                              className="relatorios__edit-select"
                              aria-label="Categoria do lançamento"
                              onKeyDown={(e) => e.key === "Escape" && cancelEdit()}
                              disabled={saving}
                              value={editForm.categoryId}
                              onChange={(e) => setEditForm({ ...editForm, categoryId: e.target.value, categoryTouched: true })}
                            >
                              {!editForm.categoryKnown && (
                                <option value={UNKNOWN_CATEGORY} disabled>
                                  Categoria atual não encontrada
                                </option>
                              )}
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
                        <td
                          className="relatorios__truncate-cell relatorios__cell-acc"
                          role="cell"
                          title={t.bankAccountName || "Sem conta"}
                        >
                          {t.bankAccountName || "—"}
                        </td>
                        <td className="relatorios__cell-status" role="cell">
                          <StatusPill status={t.status}>{STATUS_LABELS[t.status] || t.status}</StatusPill>
                        </td>
                        <td
                          className={`relatorios__amount-cell${t.type === "Income" ? " relatorios__amount-cell--income" : ""}`}
                          role="cell"
                        >
                          {formatSigned(t.amount, t.type !== "Income")}
                        </td>
                      </tr>
                    );
                  }),
                ])}
                {loading &&
                  !refreshingSameQuery &&
                  Array.from({ length: placeholderRows }, (_, i) => (
                    <tr key={`placeholder-${i}`} className="relatorios__placeholder-row" role="row" aria-hidden="true">
                      <td colSpan={COLUMNS.length + 1}>
                        <Skeleton width="100%" height="1.6rem" />
                      </td>
                    </tr>
                  ))}
                {!loading && items.length === 0 && (
                  <tr role="row">
                    <td colSpan={COLUMNS.length + 1} className="relatorios__empty" role="cell">
                      {hasFilters ? (
                        <>
                          <p>Nenhum lançamento corresponde à busca e aos filtros atuais.</p>
                          <button type="button" className="relatorios__retry" onClick={clearFilters}>
                            Limpar busca e filtros
                          </button>
                        </>
                      ) : periodMode === "month" ? (
                        <p>Nenhum lançamento em {monthLabel}.</p>
                      ) : (
                        <p>Esta conta ainda não tem lançamentos.</p>
                      )}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          <div className="relatorios__pagination">
            <span className="relatorios__count">
              {loading ? <Skeleton width="2rem" /> : totalCount} lançamentos
            </span>
            <div className="relatorios__pager">
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
      )}
    </div>
  );
}
