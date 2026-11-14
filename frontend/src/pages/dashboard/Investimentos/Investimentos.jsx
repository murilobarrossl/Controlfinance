import { useLayoutEffect, useMemo, useRef, useState } from "react";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import { InstitutionLogo, InvestmentIcon } from "./InvestmentMarks.jsx";
import NoBankAccount from "../../../components/dashboard/NoBankAccount/NoBankAccount.jsx";
import { ChevronDownIcon, InfoIcon, CloseIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import { useAuth } from "../../../context/AuthContext.jsx";
import { formatMoney, isReserve, MODALITIES, normalizeInvestment } from "../../../utils/importedInvestments.js";
import usePlanning from "./usePlanning.js";
import useLinkedResources from "./useLinkedResources.js";
import { legacyAccountEntry, summarize, summaryNote } from "./planningSummary.js";
import useImportedInvestments from "./useImportedInvestments.js";
import ImportedInvestmentDetail from "./ImportedInvestmentDetail.jsx";
import { EmergencyReserveCard, GoalsCard } from "./PlanningCards.jsx";
import FinancingSimulator from "./FinancingSimulator.jsx";
import usePageTitle from "../../../hooks/usePageTitle.js";
import "./Investimentos.css";

function Amounts({ item }) {
  if (!item.amounts.length) return "Não informado";
  return item.amounts.map((money, index) => <span className="inv-real__amount" key={index}>{formatMoney(money)}</span>);
}
const PENDING_STATES = new Set(["checking", "awaiting_sync", "sync_delayed", "pending_authorization"]);
const dismissedKey = (owner) => `investmentsSyncNoteDismissed:${owner}`;
function readDismissed(owner) {
  try {
    const ids = JSON.parse(localStorage.getItem(dismissedKey(owner)) ?? "[]");
    return Array.isArray(ids) ? ids : [];
  } catch { return []; }
}
function ProductGroup({ modality, items, selected, setSelected, planning, owner }) {
  const [collapsed, setCollapsed] = useState(false);
  return <section className={`inv-group${collapsed ? " inv-group--collapsed" : ""}`} aria-labelledby={`group-${modality.key}`}>
    <h3 id={`group-${modality.key}`} className="inv-group__header">
      <button type="button" className="inv-group__toggle" aria-expanded={!collapsed} aria-controls={`group-${modality.key}-list`} onClick={() => setCollapsed((value) => !value)}>
        <span className="inv-group__title">{modality.groupLabel}</span>
        <span className="inv-group__count">{items.length} {items.length === 1 ? "produto" : "produtos"}</span>
        <span className="inv-group__chevron" aria-hidden="true"><ChevronDownIcon /></span>
      </button>
    </h3>
    <ul id={`group-${modality.key}-list`} className="inv-list" hidden={collapsed}>{items.map((item) => <li key={item.key} className={`inv-row${selected === item.key ? " inv-row--open" : ""}`}>
      <button type="button" className="inv-row__button" aria-expanded={selected === item.key} aria-controls={`detail-${item.key}`} onClick={(event) => setSelected(selected === item.key ? null : item.key, event.currentTarget)}>
        <InvestmentIcon item={item} />
        <span className="inv-row__titles"><span className="inv-row__name">{item.name}</span><span className="inv-row__institution"><InstitutionLogo institution={item.institution} /><span className="inv-row__institution-text">{item.institution.name}{item.accountNumber && <> · conta <span className="inv-row__account">{item.accountNumber}</span></>}</span></span>
          {item.catalogPresence?.status === "not_reported_in_last_full_scan" && <span className="inv-row__flag">Não informado na última atualização · último saldo conhecido</span>}</span>
        <span className="inv-row__balance"><Amounts item={item} /></span>
        <span className="inv-row__chevron" aria-hidden="true"><ChevronDownIcon /></span>
      </button>
      {selected === item.key && <div id={`detail-${item.key}`} className="inv-row__detail"><ImportedInvestmentDetail item={item} planning={planning} owner={owner} /></div>}
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
  const catalog = useImportedInvestments(owner, dataRefreshKey, effectiveConnectionId);
  const [selected, setSelectedKey] = useState(null);
  const scrollAnchor = useRef(null);
  const setSelected = (key, element) => {
    scrollAnchor.current = element ? { element, top: element.getBoundingClientRect().top } : null;
    setSelectedKey(key);
  };
  useLayoutEffect(() => {
    const anchor = scrollAnchor.current;
    scrollAnchor.current = null;
    if (!anchor?.element.isConnected) return;
    const shift = anchor.element.getBoundingClientRect().top - anchor.top;
    if (shift) window.scrollBy({ top: shift, behavior: "instant" });
  }, [selected]);
  const [dismissedState, setDismissed] = useState(null);
  const dismissed = dismissedState?.owner === owner ? dismissedState.ids : readDismissed(owner);
  const dismissSyncNote = () => {
    const ids = allConnections.map((c) => c.id);
    try { localStorage.setItem(dismissedKey(owner), JSON.stringify(ids)); } catch { /* indisponível */ }
    setDismissed({ owner, ids });
  };
  const connections = useMemo(() => effectiveConnectionId ? allConnections.filter((c) => c.id === effectiveConnectionId) : allConnections,
    [allConnections, effectiveConnectionId]);
  const items = useMemo(() => {
    const numbers = new Map(accounts.map((account) => [account.id, account.number]));
    const reserves = catalog["reserved-balances"].data
      .map((row) => ({ ...normalizeInvestment(row, true, allConnections), accountNumber: numbers.get(row.bankAccountId) ?? null }))
      .sort((a, b) => (a.accountNumber ?? "").localeCompare(b.accountNumber ?? ""));
    return [...catalog.investments.data.map((row) => normalizeInvestment(row, false, allConnections)), ...reserves];
  }, [catalog.investments.data, catalog["reserved-balances"].data, allConnections, accounts]);
  const groups = Object.values(MODALITIES).map((modality) => ({ modality, items: items.filter((item) => item.modality.key === modality.key) })).filter((group) => group.items.length);
  const resolve = useLinkedResources(planning, items, allConnections, `${dataRefreshKey}:${catalog.refreshSequence}`);
  const legacyAccount = accounts.find((account) => account.id === planning.reserve?.bankAccountId);
  const linkedReserve = resolve(planning.reserve);
  const linkedKeys = new Set(linkedReserve.map((entry) => entry.key));
  const reserveEntries = [
    ...linkedReserve,
    ...items.filter((item) => !linkedKeys.has(item.key) && isReserve(item, planning.reserve, planning.goals)).map((item) => ({ key: item.key, id: item.id, reserved: item.reserved, status: "ready", item, auto: true })),
    ...(legacyAccount ? [legacyAccountEntry(legacyAccount)] : []),
  ];
  const goalEntries = Object.fromEntries(planning.goals.map((goal) => [goal.id, resolve(goal)]));
  const reserveSummary = summarize(reserveEntries);
  const datasets = [catalog.investments, catalog["reserved-balances"]];
  const loading = datasets.some((dataset) => dataset.status === "loading") || connectionsStatus === "loading";
  const failed = datasets.some((dataset) => dataset.status === "error") || connectionsStatus === "error";
  const confirmedEmpty = !loading && !failed && !connectionsRefreshing && !items.length && connections.length > 0
    && datasets.every((dataset) => !dataset.cursor && !dataset.refreshing)
    && connections.every((connection) => connection.status === "none_reported" && connection.reservedStatus === "none_reported");
  const reserveValue = planning.status === "loading" || reserveSummary.loading ? <Skeleton width="7rem" height="1.75rem" />
    : planning.status === "error" ? "Indisponível"
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
      <div className="inv-real__heading"><SectionHeading title="Investimentos" level={1} /><Button as="button" type="button" variant="outline" size="sm" onClick={bumpDataRefresh}>Atualizar dados</Button></div>
      {connections.some((c) => PENDING_STATES.has(c.status) || PENDING_STATES.has(c.reservedStatus))
        && allConnections.some((c) => !dismissed.includes(c.id))
        && <div className="investimentos__sync-note" role="status"><InfoIcon /><p>Estamos recebendo seus investimentos do banco. Alguns podem levar um tempo para aparecer.</p>
          <button type="button" className="investimentos__sync-note-close" aria-label="Fechar aviso" onClick={dismissSyncNote}><CloseIcon /></button></div>}
      {failed && <div className="investimentos__failure" role="alert"><p>Não foi possível atualizar parte dos dados. Os valores exibidos mantêm a data em que foram informados.</p><Button as="button" type="button" variant="outline" size="sm" onClick={bumpDataRefresh}>Tentar novamente</Button></div>}
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
      {items.length > 0 && <div className="inv-groups">{groups.map(({ modality, items: groupItems }) => <ProductGroup key={modality.key} modality={modality} items={groupItems} selected={selected} setSelected={setSelected} planning={planning} owner={owner} />)}</div>}
      <div className="inv-real__pagination">{[["investments", "investimentos"], ["reserved-balances", "caixinhas e porquinhos"]].map(([kind, label]) => <div key={kind}>
        {catalog[kind].moreError && <p className="investimentos__error" role="alert">Não foi possível carregar mais {label}.</p>}
        {catalog[kind].cursor && <Button as="button" type="button" variant="outline" size="sm" disabled={catalog[kind].loadingMore || catalog[kind].refreshing} onClick={() => catalog.loadMore(kind)}>{catalog[kind].loadingMore || catalog[kind].refreshing ? "Carregando…" : `Carregar mais ${label}`}</Button>}
      </div>)}</div>
    </section>
    <section className="investimentos__section" aria-labelledby="investimentos-plan-title">
      <div className="investimentos__section-head"><h2 id="investimentos-plan-title" className="investimentos__group-title">Seu planejamento</h2></div>
      {planning.status === "loading" ? <Skeleton width="100%" height="12rem" /> : planning.status === "error" ? <div className="investimentos__failure" role="alert"><p>Não foi possível carregar a reserva e as metas.</p><Button as="button" type="button" variant="outline" size="sm" onClick={planning.retry}>Tentar novamente</Button></div>
        : <div className="investimentos__grid"><EmergencyReserveCard planning={planning} items={items} itemsLoading={loading && !items.length} entries={reserveEntries} summary={reserveSummary} />
          <GoalsCard planning={planning} items={items} entries={goalEntries} /></div>}
    </section>
    <FinancingSimulator />
  </div>;
}
