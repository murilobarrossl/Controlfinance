import { useState } from "react";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import { CloseIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import {
  addToReserve,
  createGoal,
  deleteGoal,
  discardLegacyReserve,
  setReserve as setReserveAmount,
  setReserveAccount,
  updateGoalProgress,
  withdrawFromReserve,
} from "../../../api/planning.js";
import { formatCurrency } from "../../../utils/financeMath.js";
import useSaving from "./useSaving.js";
import { classifyInvestmentReserve } from "../../../api/investments.js";
import { formatMoney, sumMoney } from "../../../utils/importedInvestments.js";

const EMPTY_GOAL_FORM = { name: "", targetAmount: "", currentAmount: "", deadline: "" };

// O <input type="date"> grava "AAAA-MM-DD". new Date() lê isso como meia-noite UTC, o que no
// fuso do Brasil vira o dia anterior: formata direto da string pra o prazo não "voltar" um dia.
function formatIsoDate(iso) {
  const [year, month, day] = iso.split("-");
  return `${day}/${month}/${year}`;
}

function parseAmount(raw) {
  const value = Number(raw);
  return Number.isFinite(value) ? value : NaN;
}

function ReserveAccountPicker({ items, planning }) {
  const { saving, error, run } = useSaving();
  const reserve = planning.reserve;
  const selected = reserve?.importedInvestmentId ? `investment:${reserve.importedInvestmentId}`
    : reserve?.importedReservedBalanceId ? `reserved:${reserve.importedReservedBalanceId}` : reserve?.bankAccountId ?? "";

  async function handleChange(e) {
    const key = e.target.value;
    const item = items.find((entry) => (entry.key ?? entry.account?.id) === key);
    await run(async () => planning.setReserve(item?.key
      ? await classifyInvestmentReserve(item, true) : await setReserveAccount(key || null)));
  }

  return (
    <div className="investimentos__form investimentos__form--compact">
      <label className="investimentos__field">
        <span className="investimentos__field-label">Investimento usado como reserva</span>
        <select value={selected} onChange={handleChange} disabled={saving}>
          <option value="">Nenhum: anotar um valor à mão</option>
          {selected && !items.some((entry) => (entry.key ?? entry.account?.id) === selected) && <option value={selected} disabled>Recurso vinculado · dados não carregados</option>}
          {items.map((item) => (
            <option key={item.key ?? item.account.id} value={item.key ?? item.account.id}>
              {item.name ?? item.account.name} · {item.institution.name}{item.accountNumber ? ` · conta ${item.accountNumber}` : ""}
            </option>
          ))}
        </select>
      </label>
      {error && (
        <p className="investimentos__error investimentos__error--form" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

function LinkedReserve({ item }) {
  const { account, institution, modality } = item;
  return (
    <div className="investimentos__reserve-linked">
      <BankLogo name={institution.name} logoUrl={institution.logoUrl} color={institution.color} />
      <div className="investimentos__reserve-linked-info">
        <span className="investimentos__reserve-linked-name">{item.name ?? account.name}</span>
        <span className="investimentos__reserve-linked-meta">
          {modality.label} · {institution.name}{item.accountNumber ? ` · conta ${item.accountNumber}` : ""}
        </span>
      </div>
      <span className="investimentos__reserve-linked-value">{item.key
        ? item.amounts.length ? item.amounts.map((money, index) => <span className="inv-real__amount" key={index}>{formatMoney(money)}</span>) : "Não informado"
        : formatCurrency(account.balance)}</span>
      {item.key && !item.currentAmountAvailable && <small className="investimentos__hint">Último valor informado</small>}
    </div>
  );
}

export function EmergencyReserveCard({ planning, items, reserveItems }) {
  const { reserve, legacyReserveAmount, setReserve, clearLegacyReserveNotice } = planning;
  const currentAmount = reserve?.currentAmount ?? 0;
  const linked = reserve?.bankAccountId || reserve?.importedInvestmentId || reserve?.importedReservedBalanceId;
  const [panel, setPanel] = useState(null); // null | "add" | "remove"
  const [amountInput, setAmountInput] = useState("");
  const [confirmingClear, setConfirmingClear] = useState(false);
  const { saving, error, setError, run } = useSaving();

  function closePanel() {
    setPanel(null);
    setAmountInput("");
    setConfirmingClear(false);
    setError(null);
  }

  function togglePanel(next) {
    setAmountInput("");
    setConfirmingClear(false);
    setError(null);
    setPanel((prev) => (prev === next ? null : next));
  }

  async function handleSubmit(e) {
    e.preventDefault();
    const amount = parseAmount(amountInput);
    if (!(amount > 0)) {
      setError("Informe um valor maior que zero.");
      return;
    }
    const ok = await run(async () => setReserve(await (panel === "add" ? addToReserve(amount) : withdrawFromReserve(amount))));
    if (ok) closePanel();
  }

  async function handleClear() {
    const ok = await run(async () => setReserve(await setReserveAmount(0)));
    if (ok) closePanel();
  }

  async function applyLegacyAmount() {
    const ok = await run(async () => setReserve(await setReserveAmount(legacyReserveAmount)));
    if (ok) {
      discardLegacyReserve();
      clearLegacyReserveNotice();
    }
  }

  function keepAccountAmount() {
    discardLegacyReserve();
    clearLegacyReserveNotice();
  }

  return (
    <Card title="Reserva de emergência">
      {(items.length > 0 || linked) && <ReserveAccountPicker items={items} planning={planning} />}

      {reserveItems.length > 0 ? (
        <>
          {reserveItems.map((item) => <LinkedReserve key={item.key ?? item.account.id} item={item} />)}
          {reserveItems.length > 1 && reserveItems.every((item) => item.key) && (
            <p className="investimentos__reserve-total">
              <span>Total da reserva</span>
              <strong>{sumMoney(reserveItems.flatMap((item) => item.amounts ?? [])).map(formatMoney).join(" · ") || "Não informado"}</strong>
            </p>
          )}
          <p className="investimentos__fineprint">
            O valor da reserva acompanha o saldo que o banco informa. Caixinhas chamadas “Reserva de emergência” entram automaticamente; para tirar uma, abra o produto e desmarque.
            {currentAmount > 0 && ` O valor anotado antes, de ${formatCurrency(currentAmount)}, continua guardado e volta se você desmarcar.`}
          </p>
        </>
      ) : linked ? (
        <p className="investimentos__hint">A classificação foi preservada. O recurso vinculado ainda não foi carregado ou não está disponível nesta conexão.</p>
      ) : (
        <>
          <p className="investimentos__reserve-value">{formatCurrency(currentAmount)}</p>

          {legacyReserveAmount != null && (
            <div className="investimentos__confirm investimentos__confirm--notice" role="status">
              <p>
                Este navegador tem uma reserva anotada antes, de {formatCurrency(legacyReserveAmount)}, diferente da salva
                na sua conta.
              </p>
              <div className="investimentos__actions">
                <Button as="button" type="button" variant="primary" size="sm" onClick={applyLegacyAmount} disabled={saving}>
                  Usar {formatCurrency(legacyReserveAmount)}
                </Button>
                <Button as="button" type="button" variant="outline" size="sm" onClick={keepAccountAmount} disabled={saving}>
                  Manter {formatCurrency(currentAmount)}
                </Button>
              </div>
            </div>
          )}

          <div className="investimentos__actions">
            <Button as="button" type="button" variant="primary" size="sm" onClick={() => togglePanel("add")}>
              Adicionar à reserva
            </Button>
            <Button as="button" type="button" variant="secondary" size="sm" onClick={() => togglePanel("remove")}>
              Retirar da reserva
            </Button>
          </div>

          {panel && (
            <form className="investimentos__form investimentos__form--inline" onSubmit={handleSubmit}>
              <label className="investimentos__field">
                <span className="investimentos__field-label">{panel === "add" ? "Valor a adicionar" : "Valor a retirar"}</span>
                <CurrencyInput placeholder="Ex: 300,00" value={amountInput} onChange={(e) => setAmountInput(e.target.value)} />
              </label>
              <div className="investimentos__actions">
                <Button
                  as="button"
                  type="submit"
                  variant={panel === "add" ? "primary" : "secondary"}
                  size="sm"
                  disabled={confirmingClear || saving}
                >
                  {saving && !confirmingClear ? "Salvando…" : panel === "add" ? "Adicionar" : "Retirar"}
                </Button>
                {panel === "remove" && !confirmingClear && (
                  <Button as="button" type="button" variant="outline" size="sm" onClick={() => setConfirmingClear(true)} disabled={saving}>
                    Zerar reserva
                  </Button>
                )}
                <Button as="button" type="button" variant="outline" size="sm" onClick={closePanel} disabled={saving}>
                  Cancelar
                </Button>
              </div>
              {confirmingClear && (
                <div className="investimentos__confirm" role="alert">
                  <p>Zerar a reserva de {formatCurrency(currentAmount)}? Não dá para desfazer.</p>
                  <div className="investimentos__actions">
                    <Button as="button" type="button" variant="primary" size="sm" onClick={handleClear} disabled={saving}>
                      {saving ? "Salvando…" : "Zerar"}
                    </Button>
                    <Button as="button" type="button" variant="outline" size="sm" onClick={() => setConfirmingClear(false)} disabled={saving}>
                      Manter
                    </Button>
                  </div>
                </div>
              )}
            </form>
          )}

          {error && (
            <p className="investimentos__error" role="alert">
              {error}
            </p>
          )}
        </>
      )}
    </Card>
  );
}

export function GoalsCard({ planning }) {
  const { goals, updateGoals } = planning;
  const [goalForm, setGoalForm] = useState(EMPTY_GOAL_FORM);
  const [editingGoalId, setEditingGoalId] = useState(null);
  const [progressInput, setProgressInput] = useState("");
  const [confirmingDeleteId, setConfirmingDeleteId] = useState(null);
  const create = useSaving();
  const change = useSaving();

  async function handleCreateGoal(e) {
    e.preventDefault();
    const name = goalForm.name.trim();
    const targetAmount = parseAmount(goalForm.targetAmount);
    if (!name || !(targetAmount > 0)) {
      create.setError("Informe o nome da meta e um valor alvo maior que zero.");
      return;
    }

    const ok = await create.run(async () => {
      const goal = await createGoal({
        name,
        targetAmount,
        currentAmount: Math.max(0, parseAmount(goalForm.currentAmount) || 0),
        deadline: goalForm.deadline || null,
      });
      updateGoals((list) => [goal, ...list]);
    });
    if (ok) setGoalForm(EMPTY_GOAL_FORM);
  }

  async function handleDeleteGoal(id) {
    const ok = await change.run(async () => {
      await deleteGoal(id);
      updateGoals((list) => list.filter((goal) => goal.id !== id));
    });
    if (ok) setConfirmingDeleteId(null);
  }

  function startProgressEdit(goal) {
    change.setError(null);
    setConfirmingDeleteId(null);
    setEditingGoalId(goal.id);
    setProgressInput(String(goal.currentAmount));
  }

  async function handleSaveProgress(e, id) {
    e.preventDefault();
    const amount = Math.max(0, parseAmount(progressInput) || 0);
    const ok = await change.run(async () => {
      const updated = await updateGoalProgress(id, amount);
      updateGoals((list) => list.map((goal) => (goal.id === id ? updated : goal)));
    });
    if (ok) setEditingGoalId(null);
  }

  return (
    <Card title="Metas de economia">
      <form className="investimentos__form" onSubmit={handleCreateGoal}>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Nome da meta</span>
          <input
            type="text"
            placeholder="Ex: Viagem pra praia"
            maxLength={100}
            value={goalForm.name}
            onChange={(e) => setGoalForm({ ...goalForm, name: e.target.value })}
            required
          />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Valor alvo</span>
          <CurrencyInput
            placeholder="Ex: 5.000,00"
            value={goalForm.targetAmount}
            onChange={(e) => setGoalForm({ ...goalForm, targetAmount: e.target.value })}
            required
          />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Já guardado (opcional)</span>
          <CurrencyInput
            placeholder="Ex: 1.200,00"
            value={goalForm.currentAmount}
            onChange={(e) => setGoalForm({ ...goalForm, currentAmount: e.target.value })}
          />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Prazo (opcional)</span>
          <input
            type="date"
            value={goalForm.deadline}
            onChange={(e) => setGoalForm({ ...goalForm, deadline: e.target.value })}
          />
        </label>
        <Button as="button" type="submit" variant="primary" size="md" disabled={create.saving}>
          {create.saving ? "Salvando…" : "Criar meta"}
        </Button>
        {create.error && (
          <p className="investimentos__error investimentos__error--form" role="alert">
            {create.error}
          </p>
        )}
      </form>

      {goals.length === 0 ? (
        <p className="investimentos__hint">Nenhuma meta criada ainda.</p>
      ) : (
        <ul className="investimentos__goals">
          {goals.map((goal) => {
            const progress = goal.targetAmount > 0 ? Math.min(100, (goal.currentAmount / goal.targetAmount) * 100) : 0;
            const remaining = Math.max(0, goal.targetAmount - goal.currentAmount);
            const active = confirmingDeleteId === goal.id || editingGoalId === goal.id;
            return (
              <li key={goal.id} className="investimentos__goal">
                <div className="investimentos__goal-header">
                  <span className="investimentos__goal-name">{goal.name}</span>
                  <span className="investimentos__goal-percent">{Math.round(progress)}%</span>
                  <button
                    type="button"
                    onClick={() => {
                      change.setError(null);
                      setEditingGoalId(null);
                      setConfirmingDeleteId(goal.id);
                    }}
                    aria-label={`Remover meta ${goal.name}`}
                    aria-expanded={confirmingDeleteId === goal.id}
                  >
                    <CloseIcon />
                  </button>
                </div>
                <div
                  className="investimentos__progress"
                  role="progressbar"
                  aria-label={`Progresso da meta ${goal.name}`}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={Math.round(progress)}
                >
                  <div className="investimentos__progress-fill" style={{ width: `${progress}%` }} />
                </div>
                <div className="investimentos__goal-footer">
                  <span className="investimentos__goal-meta">
                    {formatCurrency(goal.currentAmount)} de {formatCurrency(goal.targetAmount)}
                    {remaining > 0 ? ` · faltam ${formatCurrency(remaining)}` : " · meta atingida"}
                    {goal.deadline && ` · até ${formatIsoDate(goal.deadline)}`}
                  </span>
                  {editingGoalId !== goal.id && (
                    <button type="button" className="investimentos__link-button" onClick={() => startProgressEdit(goal)}>
                      Atualizar valor guardado
                    </button>
                  )}
                </div>
                {confirmingDeleteId === goal.id && (
                  <div className="investimentos__confirm" role="alert">
                    <p>Remover a meta “{goal.name}”? Não dá para desfazer.</p>
                    <div className="investimentos__actions">
                      <Button
                        as="button"
                        type="button"
                        variant="primary"
                        size="sm"
                        onClick={() => handleDeleteGoal(goal.id)}
                        disabled={change.saving}
                      >
                        {change.saving ? "Removendo…" : "Remover"}
                      </Button>
                      <Button
                        as="button"
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={() => setConfirmingDeleteId(null)}
                        disabled={change.saving}
                      >
                        Manter
                      </Button>
                    </div>
                  </div>
                )}
                {editingGoalId === goal.id && (
                  <form className="investimentos__form investimentos__form--inline" onSubmit={(e) => handleSaveProgress(e, goal.id)}>
                    <label className="investimentos__field">
                      <span className="investimentos__field-label">Valor guardado</span>
                      <CurrencyInput value={progressInput} onChange={(e) => setProgressInput(e.target.value)} />
                    </label>
                    <div className="investimentos__actions">
                      <Button as="button" type="submit" variant="primary" size="sm" disabled={change.saving}>
                        {change.saving ? "Salvando…" : "Salvar"}
                      </Button>
                      <Button
                        as="button"
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={() => setEditingGoalId(null)}
                        disabled={change.saving}
                      >
                        Cancelar
                      </Button>
                    </div>
                  </form>
                )}
                {active && change.error && (
                  <p className="investimentos__error" role="alert">
                    {change.error}
                  </p>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}
