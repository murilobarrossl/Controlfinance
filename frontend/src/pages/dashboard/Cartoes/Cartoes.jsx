import { useEffect, useMemo, useState } from "react";
import { getCreditCardsSummary, createCreditCard, updateCreditCard } from "../../../api/creditCards.js";
import { setBankAccountCardDetails } from "../../../api/bankAccounts.js";
import { getInstallments, createInstallment, deleteInstallment } from "../../../api/installments.js";
import { getTransactions } from "../../../api/transactions.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import IconAvatar from "../../../components/ui/IconAvatar/IconAvatar.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import CreditCardVisual from "../../../components/dashboard/CreditCardVisual/CreditCardVisual.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import "./Cartoes.css";

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
const MAX_UPCOMING = 8;
const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };
const KIND_CLASS = { Fatura: "invoice", Parcela: "installment", Conta: "bill" };

function todayIso() {
  return new Date().toISOString().slice(0, 10);
}

// "Próximos vencimentos" (antes no Dashboard inteligente) junta as três coisas que vencem e que
// esta tela já conhece: fatura de cada cartão (só quando o banco mandou a data de verdade, sem
// estimar a partir do dia de vencimento), a próxima parcela de cada parcelamento e as contas
// pendentes/atrasadas de todas as contas, já que esta página não depende da conta selecionada.
function buildUpcoming(cards, installments, pendingTransactions) {
  const invoices = cards
    .filter((c) => c.invoiceDueDate)
    .map((c) => ({
      key: `invoice-${c.id}`,
      kind: "Fatura",
      name: `Fatura ${c.name}`,
      meta: c.brand || "Cartão de crédito",
      date: c.invoiceDueDate,
      amount: c.currentInvoice,
      status: "Pending",
    }));
  const nextInstallments = installments.map((i) => ({
    key: `installment-${i.id}`,
    kind: "Parcela",
    name: i.description,
    meta: `${i.currentInstallment}/${i.totalInstallments} · ${i.creditCardName || "Sem cartão vinculado"}`,
    date: i.nextDueDate,
    amount: i.installmentAmount,
    status: "Pending",
  }));
  const bills = pendingTransactions.map((t) => ({
    key: `transaction-${t.id}`,
    kind: "Conta",
    name: t.name,
    meta: `${t.categoryName || "Sem categoria"}${t.bankAccountName ? ` · ${t.bankAccountName}` : ""}`,
    date: t.dueDate,
    amount: t.amount,
    status: t.status,
  }));

  return [...invoices, ...nextInstallments, ...bills]
    .sort((a, b) => new Date(a.date) - new Date(b.date))
    .slice(0, MAX_UPCOMING);
}

