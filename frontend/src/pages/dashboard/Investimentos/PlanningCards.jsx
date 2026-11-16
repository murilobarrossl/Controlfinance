import { useState } from "react";
import Button from "../../../components/ui/Button/Button.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import DateInput from "../../../components/ui/DateInput/DateInput.jsx";
import { InstitutionLogo, InvestmentIcon } from "./InvestmentMarks.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { CloseIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { createGoal, deleteGoal, removeReserveAccount, setGoalResource, setReserveResource } from "../../../api/planning.js";
import { formatCurrency } from "../../../utils/financeMath.js";
import { formatMoney, goalOf, isReserve } from "../../../utils/importedInvestments.js";
import useSaving from "./useSaving.js";
import { summarize } from "./planningSummary.js";

const EMPTY_GOAL_FORM = { name: "", targetAmount: "", deadline: "" };

// O prazo é guardado como "AAAA-MM-DD". new Date() lê isso como meia-noite UTC, o que no
// fuso do Brasil vira o dia anterior: formata direto da string pra o prazo não "voltar" um dia.
function formatIsoDate(iso) {
  const [year, month, day] = iso.split("-");
  return `${day}/${month}/${year}`;
}

const optionLabel = (item) => `${item.name} · ${item.institution.name}${item.accountNumber ? ` · conta ${item.accountNumber}` : ""}`;

// Só entra no seletor o que ainda não conta para nenhum destino.
const freeItems = (items, planning) => items.filter((item) => !isReserve(item, planning.reserve, planning.goals) && !goalOf(item, planning.goals));

function ResourceRow({ entry, onRemove, removeLabel, saving }) {
  const item = entry.item;
  const amounts = (item?.amounts ?? []).filter(Boolean);
  return (
    <li className="inv-link">
      {item ? <InvestmentIcon item={item} /> : <span className="inv-mark" aria-hidden="true" />}
      <span className="inv-link__titles">
        <span className="inv-link__name">{item ? item.name : entry.status === "loading" ? <Skeleton width="8rem" height="0.9rem" /> : entry.status === "error" ? "Não foi possível carregar agora" : "Investimento indisponível"}</span>
        <span className="inv-link__meta">
          {entry.status === "loading" || entry.status === "error" ? null
            : !item ? "Não está mais disponível nesta conexão"
            : entry.legacy ? "Conta bancária"
            : <><InstitutionLogo institution={item.institution} /><span className="inv-row__institution-text">{item.institution.name}{item.accountNumber ? ` · conta ${item.accountNumber}` : ""}</span></>}
        </span>
      </span>
      <span className="inv-link__value">
        {entry.status === "loading" ? <Skeleton width="5rem" height="1rem" />
          : amounts.length ? amounts.map((money, index) => <span className="inv-real__amount" key={index}>{formatMoney(money)}</span>)
          : item ? "Não informado" : null}
        {item && amounts.length > 0 && item.currentAmountAvailable === false && <small>Último saldo informado</small>}
      </span>
      <button type="button" className="inv-link__remove" onClick={onRemove} disabled={saving} aria-label={removeLabel}>
        <CloseIcon />
      </button>
    </li>
  );
}

function AddResource({ id, label, candidates, onAdd, saving }) {
  if (!candidates.length) return null;
  return (
    <label className="inv-link__add" htmlFor={id}>
      <span className="investimentos__field-label">{label}</span>
      <select id={id} value="" disabled={saving}
        onChange={(e) => { const item = candidates.find((candidate) => candidate.key === e.target.value); if (item) onAdd(item); }}>
        <option value="">Escolha um investimento</option>
        {candidates.map((item) => <option key={item.key} value={item.key}>{optionLabel(item)}</option>)}
      </select>
    </label>
  );
}

