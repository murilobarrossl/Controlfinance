import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useSearchParams } from "react-router";
import { getCreditCardsOverview } from "../../../api/creditCards.js";
import { isAbortError } from "../../../api/client.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import LimitBar from "../../../components/dashboard/LimitBar/LimitBar.jsx";
import { formatCurrency, formatDate, formatPercentage } from "../../../utils/financeMath.js";
import { summarizeCards, groupCards } from "../../../utils/cardGroups.js";
import { buildUpcoming } from "../../../utils/upcomingDues.js";
import { formatDueDay, formatDataAge } from "../../../utils/invoiceDue.js";
import { isInvoiceOverdue } from "../../../utils/cardLimits.js";
import CardCategories from "./CardCategories.jsx";
import CardStatement from "./CardStatement.jsx";
import CardDetail from "./CardDetail.jsx";
import CardFace from "../../../components/dashboard/CardFace/CardFace.jsx";
import { cardBrand, connectorsByName as buildConnectorsMap } from "../../../utils/cardBrand.js";
import "./Cartoes.css";
import usePageTitle from "../../../hooks/usePageTitle.js";

const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };
const EMPTY_LIST = [];

function Section({ id, title, aside, children }) {
  return (
    <section className="cartoes__section" aria-labelledby={id}>
      <header className="cartoes__section-head">
        <h2 id={id} className="cartoes__section-title">{title}</h2>
        {aside}
      </header>
      {children}
    </section>
  );
}

function SummaryMetric({ label, value, warning = false, tone = "default", note }) {
  return (
    <div className="cartoes__metric">
      <dt>{label}</dt>
      <dd className={`cartoes__metric-value cartoes__metric-value--${warning ? "warning" : tone}`}>
        {value}
      </dd>
      {note && <dd className="cartoes__metric-note">{note}</dd>}
    </div>
  );
}

