import { useMemo, useState } from "react";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import { PiggyBankIcon, BoxIcon, CoinsIcon, ChartIcon, CloseIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import { useAuth } from "../../../context/AuthContext.jsx";
import {
  addToReserve,
  createGoal,
  deleteGoal,
  discardLegacyReserve,
  setReserve as setReserveAmount,
  updateGoalProgress,
  withdrawFromReserve,
} from "../../../api/planning.js";
import usePlanning from "./usePlanning.js";
import { INVESTMENT_MODALITIES, getInvestmentModality } from "../../../utils/accountKind.js";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { REFERENCE_CDI_PCT, REFERENCE_DATE_LABEL } from "../../../utils/investmentProducts.js";
import BankYieldCards from "./BankYieldCards.jsx";
import YieldSimulator from "./YieldSimulator.jsx";
import FinancingSimulator from "./FinancingSimulator.jsx";
import "./Investimentos.css";
import usePageTitle from "../../../hooks/usePageTitle.js";

const EMPTY_GOAL_FORM = { name: "", targetAmount: "", currentAmount: "", deadline: "" };

const MODALITY_ICONS = {
  caixinha: BoxIcon,
  porquinho: PiggyBankIcon,
  poupanca: CoinsIcon,
  investimento: ChartIcon,
};

// O <input type="date"> grava "AAAA-MM-DD". new Date() lê isso como meia-noite UTC, o que no
// fuso do Brasil vira o dia anterior: formata direto da string pra o prazo não "voltar" um dia.
function formatIsoDate(iso) {
  const [year, month, day] = iso.split("-");
  return `${day}/${month}/${year}`;
}

function normalizeColor(raw) {
  if (!raw) return undefined;
  return raw.startsWith("#") ? raw : `#${raw}`;
}

// Agrupa só contas que já existem (sincronizadas da Polp) e que o tipo/nome identifica como
// reserva ou investimento, por instituição. Nenhum saldo ou rendimento é calculado aqui: o valor
// mostrado é o saldo que o próprio banco mandou pra essa conta.
function groupInvestmentsByInstitution(accounts, connectors) {
  const connectorsById = new Map(connectors.map((c) => [c.id, c]));
  const institutions = new Map();

  for (const account of accounts) {
    const modality = getInvestmentModality(account);
    if (!modality) continue;

    const connector = connectorsById.get(account.bankCode);
    const key = account.bankCode ?? "unknown";
    if (!institutions.has(key)) {
      institutions.set(key, {
        key,
        name: connector?.name ?? "Instituição não identificada",
        logoUrl: connector?.logoUrl,
        color: normalizeColor(connector?.color),
        items: [],
        total: 0,
      });
    }
    const institution = institutions.get(key);
    institution.items.push({ account, modality });
    institution.total = institution.total === null || account.balance == null ? null : institution.total + account.balance;
  }

  return [...institutions.values()].sort((a, b) => (b.total ?? 0) - (a.total ?? 0));
}

function ModalityTag({ modality }) {
  const Icon = MODALITY_ICONS[modality.key] ?? ChartIcon;
  return (
    <span
      className="investimentos__modality"
      style={{ "--modality-color": modality.color, "--modality-text": modality.textColor ?? modality.color }}
    >
      <Icon />
      {modality.label}
    </span>
  );
}

function formatSyncedAt(value) {
  return new Date(value).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });
}

function syncSummary(institutions) {
  const dates = institutions.flatMap((i) => i.items.map(({ account }) => account.lastSyncedAt));
  if (dates.some((d) => !d)) return "Parte das contas ainda aguarda a primeira sincronização com o banco.";
  const times = dates.map((d) => new Date(d).getTime()).sort((a, b) => a - b);
  const oldest = formatSyncedAt(times[0]);
  const newest = formatSyncedAt(times[times.length - 1]);
  return oldest === newest
    ? `Saldos informados pelos bancos, atualizados em ${oldest}.`
    : `Saldos informados pelos bancos, atualizados entre ${oldest} e ${newest}.`;
}

function compositionByModality(institutions) {
  const byKey = new Map();
  for (const { items } of institutions) {
    for (const { account, modality } of items) {
      const entry = byKey.get(modality.key) ?? { modality, total: 0 };
      entry.total = entry.total === null || account.balance == null ? null : entry.total + account.balance;
      byKey.set(modality.key, entry);
    }
  }
  return [...byKey.values()].sort((a, b) => (b.total ?? 0) - (a.total ?? 0));
}

