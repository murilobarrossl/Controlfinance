import { useEffect, useState } from "react";
import { Link } from "react-router";
import { getCreditCardsSummary, createCreditCard, updateCreditCard } from "../../../api/creditCards.js";
import { setBankAccountCardDetails } from "../../../api/bankAccounts.js";
import { getInstallments } from "../../../api/installments.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import IconAvatar from "../../../components/ui/IconAvatar/IconAvatar.jsx";
import CreditCardVisual from "../../../components/dashboard/CreditCardVisual/CreditCardVisual.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import "./Cartoes.css";

const EMPTY_DETAIL_FORM = { id: null, brand: "", creditLimit: "", closingDay: "1", dueDay: "10" };
const EMPTY_MANUAL_FORM = { name: "", brand: "", creditLimit: "", closingDay: "1", dueDay: "10" };

// Esta página não depende da conta selecionada no seletor lateral (useAccount()): cartões
// reconhecidos (card.isSynced) já vêm prontos de getCreditCardsSummary(), independente de qual
// conta está ativa no seletor — a mesma BankAccount aparece aqui sempre que for identificada como
// cartão, sem relação com qual conta o usuário escolheu ver no Dashboard Inteligente.
export default function Cartoes() {
  const { user } = useAuth();
  const [cards, setCards] = useState([]);
  const [installments, setInstallments] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [detailForm, setDetailForm] = useState(EMPTY_DETAIL_FORM);
  const [manualForm, setManualForm] = useState(EMPTY_MANUAL_FORM);
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

  if (loading) return <p className="cartoes__hint">Carregando...</p>;

  return (
    <div className="cartoes">
      <SectionHeading kicker="Fatura, limite e parcelamentos" title="Cartões" align="left" />

      {error && <p className="cartoes__error">{error}</p>}

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
