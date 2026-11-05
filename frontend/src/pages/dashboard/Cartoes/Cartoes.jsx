import { useEffect, useMemo, useState } from "react";
import { getCreditCardsSummary, createCreditCard, updateCreditCard, setPrimaryCard } from "../../../api/creditCards.js";
import { getInstallments, createInstallment, deleteInstallment } from "../../../api/installments.js";
import { getTransactions } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import IconAvatar from "../../../components/ui/IconAvatar/IconAvatar.jsx";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { CardIcon, WalletIcon, BellIcon, HistoryIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import CreditCardVisual from "../../../components/dashboard/CreditCardVisual/CreditCardVisual.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import { summarizeCards, groupCards } from "../../../utils/cardGroups.js";
import { buildUpcoming } from "../../../utils/upcomingDues.js";
import CardStatement from "./CardStatement.jsx";
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

function todayIso() {
  const now = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

const EMPTY_LIST = [];
const TRANSACTIONS_CAP = 2000;

function normalizeName(value) {
  return String(value ?? "")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]/g, "");
}

function normalizeColor(raw) {
  if (!raw) return undefined;
  return raw.startsWith("#") ? raw : `#${raw}`;
}

// Esta página não depende da conta selecionada no seletor lateral (useAccount()): cartões
// reconhecidos (card.isSynced) já vêm prontos de getCreditCardsSummary(), independente de qual
// conta está ativa no seletor — a mesma BankAccount aparece aqui sempre que for identificada como
// cartão, sem relação com qual conta o usuário escolheu ver no Dashboard Inteligente.
export default function Cartoes() {
  usePageTitle("Cartões");
  const { user } = useAuth();
  const { connectors } = useAccount();
  const [data, setData] = useState(null);
  const [loadError, setLoadError] = useState("");
  const [reloadKey, setReloadKey] = useState(0);
  const [loadedKey, setLoadedKey] = useState(null);
  const loading = loadedKey !== reloadKey;
  const [error, setError] = useState("");
  const [detailForm, setDetailForm] = useState(EMPTY_DETAIL_FORM);
  const [manualForm, setManualForm] = useState(EMPTY_MANUAL_FORM);
  const [saving, setSaving] = useState(false);
  const [showInstallmentForm, setShowInstallmentForm] = useState(false);
  const [installmentForm, setInstallmentForm] = useState(EMPTY_INSTALLMENT_FORM);
  // Os quatro números do topo podem somar todos os cartões ("all") ou mostrar só um deles (id).
  const [summaryScope, setSummaryScope] = useState("all");

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

    setSaving(true);
    try {
      await updateCreditCard(detailForm.id, { ...payload, brand: detailForm.brand || null });
      setDetailForm(EMPTY_DETAIL_FORM);
      reload();
    } catch (err) {
      setError(err.message || "Não foi possível salvar os dados desse cartão.");
    } finally {
      setSaving(false);
    }
  }

  async function handleCreateManualCard(e) {
    e.preventDefault();
    if (!manualForm.name || !manualForm.creditLimit) return;

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
      reload();
    } catch (err) {
      setError(err.message || "Não foi possível cadastrar esse cartão.");
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
      setError(err.message || "Não foi possível criar esse parcelamento.");
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
    setError("");
    try {
      await setPrimaryCard(id);
    } catch (err) {
      markPrimary(previousPrimaryId);
      setError(err.message || "Não foi possível definir o cartão principal.");
    }
  }

  async function handleDeleteInstallment(id) {
    try {
      await deleteInstallment(id);
      reload();
    } catch (err) {
      setError(err.message || "Não foi possível remover esse parcelamento.");
    }
  }

  const upcoming = useMemo(
    () => buildUpcoming(cards, installments, pendingTransactions),
    [cards, installments, pendingTransactions]
  );

  // Se o cartão escolhido deixou de existir (banco desconectado), volta pra "todos" sozinho.
  const scopeCard = useMemo(() => cards.find((c) => c.id === summaryScope) ?? null, [cards, summaryScope]);
  const summary = useMemo(() => summarizeCards(scopeCard ? [scopeCard] : cards), [scopeCard, cards]);
  const { groups } = useMemo(() => groupCards(cards), [cards]);

  // O cartão traz só o nome do banco (e a cor); a logo vem do conector da Polp com o mesmo nome.
  // Sem correspondência, BankLogo cai nas iniciais do banco na cor dele.
  const connectorsByName = useMemo(
    () => new Map(connectors.map((c) => [normalizeName(c.name), c])),
    [connectors],
  );
  function brandOf(source) {
    const name = source.bankName || "Banco conectado";
    const connector = connectorsByName.get(normalizeName(name));
    return {
      name,
      logoUrl: connector?.logoUrl,
      color: normalizeColor(connector?.color ?? source.color ?? source.institutionColor),
    };
  }

  const scopeLabel = scopeCard ? `${brandOf(scopeCard).name} · ${scopeCard.name}` : `Todos os cartões (${cards.length})`;
  const scopeLines = [{ label: scopeLabel, value: "" }];

  function renderCardBlock(card) {
    return (
      <div key={card.id} className="cartoes__card-block">
        <CreditCardVisual
          card={card}
          cardholderName={user?.name}
          onSetPrimary={() => handleSetPrimary(card.id)}
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

  if (!loading && loadError) return <p className="cartoes__error">{loadError}</p>;

  const valueSkeleton = <Skeleton width="7rem" height="1.5rem" />;
  const listSkeleton = <Skeleton width="100%" height="6rem" />;

  return (
    <div className="cartoes">
      <SectionHeading title="Cartões">Fatura, limite e parcelamentos</SectionHeading>

      {error && <p className="cartoes__error">{error}</p>}

      <p className="cartoes__intro">
        Aqui ficam só os cartões de crédito: o que você gastou no crédito e ainda vai pagar na fatura. O saldo
        da conta corrente (dinheiro que você já tem) continua no Dashboard inteligente, sem ser somado com os
        cartões.
      </p>

      {!loading && cards.length > 0 && (
        <div className="cartoes__scope">
          <p className="cartoes__scope-title">
            Os números abaixo mostram: <strong>{scopeLabel}</strong>
          </p>
          {cards.length > 1 && (
            <div className="cartoes__scope-options" role="radiogroup" aria-label="Escolha de qual cartão mostrar os números">
              <button
                type="button"
                role="radio"
                aria-checked={!scopeCard}
                className={`cartoes__statement-card ${!scopeCard ? "cartoes__statement-card--active" : ""}`}
                onClick={() => setSummaryScope("all")}
              >
                <span className="cartoes__scope-all-icon" aria-hidden="true">
                  <CardIcon />
                </span>
                <span className="cartoes__statement-card-text">
                  <span className="cartoes__statement-card-bank">Todos os cartões</span>
                  <span className="cartoes__statement-card-name">Soma de {cards.length} cartões</span>
                </span>
              </button>
              {cards.map((card) => {
                const brand = brandOf(card);
                const active = scopeCard?.id === card.id;
                return (
                  <button
                    key={card.id}
                    type="button"
                    role="radio"
                    aria-checked={active}
                    className={`cartoes__statement-card ${active ? "cartoes__statement-card--active" : ""}`}
                    style={{ "--group-color": brand.color ?? "var(--color-border-strong)" }}
                    onClick={() => setSummaryScope(card.id)}
                  >
                    <BankLogo name={brand.name} logoUrl={brand.logoUrl} color={brand.color} size={28} />
                    <span className="cartoes__statement-card-text">
                      <span className="cartoes__statement-card-bank">{brand.name}</span>
                      <span className="cartoes__statement-card-name">{card.name}</span>
                    </span>
                  </button>
                );
              })}
            </div>
          )}
        </div>
      )}

      <div className="cartoes__summary">
        <StatCard
          icon={<CardIcon />}
          label="Limite usado"
          value={loading ? valueSkeleton : formatCurrency(summary.usedLimit)}
          secondaryLines={loading ? [] : scopeLines}
        />
        <StatCard
          icon={<WalletIcon />}
          label="Limite disponível"
          value={loading ? valueSkeleton : formatCurrency(summary.availableLimit)}
          secondaryLines={loading ? [] : scopeLines}
        />
        <StatCard
          icon={<BellIcon />}
          label="Faturas abertas"
          value={loading ? valueSkeleton : formatCurrency(summary.openInvoices)}
          secondaryLines={loading ? [] : scopeLines}
        />
        <StatCard
          icon={<HistoryIcon />}
          label="Próximo vencimento"
          value={loading ? valueSkeleton : summary.nextDue ? formatDate(summary.nextDue.date) : "—"}
          secondaryLines={!loading && summary.nextDue ? [{ label: summary.nextDue.label, value: "" }] : []}
        />
      </div>

      <Card title="Seus cartões">
        {loading ? (
          listSkeleton
        ) : cards.length === 0 ? (
          <p className="cartoes__hint">
            Nenhum cartão reconhecido ainda. Se você tem um cartão sincronizado por um banco conectado,
            ele deve aparecer aqui sozinho na próxima sincronização — ou cadastre um manualmente abaixo.
          </p>
        ) : (
          <div className="cartoes__groups">
            {groups.map((group) => {
              const brand = brandOf(group);
              return (
              <section
                key={group.key}
                className="cartoes__group"
                style={{ "--group-color": group.color ?? "var(--color-border-strong)" }}
              >
                <header className="cartoes__group-header">
                  <div className="cartoes__group-brand">
                    {group.isOthers ? (
                      <span className="cartoes__group-icon" aria-hidden="true">
                        <CardIcon />
                      </span>
                    ) : (
                      <BankLogo name={brand.name} logoUrl={brand.logoUrl} color={brand.color} size={52} />
                    )}
                    <div className="cartoes__group-heading">
                      <h3 className="cartoes__group-name">{group.bankName}</h3>
                      <span className="cartoes__group-count">
                        {group.cards.length} {group.cards.length === 1 ? "cartão" : "cartões"}
                        {group.isOthers && " cadastrados manualmente"}
                      </span>
                    </div>
                  </div>
                  <div className="cartoes__group-meta">
                    <div className="cartoes__group-stat">
                      <span>Fatura aberta</span>
                      <strong>{formatCurrency(group.openInvoice)}</strong>
                    </div>
                    <div className="cartoes__group-stat">
                      <span>Limite usado</span>
                      <strong>{formatCurrency(group.usedLimit)}</strong>
                    </div>
                    {!group.isOthers && (
                      <div className="cartoes__group-stat">
                        <span>Atualizado em</span>
                        <strong>{group.lastSyncedAt ? formatDateTime(group.lastSyncedAt) : "Ainda não"}</strong>
                      </div>
                    )}
                  </div>
                </header>
                <div className="cartoes__grid">{group.cards.map(renderCardBlock)}</div>
              </section>
              );
            })}
          </div>
        )}
      </Card>

      {!loading && <CardStatement cards={cards} getBrand={brandOf} />}

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
                    {item.meta} · {formatDate(item.date)}
                  </span>
                </div>
                <StatusPill status={item.status}>{STATUS_LABELS[item.status] || item.status}</StatusPill>
                <span className="cartoes__list-amount">{item.amount != null ? formatCurrency(item.amount) : "—"}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card title="Cadastrar cartão não sincronizado">
        <p className="cartoes__hint">Pra um cartão que não veio de nenhum banco conectado.</p>
        <form className="cartoes__form" onSubmit={handleCreateManualCard}>
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

          <div className="cartoes__form-actions">
            <Button as="button" type="submit" variant="primary" size="sm" disabled={saving}>
              {saving ? "Adicionando..." : "Adicionar cartão"}
            </Button>
          </div>
        </form>
      </Card>

      <Card title="Parcelamentos">
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
                  onClick={() => handleDeleteInstallment(i.id)}
                  aria-label="Remover parcelamento"
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
