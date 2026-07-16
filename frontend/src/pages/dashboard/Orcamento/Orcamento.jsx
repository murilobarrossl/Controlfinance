import { useEffect, useMemo, useState } from "react";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import { getCategories } from "../../../api/categories.js";
import { getTransactions, createTransaction, deleteTransaction, setTransactionFixed } from "../../../api/transactions.js";
import { getInstallments, createInstallment, deleteInstallment } from "../../../api/installments.js";
import { getCreditCards } from "../../../api/creditCards.js";
import { getReserve, addToReserve } from "../../../utils/emergencyReserveStorage.js";
import { formatCurrency } from "../../../utils/financeMath.js";
import "./Orcamento.css";

const EMPTY_FIXED_FORM = { name: "", amount: "", categoryId: "", dueDate: todayIso() };
const EMPTY_INSTALLMENT_FORM = {
  description: "",
  totalAmount: "",
  totalInstallments: "1",
  currentInstallment: "1",
  installmentAmount: "",
  nextDueDate: todayIso(),
  creditCardId: "",
};

function todayIso() {
  return new Date().toISOString().slice(0, 10);
}

function formatDate(dateStr) {
  return new Date(dateStr).toLocaleDateString("pt-BR");
}

export default function Orcamento() {
  const [categories, setCategories] = useState([]);
  const [transactions, setTransactions] = useState([]);
  const [installments, setInstallments] = useState([]);
  const [creditCards, setCreditCards] = useState([]);
  const [reserve, setReserve] = useState(() => getReserve());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [markFixedId, setMarkFixedId] = useState("");
  const [showFixedForm, setShowFixedForm] = useState(false);
  const [fixedForm, setFixedForm] = useState(EMPTY_FIXED_FORM);
  const [showInstallmentForm, setShowInstallmentForm] = useState(false);
  const [installmentForm, setInstallmentForm] = useState(EMPTY_INSTALLMENT_FORM);
  const [showReserveForm, setShowReserveForm] = useState(false);
  const [topUpInput, setTopUpInput] = useState("");

  function loadAll() {
    return Promise.all([getCategories(), getTransactions(), getInstallments(), getCreditCards()])
      .then(([categoriesData, transactionsData, installmentsData, creditCardsData]) => {
        setCategories(categoriesData);
        setTransactions(transactionsData);
        setInstallments(installmentsData);
        setCreditCards(creditCardsData);
      })
      .catch((err) => setError(err.message || "Não foi possível carregar o orçamento."))
      .finally(() => setLoading(false));
  }

  useEffect(() => {
    loadAll();
  }, []);

  function refreshTransactions() {
    return getTransactions().then(setTransactions);
  }

  function refreshInstallments() {
    return getInstallments().then(setInstallments);
  }

  const expenses = useMemo(() => transactions.filter((t) => t.type === "Expense"), [transactions]);
  const fixedExpenses = useMemo(() => expenses.filter((t) => t.isFixed), [expenses]);
  const unflaggedExpenses = useMemo(() => expenses.filter((t) => !t.isFixed), [expenses]);
  const totalFixed = useMemo(() => fixedExpenses.reduce((sum, t) => sum + t.amount, 0), [fixedExpenses]);
  const totalInstallments = useMemo(
    () => installments.reduce((sum, i) => sum + i.installmentAmount, 0),
    [installments]
  );

  async function handleMarkFixed() {
    if (!markFixedId) return;
    try {
      await setTransactionFixed(markFixedId, true);
      setMarkFixedId("");
      await refreshTransactions();
    } catch (err) {
      setError(err.message || "Não foi possível marcar essa despesa como fixa.");
    }
  }

  async function handleUnmarkFixed(id) {
    try {
      await setTransactionFixed(id, false);
      await refreshTransactions();
    } catch (err) {
      setError(err.message || "Não foi possível desmarcar essa despesa.");
    }
  }

  async function handleDeleteFixed(id) {
    try {
      await deleteTransaction(id);
      await refreshTransactions();
    } catch (err) {
      setError(err.message || "Não foi possível remover essa despesa.");
    }
  }

  async function handleCreateFixed(e) {
    e.preventDefault();
    if (!fixedForm.name || !fixedForm.amount) return;

    try {
      await createTransaction({
        name: fixedForm.name,
        description: null,
        type: "Expense",
        status: "Pending",
        amount: Math.max(0.01, Number(fixedForm.amount)),
        dueDate: fixedForm.dueDate,
        bankAccountId: null,
        categoryId: fixedForm.categoryId || null,
        isFixed: true,
      });
      setFixedForm(EMPTY_FIXED_FORM);
      setShowFixedForm(false);
      await refreshTransactions();
    } catch (err) {
      setError(err.message || "Não foi possível criar essa despesa fixa.");
    }
  }

  async function handleCreateInstallment(e) {
    e.preventDefault();
    const { description, totalAmount, totalInstallments: totalCount, currentInstallment, installmentAmount, nextDueDate, creditCardId } =
      installmentForm;
    if (!description || !totalAmount || !installmentAmount) return;

    try {
      await createInstallment({
        creditCardId: creditCardId || null,
        description,
        totalAmount: Math.max(0.01, Number(totalAmount)),
        totalInstallments: Math.max(1, Number(totalCount)),
        currentInstallment: Math.max(1, Number(currentInstallment)),
        installmentAmount: Math.max(0.01, Number(installmentAmount)),
        nextDueDate,
      });
      setInstallmentForm(EMPTY_INSTALLMENT_FORM);
      setShowInstallmentForm(false);
      await refreshInstallments();
    } catch (err) {
      setError(err.message || "Não foi possível criar esse parcelamento.");
    }
  }

  async function handleDeleteInstallment(id) {
    try {
      await deleteInstallment(id);
      await refreshInstallments();
    } catch (err) {
      setError(err.message || "Não foi possível remover esse parcelamento.");
    }
  }

  function handleTopUp(e) {
    e.preventDefault();
    if (!topUpInput) return;
    addToReserve(Math.max(0, Number(topUpInput)));
    setReserve(getReserve());
    setTopUpInput("");
    setShowReserveForm(false);
  }

  if (loading) return <p className="orcamento__hint">Carregando...</p>;

  return (
    <div className="orcamento">
      <SectionHeading kicker="Planejamento" title="Orçamento" align="left" />

      {error && <p className="orcamento__error">{error}</p>}

      <Card title="Gastos fixos">
        {fixedExpenses.length === 0 ? (
          <p className="orcamento__hint">Nenhuma despesa marcada como fixa ainda.</p>
        ) : (
          <ul className="orcamento__list">
            {fixedExpenses.map((t) => (
              <li key={t.id} className="orcamento__list-item">
                <div>
                  <span className="orcamento__list-name">{t.name}</span>
                  <span className="orcamento__list-meta">{t.categoryName || "Sem categoria"}</span>
                </div>
                <span className="orcamento__amount--expense">{formatCurrency(t.amount)}</span>
                <button
                  type="button"
                  onClick={() => (t.bankAccountName ? handleUnmarkFixed(t.id) : handleDeleteFixed(t.id))}
                  aria-label={t.bankAccountName ? "Desmarcar como fixa" : "Remover despesa"}
                >
                  {t.bankAccountName ? "Desmarcar" : "×"}
                </button>
              </li>
            ))}
          </ul>
        )}

        <p className="orcamento__total">Total: {formatCurrency(totalFixed)}</p>

        <Button as="button" type="button" variant="primary" size="sm" onClick={() => setShowFixedForm((v) => !v)}>
          Adicionar despesa fixa
        </Button>

        {showFixedForm && (
          <div className="orcamento__add-panel">
            {unflaggedExpenses.length > 0 && (
              <div className="orcamento__mark-fixed">
                <select value={markFixedId} onChange={(e) => setMarkFixedId(e.target.value)}>
                  <option value="">Marcar uma despesa já sincronizada como fixa...</option>
                  {unflaggedExpenses.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name} · {formatCurrency(t.amount)}
                    </option>
                  ))}
                </select>
                <Button
                  as="button"
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={handleMarkFixed}
                  disabled={!markFixedId}
                >
                  Marcar como fixa
                </Button>
              </div>
            )}

            <p className="orcamento__section-hint">Ou crie uma despesa fixa nova, que não está registrada no banco:</p>

            <form className="orcamento__form" onSubmit={handleCreateFixed}>
              <label className="orcamento__field">
                <span className="orcamento__field-label">Nome</span>
                <input
                  type="text"
                  placeholder="Ex: Academia"
                  value={fixedForm.name}
                  onChange={(e) => setFixedForm({ ...fixedForm, name: e.target.value })}
                  required
                />
              </label>
              <label className="orcamento__field">
                <span className="orcamento__field-label">Valor</span>
                <CurrencyInput
                  placeholder="Ex: 120"
                  value={fixedForm.amount}
                  onChange={(e) => setFixedForm({ ...fixedForm, amount: e.target.value })}
                  required
                />
              </label>
              <label className="orcamento__field">
                <span className="orcamento__field-label">Categoria (opcional)</span>
                <select
                  value={fixedForm.categoryId}
                  onChange={(e) => setFixedForm({ ...fixedForm, categoryId: e.target.value })}
                >
                  <option value="">Sem categoria</option>
                  {categories.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="orcamento__field">
                <span className="orcamento__field-label">Vencimento</span>
                <input
                  type="date"
                  value={fixedForm.dueDate}
                  onChange={(e) => setFixedForm({ ...fixedForm, dueDate: e.target.value })}
                  required
                />
              </label>
              <Button as="button" type="submit" variant="primary" size="sm">
                Adicionar
              </Button>
            </form>
          </div>
        )}
      </Card>

      <Card title="Gastos variáveis">
        {installments.length === 0 ? (
          <p className="orcamento__hint">Nenhum parcelamento cadastrado ainda.</p>
        ) : (
          <ul className="orcamento__list">
            {installments.map((i) => (
              <li key={i.id} className="orcamento__list-item">
                <div>
                  <span className="orcamento__list-name">{i.description}</span>
                  <span className="orcamento__list-meta">
                    {i.currentInstallment}/{i.totalInstallments} · {i.creditCardName || "Sem cartão vinculado"} · próx.{" "}
                    {formatDate(i.nextDueDate)}
                  </span>
                </div>
                <span className="orcamento__amount--expense">{formatCurrency(i.installmentAmount)}</span>
                <button type="button" onClick={() => handleDeleteInstallment(i.id)} aria-label="Remover parcelamento">
                  ×
                </button>
              </li>
            ))}
          </ul>
        )}

        <p className="orcamento__total">Total: {formatCurrency(totalInstallments)}</p>

        <Button as="button" type="button" variant="primary" size="sm" onClick={() => setShowInstallmentForm((v) => !v)}>
          Adicionar despesa variável
        </Button>

        {showInstallmentForm && (
          <form className="orcamento__form" onSubmit={handleCreateInstallment}>
            <label className="orcamento__field">
              <span className="orcamento__field-label">Descrição</span>
              <input
                type="text"
                placeholder="Ex: Geladeira nova"
                value={installmentForm.description}
                onChange={(e) => setInstallmentForm({ ...installmentForm, description: e.target.value })}
                required
              />
            </label>
            <label className="orcamento__field">
              <span className="orcamento__field-label">Valor total</span>
              <CurrencyInput
                placeholder="Ex: 2400"
                value={installmentForm.totalAmount}
                onChange={(e) => setInstallmentForm({ ...installmentForm, totalAmount: e.target.value })}
                required
              />
            </label>
            <label className="orcamento__field">
              <span className="orcamento__field-label">Total de parcelas</span>
              <input
                type="number"
                min="1"
                step="1"
                value={installmentForm.totalInstallments}
                onChange={(e) => setInstallmentForm({ ...installmentForm, totalInstallments: e.target.value })}
                required
              />
            </label>
            <label className="orcamento__field">
              <span className="orcamento__field-label">Parcela atual</span>
              <input
                type="number"
                min="1"
                step="1"
                value={installmentForm.currentInstallment}
                onChange={(e) => setInstallmentForm({ ...installmentForm, currentInstallment: e.target.value })}
                required
              />
            </label>
            <label className="orcamento__field">
              <span className="orcamento__field-label">Valor da parcela</span>
              <CurrencyInput
                placeholder="Ex: 200"
                value={installmentForm.installmentAmount}
                onChange={(e) => setInstallmentForm({ ...installmentForm, installmentAmount: e.target.value })}
                required
              />
            </label>
            <label className="orcamento__field">
              <span className="orcamento__field-label">Próximo vencimento</span>
              <input
                type="date"
                value={installmentForm.nextDueDate}
                onChange={(e) => setInstallmentForm({ ...installmentForm, nextDueDate: e.target.value })}
                required
              />
            </label>
            <label className="orcamento__field">
              <span className="orcamento__field-label">Cartão (opcional)</span>
              <select
                value={installmentForm.creditCardId}
                onChange={(e) => setInstallmentForm({ ...installmentForm, creditCardId: e.target.value })}
              >
                <option value="">Sem cartão</option>
                {creditCards.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </label>
            <Button as="button" type="submit" variant="primary" size="sm">
              Adicionar
            </Button>
          </form>
        )}
      </Card>

      <Card title="Reserva de emergência">
        <p className="orcamento__reserve-value">{formatCurrency(reserve.currentAmount)}</p>

        <Button as="button" type="button" variant="primary" size="sm" onClick={() => setShowReserveForm((v) => !v)}>
          Adicionar reserva
        </Button>

        {showReserveForm && (
          <form className="orcamento__form" onSubmit={handleTopUp}>
            <label className="orcamento__field">
              <span className="orcamento__field-label">Valor</span>
              <CurrencyInput placeholder="Ex: 300" value={topUpInput} onChange={(e) => setTopUpInput(e.target.value)} />
            </label>
            <Button as="button" type="submit" variant="primary" size="sm">
              Adicionar
            </Button>
          </form>
        )}
      </Card>
    </div>
  );
}
