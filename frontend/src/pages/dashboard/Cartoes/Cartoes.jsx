import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Link } from "react-router";
import { getCreditCardsSummary, createCreditCard, updateCreditCard, setPrimaryCard } from "../../../api/creditCards.js";
import { getInstallments, createInstallment, deleteInstallment } from "../../../api/installments.js";
import { getTransactions } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import { useDashboardHeaderSlot } from "../../../context/DashboardHeaderContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import IconAvatar from "../../../components/ui/IconAvatar/IconAvatar.jsx";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { CardIcon, WalletIcon, BellIcon, HistoryIcon, StarIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import CreditCardVisual from "../../../components/dashboard/CreditCardVisual/CreditCardVisual.jsx";
import LimitBar from "../../../components/dashboard/LimitBar/LimitBar.jsx";
import { formatCurrency, formatDate, formatPercentage } from "../../../utils/financeMath.js";
import { summarizeCards, groupCards } from "../../../utils/cardGroups.js";
import { buildUpcoming } from "../../../utils/upcomingDues.js";
import { formatDueDay, formatDataAge } from "../../../utils/invoiceDue.js";
import { cardLimits, isInvoiceOverdue } from "../../../utils/cardLimits.js";
import { getCardBase, cardTextColor } from "../../../utils/cardColors.js";
import CardStatement from "./CardStatement.jsx";
import CardPicker from "../../../components/dashboard/CardPicker/CardPicker.jsx";
import { cardBrand, cardPickerOption, connectorsByName as buildConnectorsMap } from "../../../utils/cardBrand.js";
import "./Cartoes.css";
import usePageTitle from "../../../hooks/usePageTitle.js";

const EMPTY_DETAIL_FORM = { id: null, brand: "", creditLimit: "", closingDay: "1", dueDay: "10" };
const EMPTY_MANUAL_FORM = { name: "", brand: "", creditLimit: "", closingDay: "1", dueDay: "10" };
const EMPTY_INSTALLMENT_FORM = {
  description: "",
  totalAmount: "",
  totalInstallments: "1",
  currentInstallment: "1",
  installmentAmount: "",
  nextDueDate: todayIso(),
  // Codifica tipo+id num valor só de <select> ("synced:<bankAccountId>" ou "manual:<creditCardId>")
  // pra dropdown misturar cartão reconhecido (BankAccount) e cartão manual (CreditCard) numa lista
  // só, já que são duas fontes diferentes.
  cardKey: "",
};
const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };
const KIND_CLASS = { Fatura: "invoice", Parcela: "installment", Conta: "bill" };

function formatDateTime(value) {
  return new Date(value).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });
}

function installmentRemovalMessage(installment) {
  const remaining = Math.max(0, installment.totalInstallments - installment.currentInstallment + 1);
  const card = installment.creditCardName || "sem cartão vinculado";
  const parts = [
    `Parcela ${installment.currentInstallment} de ${installment.totalInstallments} (${formatCurrency(installment.installmentAmount)} por mês, ${card}).`,
    remaining === 1
      ? `A última parcela, de ${formatCurrency(installment.installmentAmount)}, sai desta lista e dos próximos vencimentos.`
      : `As ${remaining} parcelas restantes, somando ${formatCurrency(remaining * installment.installmentAmount)}, saem desta lista e dos próximos vencimentos.`,
  ];
  if (installment.affectsCardLimit)
    parts.push(`O limite usado de ${installment.creditCardName} diminui nesse mesmo valor.`);
  parts.push("Os lançamentos do extrato não são apagados. Não é possível desfazer.");
  return parts.join(" ");
}