// Esta página não depende da conta selecionada no seletor lateral (useAccount()): cartões
// reconhecidos (card.isSynced) já vêm prontos de getCreditCardsSummary(), independente de qual
// conta está ativa no seletor — a mesma BankAccount aparece aqui sempre que for identificada como
// cartão, sem relação com qual conta o usuário escolheu ver no Dashboard Inteligente.
export default function Cartoes() {
  const { user } = useAuth();
  const [cards, setCards] = useState([]);
  const [installments, setInstallments] = useState([]);
  const [pendingTransactions, setPendingTransactions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [detailForm, setDetailForm] = useState(EMPTY_DETAIL_FORM);
  const [manualForm, setManualForm] = useState(EMPTY_MANUAL_FORM);
  const [saving, setSaving] = useState(false);
  const [showInstallmentForm, setShowInstallmentForm] = useState(false);
  const [installmentForm, setInstallmentForm] = useState(EMPTY_INSTALLMENT_FORM);

  function loadAll() {
    return Promise.all([
      getCreditCardsSummary(),
      getInstallments(),
      getTransactions({ status: "Overdue", type: "Expense" }),
      getTransactions({ status: "Pending", type: "Expense" }),
    ])
      .then(([cardsData, installmentsData, overdueData, pendingData]) => {
        setCards(cardsData);
        setInstallments(installmentsData);
        setPendingTransactions([...overdueData, ...pendingData]);
      })
      .catch((err) => setError(err.message || "Não foi possível carregar os cartões."))
      .finally(() => setLoading(false));
  }

  useEffect(() => {
    setLoading(true);
    loadAll();
  }, []);

  function openDetailForm(card) {
    setDetailForm({
      id: card.id,
      brand: card.brand || "",
      creditLimit: card.creditLimit != null ? String(card.creditLimit) : "",
      closingDay: card.closingDay != null ? String(card.closingDay) : "1",
      dueDay: card.dueDay != null ? String(card.dueDay) : "10",
    });
  }

  async function handleSubmitDetail(e, card) {
    e.preventDefault();
    if (!detailForm.creditLimit) return;

    const payload = {
      creditLimit: Math.max(0.01, Number(detailForm.creditLimit)),
      closingDay: Math.min(31, Math.max(1, Number(detailForm.closingDay) || 1)),
      dueDay: Math.min(31, Math.max(1, Number(detailForm.dueDay) || 1)),
    };

    setSaving(true);
    try {
      if (card.isSynced) {
        await setBankAccountCardDetails(card.id, payload);
      } else {
        await updateCreditCard(card.id, { ...payload, brand: detailForm.brand || null });
      }
      setDetailForm(EMPTY_DETAIL_FORM);
      await loadAll();
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
      await loadAll();
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
      await loadAll();
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

  async function handleDeleteInstallment(id) {
    try {
      await deleteInstallment(id);
      await loadAll();
    } catch (err) {
      setError(err.message || "Não foi possível remover esse parcelamento.");
    }
  }

  const upcoming = useMemo(
    () => buildUpcoming(cards, installments, pendingTransactions),
    [cards, installments, pendingTransactions]
  );

  if (loading) return <p className="cartoes__hint">Carregando...</p>;

  return (
    <div className="cartoes">
      <SectionHeading kicker="Fatura, limite e parcelamentos" title="Cartões" align="left" />

      {error && <p className="cartoes__error">{error}</p>}

      <p className="cartoes__intro">
        Aqui ficam só os cartões de crédito: o que você gastou no crédito e ainda vai pagar na fatura. O saldo
        da conta corrente (dinheiro que você já tem) continua no Dashboard inteligente, sem ser somado com os
        cartões.
      </p>

      <Card title="Seus cartões">
        {cards.length === 0 ? (
          <p className="cartoes__hint">
            Nenhum cartão reconhecido ainda. Se você tem um cartão sincronizado por um banco conectado,
            ele deve aparecer aqui sozinho na próxima sincronização — ou cadastre um manualmente abaixo.
          </p>
        ) : (
          <div className="cartoes__grid">
            {cards.map((card) => (
              <div key={card.id} className="cartoes__card-block">
                <CreditCardVisual
                  card={card}
                  cardholderName={user?.name}
                  onCompleteFields={!card.hasFullDetails ? () => openDetailForm(card) : undefined}
                />

                {card.hasFullDetails && detailForm.id !== card.id && (
                  <button type="button" className="cartoes__edit-link" onClick={() => openDetailForm(card)}>
                    Editar limite/fatura
                  </button>
                )}

                {detailForm.id === card.id && (
                  <form className="cartoes__form" onSubmit={(e) => handleSubmitDetail(e, card)}>
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
            ))}
          </div>
        )}
      </Card>

      <Card title="Próximos vencimentos">
        {upcoming.length === 0 ? (
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
        {installments.length === 0 ? (
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
          Cartões reconhecidos automaticamente (pela sua conta sincronizada) já têm extrato real
          disponível: selecione essa conta no seletor do cabeçalho e veja transações/categorias em
          Relatórios ou Categorias. Limite, fechamento, vencimento e fatura tentam vir direto da
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