export function EmergencyReserveCard({ planning, items, itemsLoading, entries, summary }) {
  const { saving, error, run } = useSaving();
  const candidates = freeItems(items, planning);
  const remove = (entry) => run(async () => planning.replace(entry.legacy
    ? await removeReserveAccount()
    : await setReserveResource({ id: entry.id, reserved: entry.reserved }, false)));

  return (
    <section className="inv-plan" aria-labelledby="inv-plan-reserve">
      <h3 id="inv-plan-reserve" className="inv-plan__title">Reserva de emergência</h3>

      {entries.length > 0 ? (
        <ul className="inv-links">
          {entries.map((entry) => (
            <ResourceRow key={entry.key} entry={entry} saving={saving} onRemove={() => remove(entry)}
              removeLabel={`Tirar ${entry.item?.name ?? "investimento indisponível"} da reserva`} />
          ))}
        </ul>
      ) : itemsLoading ? (
        <Skeleton width="100%" height="3rem" />
      ) : (
        <p className="investimentos__hint">{candidates.length ? "Nenhum investimento na reserva." : "Nenhum investimento disponível para a reserva."}</p>
      )}

      {entries.length > 1 && (
        <p className="inv-links__total">
          <span>Total</span>
          <strong>{summary.loading ? <Skeleton width="6rem" height="1.25rem" />
            : summary.failed ? "Indisponível"
            : summary.totals.length ? summary.totals.map((money) => <span className="inv-real__amount" key={money.currency}>{formatMoney(money)}</span>)
            : "Não informado"}</strong>
        </p>
      )}

      <AddResource id="inv-plan-reserve-add" label="Adicionar à reserva" candidates={candidates} saving={saving}
        onAdd={(item) => run(async () => planning.replace(await setReserveResource(item, true)))} />
      {error && <p className="investimentos__error" role="alert">{error}</p>}
    </section>
  );
}

function GoalProgress({ goal, entries }) {
  const summary = summarize(entries);
  if (!entries.length) {
    return (
      <p className="inv-goal__figures">
        <span className="inv-goal__saved inv-goal__saved--empty">Nenhum investimento vinculado</span>
        <span className="inv-goal__target">Objetivo {formatCurrency(goal.targetAmount)}</span>
      </p>
    );
  }
  if (summary.loading) return <Skeleton width="60%" height="1.25rem" />;
  if (summary.failed) return <p className="inv-goal__figures"><span className="inv-goal__saved inv-goal__saved--empty">Saldo indisponível no momento</span><span className="inv-goal__target">Objetivo {formatCurrency(goal.targetAmount)}</span></p>;

  const brl = summary.totals.find((money) => money.currency === "BRL");
  const saved = brl ? Number(brl.amount) : 0;
  const progress = goal.targetAmount > 0 ? Math.min(100, (saved / goal.targetAmount) * 100) : 0;
  const remaining = Math.max(0, goal.targetAmount - saved);
  const otherCurrencies = summary.totals.some((money) => money.currency !== "BRL");
  return (
    <>
      <p className="inv-goal__figures">
        <span className="inv-goal__saved">{brl ? formatMoney(brl) : formatCurrency(0)}</span>
        <span className="inv-goal__target">de {formatCurrency(goal.targetAmount)}</span>
        <span className="inv-goal__percent">{Math.round(progress)}%</span>
      </p>
      <div className="investimentos__progress" role="progressbar" aria-label={`Progresso da meta ${goal.name}`}
        aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(progress)}>
        <div className="investimentos__progress-fill" style={{ width: `${progress}%` }} />
      </div>
      <p className="inv-goal__meta">
        {remaining > 0 ? `Faltam ${formatCurrency(remaining)}` : "Meta atingida"}
        {summary.withoutAmount > 0 && ` · ${summary.withoutAmount === 1 ? "1 investimento" : `${summary.withoutAmount} investimentos`} sem saldo informado`}
        {summary.stale && " · inclui último saldo informado"}
        {otherCurrencies && " · valores em outra moeda não entram na soma"}
      </p>
    </>
  );
}