function todayIso() {
  const now = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

const EMPTY_LIST = [];
const TRANSACTIONS_CAP = 2000;



// Esta página não depende da conta selecionada no seletor lateral (useAccount()): cartões
// reconhecidos (card.isSynced) já vêm prontos de getCreditCardsSummary(), independente de qual
// conta está ativa no seletor — a mesma BankAccount aparece aqui sempre que for identificada como
// cartão, sem relação com qual conta o usuário escolheu ver no Dashboard Inteligente.
export default function Cartoes() {
  usePageTitle("Cartões");
  const { user } = useAuth();
  const { connectors } = useAccount();
  const headerSlot = useDashboardHeaderSlot();
  const [data, setData] = useState(null);
  const [loadError, setLoadError] = useState("");
  const [reloadKey, setReloadKey] = useState(0);
  const [loadedKey, setLoadedKey] = useState(null);
  const loading = loadedKey !== reloadKey;
  const [error, setError] = useState(null);
  const [detailForm, setDetailForm] = useState(EMPTY_DETAIL_FORM);
  const [manualForm, setManualForm] = useState(EMPTY_MANUAL_FORM);
  const [saving, setSaving] = useState(false);
  const [showInstallmentForm, setShowInstallmentForm] = useState(false);
  const [showManualForm, setShowManualForm] = useState(false);
  const [installmentForm, setInstallmentForm] = useState(EMPTY_INSTALLMENT_FORM);
  // Os quatro números do topo podem somar todos os cartões ("all") ou mostrar só um deles (id).
  const [summaryScope, setSummaryScope] = useState("all");
  const [pendingInstallment, setPendingInstallment] = useState(null);
  const focusAfterDelete = useRef(null);
  const installmentsRef = useRef(null);
  const manualFormRef = useRef(null);

  const cards = data?.cards ?? EMPTY_LIST;
  const installments = data?.installments ?? EMPTY_LIST;
  const pendingTransactions = data?.pendingTransactions ?? EMPTY_LIST;

  const reload = () => setReloadKey((key) => key + 1);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;

    Promise.all([
      getCreditCardsSummary({ signal }),
      getInstallments({ signal }),
      getTransactions({ status: "Overdue", type: "Expense" }, { signal }),
      getTransactions({ status: "Pending", type: "Expense" }, { signal }),
    ])
      .then(([cardsData, installmentsData, overdueData, pendingData]) => {
        if (signal.aborted) return;
        setLoadError("");
        setData({ cards: cardsData, installments: installmentsData, pendingTransactions: [...overdueData, ...pendingData] });
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

  function openDetailForm(card) {
    setDetailForm({
      id: card.id,
      brand: card.brand || "",
      creditLimit: card.creditLimit != null ? String(card.creditLimit) : "",
      closingDay: card.closingDay != null ? String(card.closingDay) : "1",
      dueDay: card.dueDay != null ? String(card.dueDay) : "10",
    });
  }

  async function handleSubmitDetail(e) {
    e.preventDefault();
    if (!detailForm.creditLimit) return;

    const payload = {
      creditLimit: Math.max(0.01, Number(detailForm.creditLimit)),
      closingDay: Math.min(31, Math.max(1, Number(detailForm.closingDay) || 1)),
      dueDay: Math.min(31, Math.max(1, Number(detailForm.dueDay) || 1)),
    };

    setError(null);
    setSaving(true);
    try {
      await updateCreditCard(detailForm.id, { ...payload, brand: detailForm.brand || null });
      setDetailForm(EMPTY_DETAIL_FORM);
      reload();
    } catch (err) {
      setError({ where: "detail", message: err.message || "Não foi possível salvar os dados desse cartão." });
    } finally {
      setSaving(false);
    }
  }

  async function handleCreateManualCard(e) {
    e.preventDefault();
    if (!manualForm.name || !manualForm.creditLimit) return;

    setError(null);
    setSaving(true);
    try {
      await createCreditCard({
        name: manualForm.name,
        brand: manualForm.brand || null,
        creditLimit: Math.max(0.01, Number(manualForm.creditLimit)),
        closingDay: Math.min(31, Math.max(1, Number(manualForm.closingDay) || 1)),
        dueDay: Math.min(31, Math.max(1, Number(manualForm.dueDay) || 1)),
      });
      setManualForm(EMPTY_MANUAL_FORM);
      setShowManualForm(false);
      reload();
    } catch (err) {
      setError({ where: "manual", message: err.message || "Não foi possível cadastrar esse cartão." });
    } finally {
      setSaving(false);
    }
  }

  async function handleCreateInstallment(e) {
    e.preventDefault();
    const { description, totalAmount, totalInstallments: totalCount, currentInstallment, installmentAmount, nextDueDate, cardKey } =
      installmentForm;
    if (!description || !totalAmount || !installmentAmount) return;

    const [cardType, cardId] = cardKey ? cardKey.split(":") : [null, null];

    setError(null);
    setSaving(true);
    try {
      await createInstallment({
        creditCardId: cardType === "manual" ? cardId : null,
        bankAccountId: cardType === "synced" ? cardId : null,
        description,
        totalAmount: Math.max(0.01, Number(totalAmount)),
        totalInstallments: Math.max(1, Number(totalCount)),
        currentInstallment: Math.max(1, Number(currentInstallment)),
        installmentAmount: Math.max(0.01, Number(installmentAmount)),
        nextDueDate,
      });
      setInstallmentForm(EMPTY_INSTALLMENT_FORM);
      setShowInstallmentForm(false);
      reload();
    } catch (err) {
      setError({ where: "installment", message: err.message || "Não foi possível criar esse parcelamento." });
    } finally {
      setSaving(false);
    }
  }

  function handleCancelInstallment() {
    setShowInstallmentForm(false);
    setInstallmentForm(EMPTY_INSTALLMENT_FORM);
  }

  async function handleSetPrimary(id) {
    const previousPrimaryId = cards.find((c) => c.isPrimary)?.id;
    const markPrimary = (primaryId) =>
      setData((current) =>
        current && { ...current, cards: current.cards.map((c) => ({ ...c, isPrimary: c.id === primaryId })) },
      );

    markPrimary(id);
    setError(null);
    try {
      await setPrimaryCard(id);
    } catch (err) {
      markPrimary(previousPrimaryId);
      setError({ where: "page", message: err.message || "Não foi possível definir o cartão principal." });
    }
  }

  async function handleDeleteInstallment(id) {
    setError(null);
    focusAfterDelete.current = installments.findIndex((i) => i.id === id);
    try {
      await deleteInstallment(id);
      reload();
    } catch (err) {
      focusAfterDelete.current = null;
      setError({ where: "installments", message: err.message || "Não foi possível remover esse parcelamento." });
    }
  }

  useEffect(() => {
    if (loading || focusAfterDelete.current == null) return;
    const container = installmentsRef.current;
    const index = focusAfterDelete.current;
    focusAfterDelete.current = null;
    const buttons = container?.querySelectorAll(".cartoes__remove");
    const target = buttons?.length ? buttons[Math.min(index, buttons.length - 1)] : container?.querySelector("h3");
    if (target && !target.matches("button")) target.setAttribute("tabindex", "-1");
    target?.focus();
  }, [loading, installments]);

  const upcoming = useMemo(
    () => buildUpcoming(cards, installments, pendingTransactions),
    [cards, installments, pendingTransactions]
  );

  // Se o cartão escolhido deixou de existir (banco desconectado), volta pra "todos" sozinho.
  const scopeCard = useMemo(() => cards.find((c) => c.id === summaryScope) ?? null, [cards, summaryScope]);
  const summary = useMemo(() => summarizeCards(scopeCard ? [scopeCard] : cards), [scopeCard, cards]);
  const { groups } = useMemo(() => groupCards(cards), [cards]);

  const connectorsMap = useMemo(() => buildConnectorsMap(connectors), [connectors]);
  const brandOf = (source) => cardBrand(source, connectorsMap);

  const scopeLabel = scopeCard ? `${brandOf(scopeCard).name} · ${scopeCard.name}` : `Todos os cartões (${cards.length})`;
  const noCards = !loading && cards.length === 0;
  const orderedCards = groups.flatMap((group) => group.cards);
  const pickerOptions = [
    { value: "all", title: "Todos os cartões", subtitle: `Soma de ${cards.length} cartões` },
    ...orderedCards.map((card) => cardPickerOption(card, brandOf(card))),
  ];
  const oldestSync = (scopeCard ? [scopeCard] : cards)
    .filter((c) => c.isSynced && c.connectionLastSyncedAt)
    .map((c) => c.connectionLastSyncedAt)
    .sort((a, b) => new Date(a) - new Date(b))[0];
  const dataAge = formatDataAge(oldestSync);
  const scopeLines = [{ label: [scopeLabel, dataAge].filter(Boolean).join(" · "), value: "" }];
  const summaryValue = (value) => (loading ? valueSkeleton : formatCurrency(value));
  const usageLines =
    summary.usagePercent == null
      ? scopeLines
      : [
          {
            label:
              summary.overLimitCards > 0
                ? `Limite excedido em ${formatCurrency(summary.overAmount)} · ${formatPercentage(summary.usagePercent)} do limite`
                : `${formatPercentage(summary.usagePercent)} do limite`,
            value: "",
          },
          ...scopeLines,
        ];

  function openManualForm() {
    setShowManualForm(true);
    requestAnimationFrame(() => manualFormRef.current?.querySelector("input")?.focus());
  }

  const valueSkeleton = <Skeleton width="7rem" height="1.5rem" />;

  function renderCardTile(card) {
    const selected = scopeCard?.id === card.id;
    const brand = brandOf(card);
    const { available, usagePercent, overLimit, overAmount } = cardLimits(card);
    const pastDue = isInvoiceOverdue(card);
    const base = getCardBase({ name: card.name, productType: card.productType, institutionColor: card.institutionColor });
    const group = groups.find((g) => g.cards.includes(card));
    return [
      <li key={card.id} className={`cartoes__tile${selected ? " cartoes__tile--selected" : ""}`}>
        <button
          type="button"
          className="cartoes__tile-button"
          aria-expanded={selected}
          aria-controls={`cartao-${card.id}`}
          onClick={() => setSummaryScope(selected ? "all" : card.id)}
        >
          <span className="cartoes__tile-face" style={{ "--card-base": base, "--card-text": cardTextColor(base) }}>
            <span className="cartoes__tile-top">
              {brand.isManual ? (
                <span className="cartoes__tile-manual" aria-hidden="true">
                  <CardIcon />
                </span>
              ) : (
                <BankLogo name={brand.name} logoUrl={brand.logoUrl} color={brand.color} size={24} />
              )}
              <span className="cartoes__tile-bank">{brand.name}</span>
              {card.isPrimary && (
                <span className="cartoes__tile-primary">
                  <StarIcon filled />
                  <span className="visually-hidden">Cartão principal</span>
                </span>
              )}
            </span>
            <span className="cartoes__tile-name">{card.name}</span>
          </span>
          <span className="cartoes__tile-stats">
            <span className="cartoes__tile-stat">
              <span>Fatura atual</span>
              <strong>{card.currentInvoice != null ? formatCurrency(card.currentInvoice) : "—"}</strong>
            </span>
            <span className="cartoes__tile-stat">
              <span>Próximo vencimento</span>
              <strong>
                {!card.invoiceDueDate ? "—" : pastDue ? formatDueDay(card.invoiceDueDate) : formatDate(card.invoiceDueDate)}
              </strong>
            </span>
            <span className="cartoes__tile-stat cartoes__tile-stat--wide">
              <span>Limite disponível</span>
              <strong>{available != null ? formatCurrency(available) : "—"}</strong>
              <LimitBar usagePercent={usagePercent} size="sm" />
              {usagePercent != null && (
                <span className={`cartoes__tile-limit-note${overLimit ? " cartoes__tile-limit-note--over" : ""}`}>
                  {overLimit
                    ? `Limite excedido em ${formatCurrency(overAmount)} · ${formatPercentage(usagePercent)} usado`
                    : `${formatPercentage(usagePercent)} do limite usado`}
                </span>
              )}
            </span>
          </span>
        </button>
      </li>,
      selected && (
        <li key={`${card.id}-detalhe`} id={`cartao-${card.id}`} className="cartoes__tile-detail">
          {card.isSynced && (
            <p className="cartoes__tile-updated">
              Atualizado em {group?.lastSyncedAt ? formatDateTime(group.lastSyncedAt) : "Ainda não"}
            </p>
          )}
          {renderCardBlock(card)}
        </li>
      ),
    ];
  }

  function renderCardBlock(card) {
    return (
      <div key={card.id} className="cartoes__card-block">
        <CreditCardVisual
          card={card}
          cardholderName={user?.name}
          onSetPrimary={() => handleSetPrimary(card.id)}
          showKeyStats={false}
          showFace={false}
        />

        {!card.isSynced && detailForm.id !== card.id && (
          <button type="button" className="cartoes__edit-link" onClick={() => openDetailForm(card)}>
            Editar limite/fatura
          </button>
        )}

        {detailForm.id === card.id && (
          <form className="cartoes__form" onSubmit={handleSubmitDetail}>
            {!card.isSynced && (
              <label className="cartoes__field">
                <span className="cartoes__field-label">Bandeira (opcional)</span>
                <input
                  type="text"
                  placeholder="Ex: Mastercard"
                  maxLength={50}
                  value={detailForm.brand}
                  onChange={(e) => setDetailForm({ ...detailForm, brand: e.target.value })}
                />
              </label>
            )}
            <label className="cartoes__field">
              <span className="cartoes__field-label">Limite de crédito</span>
              <CurrencyInput
                placeholder="Ex: 5000"
                value={detailForm.creditLimit}
                onChange={(e) => setDetailForm({ ...detailForm, creditLimit: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Dia de fechamento</span>
              <input
                type="number"
                min="1"
                max="31"
                step="1"
                value={detailForm.closingDay}
                onChange={(e) => setDetailForm({ ...detailForm, closingDay: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Dia de vencimento</span>
              <input
                type="number"
                min="1"
                max="31"
                step="1"
                value={detailForm.dueDay}
                onChange={(e) => setDetailForm({ ...detailForm, dueDay: e.target.value })}
                required
              />
            </label>

            {error?.where === "detail" && (
              <p className="cartoes__error cartoes__form-error" role="alert">
                {error.message}
              </p>
            )}
            <div className="cartoes__form-actions">
              <Button as="button" type="submit" variant="primary" size="sm" disabled={saving}>
                {saving ? "Salvando..." : "Salvar"}
              </Button>
              <Button
                as="button"
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setDetailForm(EMPTY_DETAIL_FORM)}
              >
                Cancelar
              </Button>
            </div>
          </form>
        )}
      </div>
    );
  }

  if (!loading && loadError) {
    return (
      <div className="cartoes">
        <SectionHeading title="Cartões" level={1}>Fatura, limite e parcelamentos</SectionHeading>
        <div className="cartoes__load-error">
          <p className="cartoes__error" role="alert">{loadError}</p>
          <Button as="button" type="button" variant="outline" size="sm" onClick={reload}>
            Tentar de novo
          </Button>
        </div>
      </div>
    );
  }

  const listSkeleton = <Skeleton width="100%" height="6rem" />;

  return (
    <div className="cartoes">
      <SectionHeading title="Cartões" level={1}>Fatura, limite e parcelamentos</SectionHeading>

      {error?.where === "page" && <p className="cartoes__error" role="alert">{error.message}</p>}

      <p className="cartoes__intro">
        Aqui ficam só os cartões de crédito: o que você gastou no crédito e ainda vai pagar na fatura. O saldo
        da conta corrente (dinheiro que você já tem) continua no Dashboard inteligente, sem ser somado com os
        cartões.
      </p>

      {!loading && cards.length === 1 && (
        <p className="cartoes__scope-title">
          Os números abaixo mostram: <strong>{scopeLabel}</strong>
        </p>
      )}
      {!loading &&
        cards.length > 1 &&
        headerSlot &&
        createPortal(
          <div className="dashboard-toolbar__scope">
            <CardPicker
              id="cartoes-escopo"
              labelId="cartoes-escopo-rotulo"
              options={pickerOptions}
              value={scopeCard?.id ?? "all"}
              onChange={setSummaryScope}
            />
          </div>,
          headerSlot,
        )}

      {noCards ? (
        <Card className="cartoes__empty">
          <p className="cartoes__empty-title">Você ainda não tem cartões por aqui</p>
          <p className="cartoes__hint">
            Limite, fatura e vencimento aparecem quando um cartão de crédito chega por um banco conectado ou é
            cadastrado manualmente.
          </p>
          <div className="cartoes__empty-actions">
            <Button as={Link} to="/conectar-banco" variant="primary" size="sm">
              Conectar banco
            </Button>
            <Button as="button" type="button" variant="outline" size="sm" onClick={openManualForm}>
              Cadastrar cartão
            </Button>
          </div>
        </Card>
      ) : (
        <div className="cartoes__summary">
          <StatCard
            icon={<CardIcon />}
            label="Limite usado"
            value={summaryValue(summary.usedLimit)}
            valueTone={!loading && summary.overLimitCards > 0 ? "expense" : "default"}
            secondaryLines={loading ? [] : usageLines}
          />
          <StatCard
            icon={<WalletIcon />}
            label="Limite disponível"
            value={summaryValue(summary.availableLimit)}
            secondaryLines={loading ? [] : scopeLines}
          />
          <StatCard
            icon={<BellIcon />}
            label="Fatura atual"
            value={summaryValue(summary.currentInvoice)}
            secondaryLines={loading ? [] : scopeLines}
          />
          <StatCard
            icon={<HistoryIcon />}
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
            valueTone={!loading && summary.nextDue?.pastDue ? "expense" : "default"}
            secondaryLines={
              !loading && summary.nextDue
                ? [
                    {
                      label: summary.nextDue.pastDue
                        ? ["Fatura atrasada", summary.nextDue.label, formatDataAge(summary.nextDue.syncedAt)].filter(Boolean).join(" · ")
                        : [summary.nextDue.label, formatDataAge(summary.nextDue.syncedAt)].filter(Boolean).join(" · "),
                      value: "",
                    },
                  ]
                : []
            }
          />
        </div>
      )}

      <Card title="Próximos vencimentos">
        {!loading && pendingTransactions.length >= TRANSACTIONS_CAP && (
          <p className="cartoes__hint">Mostrando os 2.000 lançamentos mais recentes.</p>
        )}
        {loading ? (
          listSkeleton
        ) : upcoming.length === 0 ? (
          <p className="cartoes__hint">Nenhuma fatura, parcela ou conta pendente por enquanto.</p>
        ) : (
          <ul className="cartoes__list">
            {upcoming.map((item) => (
              <li key={item.key} className="cartoes__list-item">
                <span className={`cartoes__kind cartoes__kind--${KIND_CLASS[item.kind]}`}>
                  {item.kind}
                </span>
                <div className="cartoes__list-info">
                  <span className="cartoes__list-name">{item.name}</span>
                  <span className="cartoes__list-meta">
                    {item.meta} · {item.pastDueText ?? formatDate(item.date)}
                  </span>
                </div>
                <StatusPill status={item.status}>{STATUS_LABELS[item.status] || item.status}</StatusPill>
                <span className="cartoes__list-amount">{item.amount != null ? formatCurrency(item.amount) : "—"}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card title="Seus cartões">
        {loading ? (
          listSkeleton
        ) : cards.length === 0 ? (
          <p className="cartoes__hint">
            Nenhum cartão reconhecido ainda. Se você tem um cartão sincronizado por um banco conectado,
            ele deve aparecer aqui sozinho na próxima sincronização — ou cadastre um manualmente abaixo.
          </p>
        ) : (
          <ul className="cartoes__tiles">
            {orderedCards.map((card) => renderCardTile(card))}
          </ul>
        )}
      </Card>

      {!loading && cards.length > 0 &&
        (scopeCard ? (
          <CardStatement key={scopeCard.id} card={scopeCard} getBrand={brandOf} />
        ) : (
          <Card title="Extrato do cartão">
            <p className="cartoes__hint">Escolha um cartão no menu acima ou na lista para ver o extrato dele.</p>
          </Card>
        ))}

      <div ref={manualFormRef}>
      <Card title="Cadastrar cartão não sincronizado">
        <p className="cartoes__hint">Pra um cartão que não veio de nenhum banco conectado.</p>
        {showManualForm ? (
          <form className="cartoes__form cartoes__form--spaced" onSubmit={handleCreateManualCard}>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Nome</span>
              <input
                type="text"
                placeholder="Ex: Nubank Roxinho"
                maxLength={80}
                value={manualForm.name}
                onChange={(e) => setManualForm({ ...manualForm, name: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Bandeira (opcional)</span>
              <input
                type="text"
                placeholder="Ex: Mastercard"
                maxLength={50}
                value={manualForm.brand}
                onChange={(e) => setManualForm({ ...manualForm, brand: e.target.value })}
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Limite de crédito</span>
              <CurrencyInput
                placeholder="Ex: 5000"
                value={manualForm.creditLimit}
                onChange={(e) => setManualForm({ ...manualForm, creditLimit: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Dia de fechamento</span>
              <input
                type="number"
                min="1"
                max="31"
                step="1"
                value={manualForm.closingDay}
                onChange={(e) => setManualForm({ ...manualForm, closingDay: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Dia de vencimento</span>
              <input
                type="number"
                min="1"
                max="31"
                step="1"
                value={manualForm.dueDay}
                onChange={(e) => setManualForm({ ...manualForm, dueDay: e.target.value })}
                required
              />
            </label>

            {error?.where === "manual" && (
              <p className="cartoes__error cartoes__form-error" role="alert">
                {error.message}
              </p>
            )}
            <div className="cartoes__form-actions">
              <Button as="button" type="submit" variant="primary" size="sm" disabled={saving}>
                {saving ? "Adicionando..." : "Adicionar cartão"}
              </Button>
              <Button
                as="button"
                type="button"
                variant="outline"
                size="sm"
                onClick={() => {
                  setShowManualForm(false);
                  setManualForm(EMPTY_MANUAL_FORM);
                }}
              >
                Cancelar
              </Button>
            </div>
          </form>
        ) : (
          <div className="cartoes__form-hint">
            <Button as="button" type="button" variant="outline" size="sm" onClick={openManualForm}>
              Cadastrar cartão
            </Button>
          </div>
        )}
      </Card>
      </div>

      <div ref={installmentsRef} className="cartoes__installments">
      <Card title="Parcelamentos">
        {error?.where === "installments" && (
          <p className="cartoes__error cartoes__form-error" role="alert">
            {error.message}
          </p>
        )}
        {loading ? (
          listSkeleton
        ) : installments.length === 0 ? (
          <p className="cartoes__hint">Nenhum parcelamento cadastrado ainda.</p>
        ) : (
          <ul className="cartoes__list">
            {installments.map((i) => (
              <li key={i.id} className="cartoes__list-item">
                <IconAvatar type="expense" />
                <div className="cartoes__list-info">
                  <span className="cartoes__list-name">{i.description}</span>
                  <span className="cartoes__list-meta">
                    {i.currentInstallment}/{i.totalInstallments} · {i.creditCardName || "Sem cartão vinculado"} · próx.{" "}
                    {formatDate(i.nextDueDate)}
                  </span>
                </div>
                <span className="cartoes__list-amount">{formatCurrency(i.installmentAmount)}</span>
                <button
                  type="button"
                  className="cartoes__remove"
                  onClick={() => setPendingInstallment(i)}
                  aria-label={`Remover parcelamento ${i.description}`}
                  title="Remover parcelamento"
                >
                  ×
                </button>
              </li>
            ))}
          </ul>
        )}

        {showInstallmentForm ? (
          <form className="cartoes__form cartoes__form--spaced" onSubmit={handleCreateInstallment}>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Descrição</span>
              <input
                type="text"
                placeholder="Ex: Notebook"
                maxLength={150}
                value={installmentForm.description}
                onChange={(e) => setInstallmentForm({ ...installmentForm, description: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Valor total</span>
              <CurrencyInput
                placeholder="Ex: 3000"
                value={installmentForm.totalAmount}
                onChange={(e) => setInstallmentForm({ ...installmentForm, totalAmount: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Total de parcelas</span>
              <input
                type="number"
                min="1"
                step="1"
                value={installmentForm.totalInstallments}
                onChange={(e) => setInstallmentForm({ ...installmentForm, totalInstallments: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Parcela atual</span>
              <input
                type="number"
                min="1"
                step="1"
                value={installmentForm.currentInstallment}
                onChange={(e) => setInstallmentForm({ ...installmentForm, currentInstallment: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Valor da parcela</span>
              <CurrencyInput
                placeholder="Ex: 250"
                value={installmentForm.installmentAmount}
                onChange={(e) => setInstallmentForm({ ...installmentForm, installmentAmount: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Próximo vencimento</span>
              <input
                type="date"
                value={installmentForm.nextDueDate}
                onChange={(e) => setInstallmentForm({ ...installmentForm, nextDueDate: e.target.value })}
                required
              />
            </label>
            <label className="cartoes__field">
              <span className="cartoes__field-label">Cartão (opcional)</span>
              <select
                value={installmentForm.cardKey}
                onChange={(e) => setInstallmentForm({ ...installmentForm, cardKey: e.target.value })}
              >
                <option value="">Sem cartão vinculado</option>
                {cards.map((c) => (
                  <option key={c.id} value={`${c.isSynced ? "synced" : "manual"}:${c.id}`}>
                    {c.name}
                  </option>
                ))}
              </select>
            </label>
            {error?.where === "installment" && (
              <p className="cartoes__error cartoes__form-error" role="alert">
                {error.message}
              </p>
            )}
            <div className="cartoes__form-actions">
              <Button as="button" type="submit" variant="primary" size="sm" disabled={saving}>
                {saving ? "Adicionando..." : "Adicionar"}
              </Button>
              <Button as="button" type="button" variant="outline" size="sm" onClick={handleCancelInstallment}>
                Cancelar
              </Button>
            </div>
          </form>
        ) : (
          <div className="cartoes__form-hint">
            <Button as="button" type="button" variant="outline" size="sm" onClick={() => setShowInstallmentForm(true)}>
              + Adicionar parcelamento
            </Button>
          </div>
        )}
      </Card>
      </div>

      <ConfirmDialog
        open={pendingInstallment !== null}
        title={`Remover o parcelamento "${pendingInstallment?.description ?? ""}"?`}
        message={pendingInstallment ? installmentRemovalMessage(pendingInstallment) : ""}
        confirmLabel="Remover"
        cancelLabel="Cancelar"
        onConfirm={() => {
          const id = pendingInstallment.id;
          setPendingInstallment(null);
          handleDeleteInstallment(id);
        }}
        onClose={() => setPendingInstallment(null)}
      />

      <Card title="Sobre os dados desta página">
        <p className="cartoes__hint">
          Cartões reconhecidos automaticamente (pela sua conta sincronizada) têm extrato real, logo
          acima, e as transações deles também aparecem em Relatórios e Categorias ao selecionar a
          conta no seletor do cabeçalho. Limite, fechamento, vencimento e fatura tentam vir direto da
          Polp; quando ela não manda algum desses dados pra um banco específico, só o que faltar
          pede preenchimento manual aqui. Um relatório de gastos por categoria dentro do próprio
          cartão ainda não é possível de forma confiável (falta vínculo entre lançamentos e cartão
          no sistema) — é um ponto de integração pra evoluir mais adiante, não algo que esta tela
          tenta simular.
        </p>
      </Card>
    </div>
  );
}
