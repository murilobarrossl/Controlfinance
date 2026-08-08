import { useEffect, useState } from "react";
import { Link } from "react-router";
import { getCreditCardsSummary, createCreditCard, updateCreditCard } from "../../../api/creditCards.js";
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

const EMPTY_DETAIL_FORM = { key: null, brand: "", creditLimit: "", closingDay: "1", dueDay: "10" };
const EMPTY_MANUAL_FORM = { name: "", brand: "", creditLimit: "", closingDay: "1", dueDay: "10", bankAccountId: "" };

// Identifica cada item da lista unificada (RecognizedCardDto): uma conta reconhecida ainda sem
// CreditCard usa o id da conta; com CreditCard (reconhecida ou 100% manual) usa o id do cartão.
function cardKey(item) {
  return item.bankAccountId ?? item.details?.card?.id;
}

// Só lê accounts do AccountContext (não selectedAccountId/effectiveAccountId): serve só pra
// popular o formulário de cadastro manual (seção secundária, pra cartão não sincronizado). Os
// dados principais da página (cartões reconhecidos, parcelamentos) são independentes de qual
// conta está selecionada no seletor lateral.
export default function Cartoes() {
  const { user } = useAuth();
  const { accounts } = useAccount();
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

  function openComplete(item) {
    setDetailForm({ key: cardKey(item), brand: "", creditLimit: "", closingDay: "1", dueDay: "10" });
  }

  function openEdit(item) {
    const c = item.details.card;
    setDetailForm({
      key: cardKey(item),
      brand: c.brand || "",
      creditLimit: String(c.creditLimit),
      closingDay: String(c.closingDay),
      dueDay: String(c.dueDay),
    });
  }

  async function handleSubmitDetail(e, item) {
    e.preventDefault();
    if (!detailForm.creditLimit) return;

    const payload = {
      brand: detailForm.brand || null,
      creditLimit: Math.max(0.01, Number(detailForm.creditLimit)),
      closingDay: Math.min(31, Math.max(1, Number(detailForm.closingDay) || 1)),
      dueDay: Math.min(31, Math.max(1, Number(detailForm.dueDay) || 1)),
    };

    setSaving(true);
    try {
      if (item.hasDetails) {
        await updateCreditCard(item.details.card.id, payload);
      } else {
        await createCreditCard({ ...payload, name: item.bankAccountName, bankAccountId: item.bankAccountId });
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
        bankAccountId: manualForm.bankAccountId || null,
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

  // Contas já reconhecidas (aparecem em `cards`, com ou sem detalhes) saem da lista do cadastro
  // manual: ou já têm um card automático acima, ou (se vinculadas via esse mesmo formulário) o
  // backend rejeitaria uma 2ª vinculação de qualquer forma.
  const linkedAccountIds = new Set(cards.map((c) => c.bankAccountId).filter(Boolean));
  const availableAccounts = accounts.filter((a) => !linkedAccountIds.has(a.id));

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
            {cards.map((item) => {
              const key = cardKey(item);
              return (
                <div key={key} className="cartoes__card-block">
                  <CreditCardVisual
                    recognized={item}
                    cardholderName={user?.name}
                    onComplete={!item.hasDetails ? () => openComplete(item) : undefined}
                  />

                  {item.hasDetails && detailForm.key !== key && (
                    <button type="button" className="cartoes__edit-link" onClick={() => openEdit(item)}>
                      Editar limite/fatura
                    </button>
                  )}

                  {detailForm.key === key && (
                    <form className="cartoes__form" onSubmit={(e) => handleSubmitDetail(e, item)}>
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
            })}
          </div>
        )}
      </Card>

      <Card title="Cadastrar cartão não sincronizado">
        <p className="cartoes__hint">
          Pra um cartão que não veio de nenhum banco conectado (ou que a sincronização ainda não
          reconheceu automaticamente).
        </p>
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
          <label className="cartoes__field">
            <span className="cartoes__field-label">Conta vinculada (opcional)</span>
            <select
              value={manualForm.bankAccountId}
              onChange={(e) => setManualForm({ ...manualForm, bankAccountId: e.target.value })}
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
          Cartões reconhecidos automaticamente (pela sua conta sincronizada) já têm extrato real
          disponível: selecione essa conta no seletor do cabeçalho e veja transações/categorias em
          Relatórios ou Categorias. Limite, fechamento e vencimento continuam vindo de cadastro
          manual — a Polp não expõe esses dados hoje. Um relatório de gastos por categoria dentro do
          próprio cartão ainda não é possível de forma confiável (falta vínculo entre lançamentos e
          cartão no sistema) — é um ponto de integração pra evoluir mais adiante, não algo que esta
          tela tenta simular.
        </p>
      </Card>
    </div>
  );
}
