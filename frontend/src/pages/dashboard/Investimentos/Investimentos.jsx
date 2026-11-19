import { useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import { InstitutionLogo, InvestmentIcon } from "./InvestmentMarks.jsx";
import NoBankAccount from "../../../components/dashboard/NoBankAccount/NoBankAccount.jsx";
import { ChevronDownIcon, InfoIcon, CloseIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import { useAuth } from "../../../context/AuthContext.jsx";
import { formatExact, formatMoney, investmentPath, MODALITIES, normalizeInvestment } from "../../../utils/importedInvestments.js";
import { requestInvestmentsRefresh } from "../../../api/investments.js";
import usePlanning from "./usePlanning.js";
import useLinkedResources from "./useLinkedResources.js";
import { summarize, summaryNote } from "./planningSummary.js";
import useImportedInvestments from "./useImportedInvestments.js";
import { EmergencyReserveCard, GoalsCard } from "./PlanningCards.jsx";
import FinancingSimulator from "./FinancingSimulator.jsx";
import usePageTitle from "../../../hooks/usePageTitle.js";
import "./Investimentos.css";

function equitySummary(item) {
  if (item.family !== "variable-incomes") return {};
  const quantity = item.document?.availability?.quantity === "available" ? formatExact(item.document.values?.quantity) : null;
  return { quantity: quantity && quantity !== "Não informado" ? `${quantity} ${quantity === "1" ? "ação" : "ações"}` : null };
}
function Amounts({ item }) {
  if (!item.amounts.length) return "Não informado";
  return item.amounts.map((money, index) => <span className="inv-real__amount" key={index}>{formatMoney(money)}</span>);
}
const MANUAL_REFRESH_INTERVAL_MS = 30000;
const REFRESH_FEEDBACK_MS = 4000;
const PENDING_STATES = new Set(["checking", "awaiting_sync", "sync_delayed", "pending_authorization"]);
const dismissedKey = (owner) => `investmentsSyncNoteDismissed:${owner}`;
function readDismissed(owner) {
  try {
    const ids = JSON.parse(localStorage.getItem(dismissedKey(owner)) ?? "[]");
    return Array.isArray(ids) ? ids : [];
  } catch { return []; }
}
const listStateKey = (owner) => `investmentsListState:${owner}`;
function readListState(owner, scope) {
  try {
    const saved = JSON.parse(sessionStorage.getItem(listStateKey(owner)) ?? "null");
    return saved && saved.scope === (scope ?? null) ? saved : null;
  } catch { return null; }
}
function InvestmentRow({ item, onOpen }) {
  const equity = equitySummary(item);
  return <Link to={investmentPath(item)} className="inv-row__button" onClick={onOpen}>
    <InvestmentIcon item={item} />
    <span className="inv-row__titles"><span className="inv-row__name">{item.name}</span><span className="inv-row__institution"><InstitutionLogo institution={item.institution} /><span className="inv-row__institution-text">{item.institution.name}{item.accountNumber && <> · conta <span className="inv-row__account">{item.accountNumber}</span></>}{equity.quantity && <> · <span className="inv-row__account">{equity.quantity}</span></>}</span></span>
      {item.catalogPresence?.status === "not_reported_in_last_full_scan" && <span className="inv-row__flag">Não informado na última atualização · último saldo conhecido</span>}</span>
    <span className="inv-row__balance"><Amounts item={item} /></span>
    <span className="inv-row__chevron" aria-hidden="true"><ChevronDownIcon /></span>
  </Link>;
}
function ProductGroup({ modality, items, collapsed, onToggle, onOpen }) {
  return <section className={`inv-group${collapsed ? " inv-group--collapsed" : ""}`} aria-labelledby={`group-${modality.key}`}>
    <h3 id={`group-${modality.key}`} className="inv-group__header">
      <button type="button" className="inv-group__toggle" aria-expanded={!collapsed} aria-controls={`group-${modality.key}-list`} onClick={onToggle}>
        <span className="inv-group__title">{modality.groupLabel}</span>
        <span className="inv-group__count">{items.length} {items.length === 1 ? "produto" : "produtos"}</span>
        <span className="inv-group__chevron" aria-hidden="true"><ChevronDownIcon /></span>
      </button>
    </h3>
    <ul id={`group-${modality.key}-list`} className="inv-list" hidden={collapsed}>{items.map((item) => <li key={item.key} className="inv-row">
      <InvestmentRow item={item} onOpen={onOpen} />
    </li>)}</ul>
  </section>;
}
export default function Investimentos() {
  usePageTitle("Investimentos");
  const owner = useAuth()?.user?.id;
  const { accounts, connections: rawConnections, connectors, connectionsStatus, connectionsRefreshing,
    effectiveConnectionId, dataRefreshKey, bumpDataRefresh } = useAccount();
  const allConnections = useMemo(() => rawConnections.map((connection) => ({ ...connection,
    institutionLogoUrl: connectors.find((connector) => connector.id === connection.institutionId)?.logoUrl ?? null })), [rawConnections, connectors]);
  const planning = usePlanning();
  const [restored] = useState(() => readListState(owner, effectiveConnectionId));
  const catalog = useImportedInvestments(owner, dataRefreshKey, effectiveConnectionId, restored?.pages);
  const [collapsedGroups, setCollapsedGroups] = useState(() => new Set(restored?.collapsed ?? []));
  const toggleGroup = (key) => setCollapsedGroups((prev) => {
    const next = new Set(prev);
    if (!next.delete(key)) next.add(key);
    return next;
  });
  const pendingScroll = useRef(restored?.scrollY ?? null);
  const saveListState = () => {
    try {
      sessionStorage.setItem(listStateKey(owner), JSON.stringify({ scope: effectiveConnectionId ?? null, scrollY: window.scrollY,
        collapsed: [...collapsedGroups], pages: { investments: catalog.investments.pages, "reserved-balances": catalog["reserved-balances"].pages } }));
    } catch { /* indisponível */ }
  };
  const [dismissedState, setDismissed] = useState(null);
  // Chave da atualização pedida pelo botão: "concluída" é essa chave já em vigor e nada mais carregando.
  const [refreshRequest, setRefreshRequest] = useState(null);
  const [refreshNotice, setRefreshNotice] = useState(null);
  const lastManualRefresh = useRef(0);
  const [requesting, setRequesting] = useState(false);
  // O intervalo vale no servidor (InvestmentRefreshController), que sobrevive a sair e voltar da aba;
  // a checagem local só poupa a chamada quando já se sabe a resposta.
  const requestRefresh = async () => {
    if (requesting) return;
    if (Date.now() - lastManualRefresh.current < MANUAL_REFRESH_INTERVAL_MS) { setRefreshNotice("Dados atualizados há pouco"); return; }
    setRequesting(true);
    setRefreshNotice(null);
    try {
      await requestInvestmentsRefresh();
      lastManualRefresh.current = Date.now();
      setRefreshRequest(dataRefreshKey + 1);
      bumpDataRefresh();
    } catch (error) {
      setRefreshNotice(error.status === 429 ? "Dados atualizados há pouco" : error.status === 503 ? "Muitos acessos agora. Tente em instantes." : "Não foi possível atualizar agora");
    } finally {
      setRequesting(false);
    }
  };
  const dismissed = dismissedState?.owner === owner ? dismissedState.ids : readDismissed(owner);
  const dismissSyncNote = () => {
    const ids = allConnections.map((c) => c.id);
    try { localStorage.setItem(dismissedKey(owner), JSON.stringify(ids)); } catch { /* indisponível */ }
    setDismissed({ owner, ids });
  };
  const connections = useMemo(() => effectiveConnectionId ? allConnections.filter((c) => c.id === effectiveConnectionId) : allConnections,
    [allConnections, effectiveConnectionId]);
  const investmentData = catalog.investments.data;
  const reservedData = catalog["reserved-balances"].data;
  const items = useMemo(() => {
    const numbers = new Map(accounts.map((account) => [account.id, account.number]));
    const reserves = reservedData
      .map((row) => ({ ...normalizeInvestment(row, true, allConnections), accountNumber: numbers.get(row.bankAccountId) ?? null }))
      .sort((a, b) => (a.accountNumber ?? "").localeCompare(b.accountNumber ?? ""));
    return [...investmentData.map((row) => normalizeInvestment(row, false, allConnections)), ...reserves];
  }, [investmentData, reservedData, allConnections, accounts]);
  const groups = Object.values(MODALITIES).map((modality) => ({ modality, items: items.filter((item) => item.modality.key === modality.key) })).filter((group) => group.items.length);
  const resolve = useLinkedResources(planning, items, allConnections, `${owner}:${dataRefreshKey}:${catalog.refreshSequence}`);
  const reserveEntries = resolve(planning.reserve);
  const goalEntries = Object.fromEntries(planning.goals.map((goal) => [goal.id, resolve(goal)]));
  const reserveSummary = summarize(reserveEntries);
  const datasets = [catalog.investments, catalog["reserved-balances"]];
  const loading = datasets.some((dataset) => dataset.status === "loading") || connectionsStatus === "loading";
  const failed = datasets.some((dataset) => dataset.status === "error") || connectionsStatus === "error";
  const limited = datasets.some((dataset) => dataset.limited);
  const busy = datasets.some((dataset) => dataset.busy);
  const refreshing = requesting || connectionsRefreshing || datasets.some((dataset) => dataset.refreshing || dataset.status === "loading");
  const refreshDone = refreshRequest !== null && refreshRequest === dataRefreshKey && !refreshing;
  useEffect(() => {
    if (!refreshDone) return;
    const timer = setTimeout(() => setRefreshRequest(null), REFRESH_FEEDBACK_MS);
    return () => clearTimeout(timer);
  }, [refreshDone]);
  useEffect(() => {
    if (!refreshNotice) return;
    const timer = setTimeout(() => setRefreshNotice(null), REFRESH_FEEDBACK_MS);
    return () => clearTimeout(timer);
  }, [refreshNotice]);
  useEffect(() => {
    if (pendingScroll.current === null || loading || refreshing) return;
    const top = pendingScroll.current;
    pendingScroll.current = null;
    try { sessionStorage.removeItem(listStateKey(owner)); } catch { /* indisponível */ }
    window.scrollTo({ top, behavior: "instant" });
  }, [loading, refreshing, owner]);
  const confirmedEmpty = !loading && !failed && !connectionsRefreshing && !items.length && connections.length > 0
    && datasets.every((dataset) => !dataset.cursor && !dataset.refreshing)
    && connections.every((connection) => connection.status === "none_reported" && connection.reservedStatus === "none_reported");
  const reserveValue = planning.status === "loading" || reserveSummary.loading ? <Skeleton width="7rem" height="1.75rem" />
    : planning.status === "error" || reserveSummary.failed ? "Indisponível"
    : !reserveSummary.count ? <span className="inv-summary__empty">Nenhum investimento</span>
    : reserveSummary.totals.length ? reserveSummary.totals.map((money) => <span className="inv-real__amount" key={money.currency}>{formatMoney(money)}</span>) : "Não informado";
  if (allConnections.length === 0) {
    if (connectionsStatus === "loading" || connectionsRefreshing)
      return <Skeleton width="100%" height="12rem" />;
    return <NoBankAccount />;
  }
  const reserveNote = reserveSummary.count && !reserveSummary.loading ? summaryNote(reserveSummary) : null;
  const emptyTitle = failed ? "Não foi possível carregar os investimentos" : confirmedEmpty ? "Nenhum investimento informado pelo banco" : connections.length ? "Ainda não há investimentos para mostrar" : "Nenhuma conexão bancária encontrada";
  const emptyText = failed ? "Tente atualizar os dados novamente." : confirmedEmpty ? "Produtos que não foram compartilhados pelo banco não aparecem aqui, o que não significa saldo zero." : connections.length ? "Eles aparecem aqui assim que o banco enviar os dados." : "Os investimentos aparecem depois que um banco é conectado e envia os dados.";
  return <div className="investimentos">
    <header className="investimentos__header">
      <div className="inv-real__heading"><SectionHeading title="Investimentos" level={1} /><div className="investimentos__refresh"><span className="investimentos__refresh-status" role="status">{refreshNotice ?? (refreshDone && !failed ? "Dados atualizados" : "")}</span>
        <Button as="button" type="button" variant="outline" size="sm" onClick={requestRefresh} disabled={refreshing} aria-busy={refreshing}>{refreshing ? "Atualizando…" : "Atualizar dados"}</Button></div></div>
      {connections.some((c) => PENDING_STATES.has(c.status) || PENDING_STATES.has(c.reservedStatus))
        && allConnections.some((c) => !dismissed.includes(c.id))
        && <div className="investimentos__sync-note" role="status"><InfoIcon /><p>Estamos recebendo seus investimentos do banco. Alguns podem levar um tempo para aparecer.</p>
          <button type="button" className="investimentos__sync-note-close" aria-label="Fechar aviso" onClick={dismissSyncNote}><CloseIcon /></button></div>}
      {(failed || limited || busy) && <div className="investimentos__failure" role="alert"><p>{limited ? "Muitas atualizações em pouco tempo. Os valores exibidos continuam com a data em que foram informados; tente de novo em instantes."
        : busy ? "Muitos acessos neste momento. Os valores exibidos continuam com a data em que foram informados; tente de novo em instantes."
        : "Não foi possível atualizar parte dos dados. Os valores exibidos mantêm a data em que foram informados."}</p><Button as="button" type="button" variant="outline" size="sm" onClick={requestRefresh} disabled={refreshing}>Tentar novamente</Button></div>}
      <dl className="inv-summary">
        <div className="inv-summary__item"><dt>Reserva de emergência</dt>
          <dd className="inv-summary__value">{reserveValue}</dd>
          {planning.status === "ready" && reserveNote && <dd className="inv-summary__note">{reserveNote}</dd>}</div>
        <div className="inv-summary__item"><dt>Investimentos carregados</dt>
          <dd className="inv-summary__value">{loading ? <Skeleton width="3rem" height="1.75rem" /> : catalog.investments.data.length}</dd></div>
        <div className="inv-summary__item"><dt>Caixinhas e porquinhos</dt>
          <dd className="inv-summary__value">{loading ? <Skeleton width="3rem" height="1.75rem" /> : catalog["reserved-balances"].data.length}</dd></div>
      </dl>
    </header>
    <section className="investimentos__section" aria-labelledby="investimentos-list-title">
      <div className="investimentos__section-head"><h2 id="investimentos-list-title" className="investimentos__group-title">Seus investimentos</h2>
        {items.length > 0 && <p className="investimentos__lead">Aplicações e caixinhas não são somadas entre si, porque podem se sobrepor.</p>}</div>
      {loading && !items.length && <Skeleton width="100%" height="12rem" />}
      {!loading && !items.length && <div className="investimentos__empty"><p className="investimentos__empty-title">{emptyTitle}</p><p className="investimentos__hint">{emptyText}</p></div>}
      {items.length > 0 && <div className="inv-groups">{groups.map(({ modality, items: groupItems }) => <ProductGroup key={modality.key} modality={modality} items={groupItems} collapsed={collapsedGroups.has(modality.key)} onToggle={() => toggleGroup(modality.key)} onOpen={saveListState} />)}</div>}
      <div className="inv-real__pagination">{[["investments", "investimentos"], ["reserved-balances", "caixinhas e porquinhos"]].map(([kind, label]) => <div key={kind}>
        {catalog[kind].moreError && <p className="investimentos__error" role="alert">Não foi possível carregar mais {label}.</p>}
        {catalog[kind].cursor && <Button as="button" type="button" variant="outline" size="sm" disabled={catalog[kind].loadingMore || catalog[kind].refreshing} onClick={() => catalog.loadMore(kind)}>{catalog[kind].loadingMore || catalog[kind].refreshing ? "Carregando…" : `Carregar mais ${label}`}</Button>}
      </div>)}</div>
    </section>
    <section className="investimentos__section" aria-labelledby="investimentos-plan-title">
      <div className="investimentos__section-head"><h2 id="investimentos-plan-title" className="investimentos__group-title">Seu planejamento</h2></div>
      {planning.status === "loading" ? <Skeleton width="100%" height="12rem" /> : planning.status === "error" ? <div className="investimentos__failure" role="alert"><p>Não foi possível carregar a reserva e as metas.</p><Button as="button" type="button" variant="outline" size="sm" onClick={planning.retry}>Tentar novamente</Button></div>
        : <div className="investimentos__grid"><EmergencyReserveCard onOpen={saveListState} itemsLoading={loading && !items.length} entries={reserveEntries} summary={reserveSummary} />
          <GoalsCard planning={planning} items={items} entries={goalEntries} /></div>}
    </section>
    <FinancingSimulator />
  </div>;
}