function formatShare(share) {
  return share > 0 && share < 1 ? "menos de 1%" : formatPercentage(Math.round(share));
}

function Composition({ entries, total }) {
  const shares = entries.map((e) => ({ ...e, share: (e.total / total) * 100 }));
  const description = shares.map((e) => `${e.modality.label} ${formatShare(e.share)}`).join(", ");

  return (
    <div className="investimentos__composition">
      <div className="investimentos__composition-bar" role="img" aria-label={`Composição por modalidade: ${description}`}>
        {shares.map((e) => (
          <span key={e.modality.key} style={{ flexGrow: e.total, backgroundColor: e.modality.color }} />
        ))}
      </div>
      <ul className="investimentos__composition-list">
        {shares.map((e) => (
          <li key={e.modality.key}>
            <ModalityTag modality={e.modality} />
            <span className="investimentos__composition-value">{formatCurrency(e.total)}</span>
            <span className="investimentos__composition-share">{formatShare(e.share)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function PositionSection({ institutions, status, usedModalityKeys, onRetry }) {
  const grandTotal = institutions.some((i) => i.total === null) ? null : institutions.reduce((sum, i) => sum + i.total, 0);
  const composition = compositionByModality(institutions);
  const showComposition =
    grandTotal > 0 && composition.length > 1 && composition.every((e) => e.total !== null && e.total >= 0);
  const count = institutions.length;

  return (
    <section className="investimentos__position" aria-labelledby="investimentos-position-title">
      <h2 id="investimentos-position-title" className="investimentos__group-title">
        Suas reservas e investimentos
      </h2>

      {status === "loading" ? (
        <Skeleton width="100%" height="10rem" />
      ) : status === "error" ? (
        <div className="investimentos__failure" role="alert">
          <p className="investimentos__hint">Não foi possível carregar as contas.</p>
          <Button as="button" type="button" variant="outline" size="sm" onClick={onRetry}>
            Tentar de novo
          </Button>
        </div>
      ) : count === 0 ? (
        <p className="investimentos__hint">
          Nenhuma conta de reserva ou investimento foi reconhecida nos bancos conectados.
        </p>
      ) : (
        <>
          <div className="investimentos__summary">
            <div className="investimentos__summary-main">
              <p className="investimentos__summary-total">
                {grandTotal === null ? (
                  <>Algum banco não informou o saldo de uma das contas, então o total não pode ser somado.</>
                ) : (
                  <>
                    <strong>{formatCurrency(grandTotal)}</strong> guardados em {count}{" "}
                    {count === 1 ? "instituição" : "instituições"}
                  </>
                )}
              </p>
              <p className="investimentos__summary-sync">{syncSummary(institutions)}</p>
            </div>
            {showComposition && <Composition entries={composition} total={grandTotal} />}
          </div>

          <div className="investimentos__institutions">
            {institutions.map((institution) => (
              <section key={institution.key} className="investimentos__institution">
                <header className="investimentos__institution-header">
                  <BankLogo name={institution.name} logoUrl={institution.logoUrl} color={institution.color} />
                  <h3 className="investimentos__institution-name">{institution.name}</h3>
                  <span className="investimentos__institution-total">{formatCurrency(institution.total)}</span>
                </header>
                <ul className="investimentos__institution-items">
                  {institution.items.map(({ account, modality }) => (
                    <li key={account.id} className="investimentos__institution-item">
                      <ModalityTag modality={modality} />
                      <span className="investimentos__institution-item-name" title={account.name}>
                        {account.name}
                      </span>
                      <span className="investimentos__institution-item-value">{formatCurrency(account.balance)}</span>
                    </li>
                  ))}
                </ul>
              </section>
            ))}
          </div>
        </>
      )}

      {count > 0 && (
        <p className="investimentos__fineprint">A modalidade é deduzida do nome e do tipo da conta e pode não ser exata.</p>
      )}

      <ModalitiesGuide usedKeys={usedModalityKeys} />
    </section>
  );
}

function ModalitiesGuide({ usedKeys }) {
  const modalities = Object.values(INVESTMENT_MODALITIES)
    .map((m) => ({ ...m, Icon: MODALITY_ICONS[m.key] }))
    .sort((a, b) => Number(usedKeys.has(b.key)) - Number(usedKeys.has(a.key)));

  return (
    <details className="investimentos__explain investimentos__guide-details">
      <summary>Entenda cada modalidade</summary>
      <ul className="investimentos__guide">
        {modalities.map(({ key, label, color, Icon, description }) => (
          <li key={key} className="investimentos__guide-item" style={{ "--modality-color": color }}>
            <span className="investimentos__guide-icon" aria-hidden="true">
              <Icon />
            </span>
            <div>
              <span className="investimentos__guide-title">{label}</span>
              <p>{description}</p>
            </div>
          </li>
        ))}
      </ul>
    </details>
  );
}

function parseAmount(raw) {
  const value = Number(raw);
  return Number.isFinite(value) ? value : NaN;
}

function useSaving() {
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  async function run(action) {
    setSaving(true);
    setError(null);
    try {
      await action();
      return true;
    } catch (err) {
      setError(err?.message || "Não foi possível salvar. Tente de novo.");
      return false;
    } finally {
      setSaving(false);
    }
  }

  return { saving, error, setError, run };
}

function EmergencyReserveSection({ planning }) {
  const { reserve, legacyReserveAmount, setReserve, clearLegacyReserveNotice } = planning;
  const currentAmount = reserve?.currentAmount ?? 0;
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
      <p className="investimentos__section-hint">
        Também usado na simulação de corte de gastos de Categorias.
      </p>
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
    </Card>
  );
}

function GoalsSection({ planning }) {
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

function PlanningSection() {
  const planning = usePlanning();

  if (planning.status === "loading") {
    return (
      <div className="investimentos__grid" aria-busy="true">
        <Skeleton width="100%" height="12rem" />
        <Skeleton width="100%" height="12rem" />
      </div>
    );
  }

  if (planning.status === "error") {
    return (
      <div className="investimentos__failure" role="alert">
        <p className="investimentos__hint">Não foi possível carregar a reserva e as metas.</p>
        <Button as="button" type="button" variant="outline" size="sm" onClick={planning.retry}>
          Tentar de novo
        </Button>
      </div>
    );
  }

  return (
    <div className="investimentos__grid">
      <EmergencyReserveSection planning={planning} />
      <GoalsSection planning={planning} />
    </div>
  );
}

// Cada seção guarda o próprio estado: digitar no simulador ou numa meta não re-renderiza a lista
// de instituições (nem refaz o agrupamento), como acontecia com tudo num componente só.
export default function Investimentos() {
  usePageTitle("Investimentos");
  const { accounts, connectors, accountsStatus, bumpDataRefresh } = useAccount();
  const isCompany = useAuth()?.user?.documentType === "CNPJ";
  const [cdi, setCdi] = useState(String(REFERENCE_CDI_PCT));
  const institutions = useMemo(() => groupInvestmentsByInstitution(accounts, connectors), [accounts, connectors]);
  const usedModalityKeys = useMemo(
    () => new Set(institutions.flatMap((institution) => institution.items.map((item) => item.modality.key))),
    [institutions],
  );

  return (
    <div className="investimentos">
      <SectionHeading title="Investimentos" level={1} />

      <PositionSection
        institutions={institutions}
        status={accountsStatus}
        usedModalityKeys={usedModalityKeys}
        onRetry={bumpDataRefresh}
      />

      <section className="investimentos__group" aria-labelledby="investimentos-yield-title">
        <header className="investimentos__group-header">
          <h2 id="investimentos-yield-title" className="investimentos__group-title">
            Rendimento estimado
          </h2>
          <p className="investimentos__group-lead">
            Projeções com taxas de referência de {REFERENCE_DATE_LABEL}, não o rendimento real das suas contas.
          </p>
        </header>
        <BankYieldCards accounts={accounts} connectors={connectors} cdi={cdi} onCdiChange={setCdi} isCompany={isCompany} />
        <YieldSimulator cdi={cdi} onCdiChange={setCdi} isCompany={isCompany} />
      </section>

      <section className="investimentos__group" aria-labelledby="investimentos-plan-title">
        <header className="investimentos__group-header">
          <h2 id="investimentos-plan-title" className="investimentos__group-title">
            Seu planejamento
          </h2>
          <p className="investimentos__group-lead">Anotados por você, sem ligação com os bancos, e salvos na sua conta.</p>
        </header>
        <PlanningSection />
      </section>

      <FinancingSimulator />
    </div>
  );
}