// Esta página não depende da conta selecionada no seletor lateral (useAccount()): os cartões vêm
// prontos de getCreditCardsOverview(), independente de qual conta está ativa no Dashboard.
export default function Cartoes() {
  usePageTitle("Cartões");
  const { user } = useAuth();
  const { connectors } = useAccount();
  const [data, setData] = useState(null);
  const [loadError, setLoadError] = useState("");
  const [reloadKey, setReloadKey] = useState(0);
  const [loadedKey, setLoadedKey] = useState(null);
  const loading = loadedKey !== reloadKey;
  // O topo e os próximos vencimentos somam sempre todos os cartões; a seleção só abre o painel do
  // cartão e escolhe o extrato.
  const location = useLocation();
  const [params, setParams] = useSearchParams();
  const showCategories = params.get("visao") === "categorias";
  const [selectedId, setSelectedId] = useState(() => location.state?.cardId ?? null);
  const pendingScrollId = useRef(location.state?.cardId ?? null);

  const cards = data?.cards ?? EMPTY_LIST;
  const dueItems = data?.upcoming ?? EMPTY_LIST;

  const reload = () => setReloadKey((key) => key + 1);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;

    getCreditCardsOverview({ signal })
      .then((overview) => {
        if (signal.aborted) return;
        setLoadError("");
        setData(overview);
        setLoadedKey(reloadKey);
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setData(null);
        setLoadError(err.message || "Não foi possível carregar os cartões.");
        setLoadedKey(reloadKey);
      });

    return () => controller.abort();
  }, [reloadKey]);

  const upcoming = useMemo(() => buildUpcoming(cards, dueItems), [cards, dueItems]);

  // Se o cartão escolhido deixou de existir (banco desconectado), a seleção some sozinha.
  const selectedCard = useMemo(() => cards.find((c) => c.id === selectedId) ?? null, [cards, selectedId]);
  const summary = useMemo(() => summarizeCards(cards), [cards]);

  useEffect(() => {
    if (loading || !pendingScrollId.current) return;
    const targetId = pendingScrollId.current;
    pendingScrollId.current = null;
    if (selectedCard?.id === targetId) {
      const target = location.state?.cardStatement?.cardId === targetId ? "cartoes-extrato" : `cartao-${targetId}`;
      document.getElementById(target)?.scrollIntoView({ block: "start" });
    }
  }, [loading, selectedCard, location.state]);
  const { groups } = useMemo(() => groupCards(cards), [cards]);

  const connectorsMap = useMemo(() => buildConnectorsMap(connectors), [connectors]);
  const brandOf = (source) => cardBrand(source, connectorsMap);

  const noCards = !loading && cards.length === 0;
  const orderedCards = groups.flatMap((group) => group.cards);
  const oldestSync = cards
    .filter((c) => c.connectionLastSyncedAt)
    .map((c) => c.connectionLastSyncedAt)
    .sort((a, b) => new Date(a) - new Date(b))[0];
  const dataAge = formatDataAge(oldestSync);
  const scopeLabel = cards.length === 1 ? "1 cartão" : `Soma de ${cards.length} cartões`;
  const scopeNote = [scopeLabel, dataAge].filter(Boolean).join(" · ");

  const valueSkeleton = <Skeleton width="7rem" height="1.5rem" />;
  const summaryValue = (value) => (loading ? valueSkeleton : formatCurrency(value));

  const usageLine = (() => {
    if (summary.usagePercent == null) return null;
    const percent = formatPercentage(summary.usagePercent);
    if (summary.overLimitCards === 0) return `${percent} do limite`;
    if (cards.length === 1) return `Excedido em ${formatCurrency(summary.overAmount)} · ${percent} do limite`;
    const which = summary.overLimitCards === 1 ? "1 cartão acima" : `${summary.overLimitCards} cartões acima`;
    return `${which} do limite, em ${formatCurrency(summary.overAmount)} · ${percent} do limite total`;
  })();

  const nextDueLine = (() => {
    if (!summary.nextDue) return null;
    const ownAge = formatDataAge(summary.nextDue.syncedAt);
    return [summary.nextDue.pastDue && "Fatura atrasada", summary.nextDue.label, ownAge !== dataAge && ownAge]
      .filter(Boolean)
      .join(" · ");
  })();

  function renderCardTile(card) {
    const selected = selectedCard?.id === card.id;
    const pastDue = isInvoiceOverdue(card);
    return [
      <li key={card.id} className={`cartoes__tile${selected ? " cartoes__tile--selected" : ""}`}>
        <button
          type="button"
          className="cartoes__tile-button"
          aria-expanded={selected}
          aria-controls={`cartao-${card.id}`}
          onClick={() => setSelectedId(selected ? null : card.id)}
        >
          <CardFace card={card} holder={user?.name} />
          <span className="cartoes__tile-stats">
            <span className="cartoes__tile-stat">
              <span>Fatura atual</span>
              <strong className={pastDue ? "cartoes__tile-value--overdue" : undefined}>{card.currentInvoice != null ? formatCurrency(card.currentInvoice) : "—"}</strong>
            </span>
            <span className="cartoes__tile-stat">
              <span>Vencimento</span>
              <strong className={pastDue ? "cartoes__tile-value--overdue" : undefined}>
                {!card.invoiceDueDate ? "—" : pastDue ? formatDueDay(card.invoiceDueDate) : formatDate(card.invoiceDueDate)}
              </strong>
            </span>
          </span>
        </button>
      </li>,
      selected && (
        <CardDetail key={`${card.id}-detalhe`} id={`cartao-${card.id}`} card={card} bankName={brandOf(card).name} />
      ),
    ];
  }

  if (!loading && loadError) {
    return (
      <div className="cartoes">
        <SectionHeading title="Cartões" level={1} />
        <div className="cartoes__load-error">
          <p className="cartoes__error" role="alert">{loadError}</p>
          <Button as="button" type="button" variant="outline" size="sm" onClick={reload}>
            Tentar de novo
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="cartoes">
      <SectionHeading title="Cartões" level={1} />


      <div className="cartoes__view-switch">
        <div className="categorias__period-modes" role="group" aria-label="Visualização dos cartões">
          {[[false, "Visão geral"], [true, "Categorias"]].map(([categories, label]) => (
            <button key={label} type="button" aria-pressed={showCategories === categories}
              className={`categorias__segment${showCategories === categories ? " categorias__segment--active" : ""}`}
              onClick={() => setParams(categories ? { visao: "categorias" } : {}, { replace: true, state: location.state })}>
              {label}
            </button>
          ))}
        </div>
      </div>
      {showCategories ? (
        loading ? <Skeleton width="100%" height="12rem" /> : <CardCategories cards={cards} brandOf={brandOf} />
      ) : <>
      {noCards ? (
        <Card className="cartoes__empty">
          <p className="cartoes__empty-title">Nenhum cartão encontrado</p>
          <p className="cartoes__hint">
            Os cartões de crédito aparecem aqui quando você conecta o banco que os emitiu.
          </p>
          <div className="cartoes__empty-actions">
            <Button as={Link} to="/conectar-banco" variant="primary" size="sm">
              Conectar banco
            </Button>
          </div>
        </Card>
      ) : (
        <div className="cartoes__overview">
          <dl className="cartoes__summary summary-dividers summary-dividers--four" aria-label="Resumo dos cartões" aria-busy={loading}>
            <SummaryMetric
              label="Limite usado"
              value={summaryValue(summary.usedLimit)}
              warning={!loading && summary.overLimitCards > 0}
              note={!loading && usageLine}
            />
            <SummaryMetric label="Limite disponível" value={summaryValue(summary.availableLimit)} />
            <SummaryMetric label="Fatura atual" value={summaryValue(summary.currentInvoice)} />
            <SummaryMetric
              label="Próximo vencimento"
              value={
                loading
                  ? valueSkeleton
                  : !summary.nextDue
                    ? "—"
                    : summary.nextDue.pastDue
                      ? formatDueDay(summary.nextDue.date)
                      : formatDate(summary.nextDue.date)
              }
              warning={!loading && summary.nextDue?.pastDue}
              note={!loading && nextDueLine}
            />
          </dl>
          <p className="cartoes__scope-note">{loading ? <Skeleton width="14rem" height="0.9rem" /> : scopeNote}</p>
        </div>
      )}

      <Section id="cartoes-vencimentos" title="Próximos vencimentos">
        {loading ? (
          <Skeleton width="100%" height="6rem" />
        ) : upcoming.length === 0 ? (
          <p className="cartoes__hint">Nenhuma fatura ou conta pendente por enquanto.</p>
        ) : (
          <ul className="cartoes__rows">
            {upcoming.map((item) => (
              <li key={item.key} className="cartoes__row">
                <span className="cartoes__row-kind">{item.kind}</span>
                <span className="cartoes__row-info">
                  <span className="cartoes__row-name" title={item.name}>{item.name}</span>
                  <span className="cartoes__row-meta">
                    {item.meta} · {item.pastDueText ?? formatDate(item.date)}
                  </span>
                </span>
                <span className="cartoes__row-end">
                  <StatusPill status={item.status} pendingTone="neutral">{STATUS_LABELS[item.status] || item.status}</StatusPill>
                  <span className={`cartoes__row-amount${item.status === "Overdue" ? " cartoes__row-amount--expense" : ""}`}>{item.amount != null ? formatCurrency(item.amount) : "—"}</span>
                </span>
              </li>
            ))}
          </ul>
        )}
      </Section>

      </>}

      {!noCards && (
        <Section
          id="cartoes-lista"
          title="Seus cartões"
          aside={
            !loading && !selectedCard && (
              <span className="cartoes__section-note">Clique em um cartão para ver detalhes e o extrato dele.</span>
            )
          }
        >
          {loading ? (
            <Skeleton width="100%" height="12rem" />
          ) : (
            <ul className="cartoes__tiles">{orderedCards.map((card) => renderCardTile(card))}</ul>
          )}
        </Section>
      )}

      {!loading && cards.length > 0 &&
        (selectedCard ? (
          <CardStatement key={selectedCard.id} card={selectedCard} getBrand={brandOf} />
        ) : (
          <Section id="cartoes-extrato" title="Extrato do cartão">
            <p className="cartoes__hint">Selecione um cartão acima para ver o extrato.</p>
          </Section>
        ))}
    </div>
  );
}
