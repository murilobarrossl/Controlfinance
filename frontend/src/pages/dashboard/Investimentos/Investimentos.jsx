import { Fragment, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import NoBankAccount from "../../../components/dashboard/NoBankAccount/NoBankAccount.jsx";
import { ChartIcon, BoxIcon, ShieldIcon, InfoIcon, CloseIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import { useAuth } from "../../../context/AuthContext.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import { formatMoney, isLinkedReserve, isReserve, MODALITIES, normalizeInvestment, sumMoney } from "../../../utils/importedInvestments.js";
import { investmentDetail } from "../../../api/investments.js";
import usePlanning from "./usePlanning.js";
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
  const Icon = modality.key === "reserved" ? BoxIcon : ChartIcon;
  return <section className="inv-group card" aria-labelledby={`group-${modality.key}`} style={{ "--modality-color": modality.color, "--modality-text": modality.color }}>
    <header className="inv-group__header"><span className="inv-group__icon" aria-hidden="true"><Icon /></span><h3 id={`group-${modality.key}`} className="inv-group__title">{modality.groupLabel}</h3><span className="inv-group__count">{items.length} {items.length === 1 ? "produto" : "produtos"}</span></header>
    <ul className="inv-tiles">{items.map((item) => <Fragment key={item.key}>
      <li className={`inv-tile${selected === item.key ? " inv-tile--selected" : ""}`}><button type="button" className="inv-tile__button" aria-expanded={selected === item.key} aria-controls={`detail-${item.key}`} onClick={(event) => setSelected(selected === item.key ? null : item.key, event.currentTarget)}>
        <span className="inv-tile__head"><BankLogo name={item.institution.name} /><span className="inv-tile__titles"><span className="inv-tile__name">{item.name}</span><span className="inv-tile__institution">{item.institution.name}{item.accountNumber && <> · conta <span className="inv-tile__account">{item.accountNumber}</span></>}</span></span></span>
        <span className="inv-tile__balance"><Amounts item={item} /></span>
        {item.catalogPresence?.status === "not_reported_in_last_full_scan" && <span className="inv-tile__rule inv-tile__rule--missing">Não reencontrado na última consulta completa · saldo preservado</span>}
      </button></li>
      {selected === item.key && <li id={`detail-${item.key}`} className="inv-tile-detail"><ImportedInvestmentDetail item={item} planning={planning} owner={owner} /></li>}
    </Fragment>)}</ul>
  </section>;
}
export default function Investimentos() {
  usePageTitle("Investimentos");
  const owner = useAuth()?.user?.id;
  const { accounts, connections: allConnections, connectionsStatus, connectionsRefreshing,
    effectiveConnectionId, dataRefreshKey, bumpDataRefresh } = useAccount();
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
  const linked = planning.reserve?.bankAccountId || planning.reserve?.importedInvestmentId || planning.reserve?.importedReservedBalanceId;
  const loadedReserve = items.find((item) => isLinkedReserve(item, planning.reserve));
  const linkedId = planning.reserve?.importedInvestmentId ?? planning.reserve?.importedReservedBalanceId;
  const linkedReserved = Boolean(planning.reserve?.importedReservedBalanceId);
  const linkedKey = `${owner}:${linkedReserved}:${linkedId}`;
  const [linkedDetail, setLinkedDetail] = useState(null);
  const missingLinked = Boolean(linkedId && !loadedReserve);
  useEffect(() => {
    if (!missingLinked) return;
    const controller = new AbortController();
    investmentDetail(linkedId, controller.signal, linkedReserved).then((data) => {
      if (!controller.signal.aborted) setLinkedDetail({ key: linkedKey, data });
    }).catch(() => { if (!controller.signal.aborted) setLinkedDetail({ key: linkedKey, data: null }); });
    return () => controller.abort();
  }, [linkedId, linkedReserved, linkedKey, missingLinked, dataRefreshKey, catalog.refreshSequence]);
  const importedReserve = loadedReserve ?? (missingLinked && linkedDetail?.key === linkedKey && linkedDetail.data
    ? normalizeInvestment(linkedDetail.data, linkedReserved, allConnections) : null);
  const legacyAccount = accounts.find((account) => account.id === planning.reserve?.bankAccountId);
  const legacyItem = legacyAccount ? { account: legacyAccount, institution: { name: legacyAccount.name }, modality: { label: "Conta vinculada anteriormente" } } : null;
  const autoReserves = items.filter((item) => item !== loadedReserve && isReserve(item, planning.reserve));
  const importedReserves = [...(importedReserve ? [importedReserve] : []), ...autoReserves];
  const reserveItems = [...importedReserves, ...(legacyItem && !importedReserves.length ? [legacyItem] : [])];
  const datasets = [catalog.investments, catalog["reserved-balances"]];
  const loading = datasets.some((dataset) => dataset.status === "loading") || connectionsStatus === "loading";
  const failed = datasets.some((dataset) => dataset.status === "error") || connectionsStatus === "error";
  const confirmedEmpty = !loading && !failed && !connectionsRefreshing && !items.length && connections.length > 0
    && datasets.every((dataset) => !dataset.cursor && !dataset.refreshing)
    && connections.every((connection) => connection.status === "none_reported" && connection.reservedStatus === "none_reported");
  const reserveTotals = sumMoney(importedReserves.flatMap((item) => item.amounts));
  const reserveValue = importedReserves.length ? (reserveTotals.length ? reserveTotals.map((money) => <span className="inv-real__amount" key={money.currency}>{formatMoney(money)}</span>) : "Não informado")
    : legacyAccount ? formatCurrency(legacyAccount.balance)
    : linked ? "Não informado" : formatCurrency(planning.reserve?.currentAmount ?? null);
  if (allConnections.length === 0) {
    if (connectionsStatus === "loading" || connectionsRefreshing)
      return <Skeleton width="100%" height="12rem" />;
    return <NoBankAccount />;
  }
  const planningSection = <section className="investimentos__group" aria-labelledby="investimentos-plan-title"><h2 id="investimentos-plan-title" className="investimentos__group-title">Seu planejamento</h2>
    {planning.status === "loading" ? <Skeleton width="100%" height="12rem" /> : planning.status === "error" ? <div role="alert"><p>Não foi possível carregar a reserva e as metas.</p><Button as="button" type="button" variant="outline" size="sm" onClick={planning.retry}>Tentar novamente</Button></div>
      : <div className="investimentos__grid"><EmergencyReserveCard planning={planning} items={[...items, ...(!loadedReserve && importedReserve ? [importedReserve] : []), ...(legacyItem ? [legacyItem] : [])]} reserveItems={reserveItems} /><GoalsCard planning={planning} /></div>}
  </section>;
  return <div className="investimentos">
    <div className="inv-real__heading"><SectionHeading title="Investimentos" level={1} /><Button as="button" type="button" variant="outline" size="sm" onClick={bumpDataRefresh}>Atualizar dados</Button></div>
    {connections.some((c) => PENDING_STATES.has(c.status) || PENDING_STATES.has(c.reservedStatus))
      && allConnections.some((c) => !dismissed.includes(c.id))
      && <div className="investimentos__sync-note" role="status"><InfoIcon /><p>Seus investimentos estão sendo sincronizados. Alguns podem demorar um pouco para aparecer. Eles serão exibidos gradualmente, conforme os dados forem disponibilizados pelo banco.</p>
        <button type="button" className="investimentos__sync-note-close" aria-label="Fechar aviso" onClick={dismissSyncNote}><CloseIcon /></button></div>}
    {failed && <div className="investimentos__failure" role="alert"><p>Não foi possível atualizar parte dos dados. Os registros carregados mantêm suas datas de referência.</p><Button as="button" type="button" variant="outline" size="sm" onClick={bumpDataRefresh}>Tentar novamente</Button></div>}
    <div className="investimentos__summary">
      <StatCard icon={<ChartIcon />} label="Investimentos carregados" value={loading ? <Skeleton width="5rem" height="1.5rem" /> : catalog.investments.data.length} secondaryLines={[{ label: "Cada produto conserva sua posição e modalidade", value: "" }]} />
      <StatCard icon={<BoxIcon />} label="Caixinhas e porquinhos" value={loading ? <Skeleton width="5rem" height="1.5rem" /> : catalog["reserved-balances"].data.length} secondaryLines={[{ label: "Saldos reservados informados pelas contas", value: "" }]} />
      <StatCard icon={<ShieldIcon />} label="Reserva de emergência" value={planning.status === "loading" ? <Skeleton width="6rem" height="1.5rem" /> : planning.status === "error" ? "Indisponível" : reserveValue}
        secondaryLines={[{ label: importedReserves.length > 1 ? `Soma de ${importedReserves.length} reservas${importedReserves.some((item) => !item.currentAmountAvailable) ? " · inclui último valor informado" : ""}`
          : importedReserves.length ? `${importedReserves[0].name}${!importedReserves[0].currentAmountAvailable ? " · último valor informado" : ""}` : linked ? "Classificação vinculada ao recurso original" : "Valor anotado no planejamento", value: "" }]} />
    </div>
    {loading && !items.length && <Skeleton width="100%" height="12rem" />}
    {!loading && !items.length && <Card className="investimentos__empty"><p className="investimentos__empty-title">{failed ? "Não foi possível carregar os investimentos" : confirmedEmpty ? "Nenhum investimento informado pelas instituições" : connections.length ? "Ainda não há investimentos disponíveis para exibir" : "Nenhuma conexão bancária encontrada"}</p><p className="investimentos__hint">{failed ? "Tente atualizar os dados novamente." : confirmedEmpty ? "As consultas completas não retornaram aplicações ou saldos reservados. Isso não representa saldo zero em produtos não compartilhados." : connections.length ? "Valores ainda não recebidos permanecem indisponíveis." : "Os investimentos serão apresentados após a conexão bancária e o recebimento dos dados."}</p></Card>}
    {items.length > 0 && <><p className="investimentos__hint">Aplicações e reservas de conta permanecem separadas. Sem confirmação de vínculo entre elas, os saldos não são consolidados em um total.</p><div className="inv-groups">{groups.map(({ modality, items: groupItems }) => <ProductGroup key={modality.key} modality={modality} items={groupItems} selected={selected} setSelected={setSelected} planning={planning} owner={owner} />)}</div></>}
    <div className="inv-real__pagination">{[["investments", "investimentos"], ["reserved-balances", "caixinhas e porquinhos"]].map(([kind, label]) => <div key={kind}>
      {catalog[kind].moreError && <p className="investimentos__error" role="alert">Não foi possível carregar mais {label}.</p>}
      {catalog[kind].cursor && <Button as="button" type="button" variant="outline" size="sm" disabled={catalog[kind].loadingMore || catalog[kind].refreshing} onClick={() => catalog.loadMore(kind)}>{catalog[kind].loadingMore || catalog[kind].refreshing ? "Carregando…" : `Carregar mais ${label}`}</Button>}
    </div>)}</div>
    {planningSection}
    <FinancingSimulator />
  </div>;
}