function Goal({ goal, entries, candidates, planning }) {
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const { saving, error, run } = useSaving();

  async function handleDelete() {
    const ok = await run(async () => {
      await deleteGoal(goal.id);
      planning.updateGoals((list) => list.filter((g) => g.id !== goal.id));
    });
    if (ok) setConfirmingDelete(false);
  }

  return (
    <li className="inv-goal">
      <div className="inv-goal__header">
        <span className="inv-goal__name">{goal.name}</span>
        {goal.deadline && <span className="inv-goal__deadline">até {formatIsoDate(goal.deadline)}</span>}
        <button type="button" className="inv-link__remove inv-goal__delete" onClick={() => setConfirmingDelete(true)}
          aria-label={`Remover meta ${goal.name}`} aria-expanded={confirmingDelete}>
          <CloseIcon />
        </button>
      </div>

      <GoalProgress goal={goal} entries={entries} />

      {entries.length > 0 && (
        <ul className="inv-links inv-links--compact">
          {entries.map((entry) => (
            <ResourceRow key={entry.key} entry={entry} saving={saving}
              removeLabel={`Desvincular ${entry.item?.name ?? "investimento indisponível"} da meta ${goal.name}`}
              onRemove={() => run(async () => planning.replace(await setGoalResource(goal.id, { id: entry.id, reserved: entry.reserved }, false)))} />
          ))}
        </ul>
      )}

      <AddResource id={`inv-goal-${goal.id}-add`} label="Vincular investimento" candidates={candidates} saving={saving}
        onAdd={(item) => run(async () => planning.replace(await setGoalResource(goal.id, item, true)))} />

      {confirmingDelete && (
        <div className="investimentos__confirm" role="alert">
          <p>Remover a meta “{goal.name}”? Os investimentos vinculados não são afetados.</p>
          <div className="investimentos__actions">
            <Button as="button" type="button" variant="primary" size="sm" onClick={handleDelete} disabled={saving}>
              {saving ? "Removendo…" : "Remover"}
            </Button>
            <Button as="button" type="button" variant="outline" size="sm" onClick={() => setConfirmingDelete(false)} disabled={saving}>
              Manter
            </Button>
          </div>
        </div>
      )}
      {error && <p className="investimentos__error" role="alert">{error}</p>}
    </li>
  );
}

export function GoalsCard({ planning, items, entries }) {
  const [goalForm, setGoalForm] = useState(EMPTY_GOAL_FORM);
  const create = useSaving();
  const candidates = freeItems(items, planning);

  async function handleCreateGoal(e) {
    e.preventDefault();
    const name = goalForm.name.trim();
    const targetAmount = Number(goalForm.targetAmount);
    if (!name || !(targetAmount > 0)) {
      create.setError("Informe o nome da meta e um objetivo maior que zero.");
      return;
    }
    const ok = await create.run(async () => {
      const goal = await createGoal({ name, targetAmount, deadline: goalForm.deadline || null });
      planning.updateGoals((list) => [goal, ...list]);
    });
    if (ok) setGoalForm(EMPTY_GOAL_FORM);
  }

  return (
    <section className="inv-plan" aria-labelledby="inv-plan-goals">
      <h3 id="inv-plan-goals" className="inv-plan__title">Metas de economia</h3>
      {planning.goals.length === 0 ? (
        <p className="investimentos__hint">Nenhuma meta criada.</p>
      ) : (
        <ul className="inv-goals">
          {planning.goals.map((goal) => (
            <Goal key={goal.id} goal={goal} entries={entries[goal.id] ?? []} candidates={candidates} planning={planning} />
          ))}
        </ul>
      )}

      <form className="investimentos__form inv-plan__new" onSubmit={handleCreateGoal} aria-labelledby="inv-plan-new-goal">
        <h4 id="inv-plan-new-goal" className="inv-plan__subtitle">Nova meta</h4>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Nome</span>
          <input type="text" placeholder="Ex: Viagem de férias" maxLength={100} value={goalForm.name}
            onChange={(e) => setGoalForm({ ...goalForm, name: e.target.value })} required />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Objetivo</span>
          <CurrencyInput placeholder="Ex: 5.000,00" value={goalForm.targetAmount}
            onChange={(e) => setGoalForm({ ...goalForm, targetAmount: e.target.value })} required />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Prazo (opcional)</span>
          <DateInput value={goalForm.deadline} onChange={(deadline) => setGoalForm({ ...goalForm, deadline })} />
        </label>
        <Button as="button" type="submit" variant="primary" size="md" disabled={create.saving}>
          {create.saving ? "Salvando…" : "Criar meta"}
        </Button>
        {create.error && <p className="investimentos__error investimentos__error--form" role="alert">{create.error}</p>}
      </form>
    </section>
  );
}
