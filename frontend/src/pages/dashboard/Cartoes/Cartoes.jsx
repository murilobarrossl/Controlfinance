import { useEffect, useState } from "react";
import { Link } from "react-router";
import { getCreditCardsSummary, createCreditCard } from "../../../api/creditCards.js";
import { getInstallments } from "../../../api/installments.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import IconAvatar from "../../../components/ui/IconAvatar/IconAvatar.jsx";
import CreditCardVisual from "../../../components/dashboard/CreditCardVisual/CreditCardVisual.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import "./Cartoes.css";

const EMPTY_CARD_FORM = { name: "", brand: "", creditLimit: "", closingDay: "1", dueDay: "10", bankAccountId: "" };

// Só lê accounts do AccountContext (não selectedAccountId/effectiveAccountId): serve pra listar
// candidatas a vincular no formulário abaixo, não pra filtrar os dados da página. Os dados
// principais (cartões, parcelamentos) continuam independentes de qual conta está selecionada no
// seletor lateral — cartão de crédito aqui vem do cadastro manual (CreditCard), o vínculo com
// BankAccount é opcional (ver nota no card "Sobre os dados" abaixo).
export default function Cartoes() {
  const { user } = useAuth();
  const { accounts } = useAccount();
  const [cards, setCards] = useState([]);
  const [installments, setInstallments] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [cardForm, setCardForm] = useState(EMPTY_CARD_FORM);
  const [saving, setSaving] = useState(false);

  function loadAll() {
    return Promise.all([getCreditCardsSummary(), getInstallments()])
      .then(([cardsData, installmentsData]) => {
        setCards(cardsData);
        setInstallments(installmentsData);
      })
      .catch((err) => setError(err.message || "Não foi possível carregar os cartões."))
      .finally(() => setLoading(false));
  }

  useEffect(() => {
    setLoading(true);
    loadAll();
  }, []);

  async function handleCreateCard(e) {
    e.preventDefault();
    if (!cardForm.name || !cardForm.creditLimit) return;

    setSaving(true);
    try {
      await createCreditCard({
        name: cardForm.name,
        brand: cardForm.brand || null,
        creditLimit: Math.max(0.01, Number(cardForm.creditLimit)),
        closingDay: Math.min(31, Math.max(1, Number(cardForm.closingDay) || 1)),
        dueDay: Math.min(31, Math.max(1, Number(cardForm.dueDay) || 1)),
        bankAccountId: cardForm.bankAccountId || null,
      });
      setCardForm(EMPTY_CARD_FORM);
      await loadAll();
    } catch (err) {
      setError(err.message || "Não foi possível cadastrar esse cartão.");
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <p className="cartoes__hint">Carregando...</p>;

  // Contas já vinculadas a outro cartão saem da lista: o backend rejeitaria de qualquer forma
  // (índice único em CreditCards.BankAccountId), então nem oferece a opção aqui.
  const linkedAccountIds = new Set(cards.map((c) => c.card.bankAccountId).filter(Boolean));
  const availableAccounts = accounts.filter((a) => !linkedAccountIds.has(a.id));

  return (
    <div className="cartoes">
      <SectionHeading kicker="Fatura, limite e parcelamentos" title="Cartões" align="left" />

      {error && <p className="cartoes__error">{error}</p>}

      <Card title="Seus cartões">
        {cards.length === 0 ? (
          <p className="cartoes__hint">
            Nenhum cartão cadastrado ainda. Use o formulário abaixo pra adicionar o primeiro.
          </p>
        ) : (
          <div className="cartoes__grid">
            {cards.map((c) => (
              <CreditCardVisual
                key={c.card.id}
                card={c.card}
                cardholderName={user?.name}
                currentInvoice={c.currentInvoice}
                invoiceDueDate={c.invoiceDueDate}
                activeInstallmentsCount={c.installments?.length ?? 0}
                linkedAccountName={c.card.bankAccountName}
              />
            ))}
          </div>
        )}
      </Card>

      <Card title="Adicionar cartão">
        <form className="cartoes__form" onSubmit={handleCreateCard}>
          <label className="cartoes__field">
            <span className="cartoes__field-label">Nome</span>
            <input
              type="text"
              placeholder="Ex: Nubank Roxinho"
              maxLength={80}
              value={cardForm.name}
              onChange={(e) => setCardForm({ ...cardForm, name: e.target.value })}
              required
            />
          </label>
          <label className="cartoes__field">
            <span className="cartoes__field-label">Bandeira (opcional)</span>
            <input
              type="text"
              placeholder="Ex: Mastercard"
              maxLength={50}
              value={cardForm.brand}
              onChange={(e) => setCardForm({ ...cardForm, brand: e.target.value })}
            />
          </label>
          <label className="cartoes__field">
            <span className="cartoes__field-label">Limite de crédito</span>
            <CurrencyInput
              placeholder="Ex: 5000"
              value={cardForm.creditLimit}
              onChange={(e) => setCardForm({ ...cardForm, creditLimit: e.target.value })}
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
              value={cardForm.closingDay}
              onChange={(e) => setCardForm({ ...cardForm, closingDay: e.target.value })}
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
              value={cardForm.dueDay}
              onChange={(e) => setCardForm({ ...cardForm, dueDay: e.target.value })}
              required
            />
          </label>
          <label className="cartoes__field">
            <span className="cartoes__field-label">Conta vinculada (opcional)</span>
            <select
              value={cardForm.bankAccountId}
              onChange={(e) => setCardForm({ ...cardForm, bankAccountId: e.target.value })}
            >
              <option value="">Nenhuma (só cadastro manual)</option>
              {availableAccounts.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.name}
                </option>
              ))}
            </select>
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
              </li>
            ))}
          </ul>
        )}

        <p className="cartoes__form-hint">
          Pra adicionar ou remover um parcelamento, use a aba{" "}
          <Link to="/dashboard/orcamento">Orçamento</Link>.
        </p>
      </Card>

      <Card title="Sobre os dados desta página">
        <p className="cartoes__hint">
          Fatura, limite e parcelamentos vêm do cadastro manual de cartão (acima). Cartões com uma
          conta vinculada já têm extrato real disponível: selecione essa conta no seletor do
          cabeçalho e veja transações/categorias em Relatórios ou Categorias. Cartões sem vínculo
          ainda não têm como gerar esse tipo de relatório de forma confiável — é um ponto de
          integração pra evoluir mais adiante, não algo que esta tela tenta simular.
        </p>
      </Card>
    </div>
  );
}
