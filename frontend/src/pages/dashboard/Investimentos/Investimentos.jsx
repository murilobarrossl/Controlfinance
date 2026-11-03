import { useMemo, useState } from "react";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import CurrencyInput from "../../../components/ui/CurrencyInput/CurrencyInput.jsx";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import {
  PiggyBankIcon,
  BoxIcon,
  CoinsIcon,
  ChartIcon,
  ShieldIcon,
  TargetIcon,
} from "../../../components/ui/icons/FeatureIcons.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import { getGoals, saveGoal, deleteGoal, updateGoalProgress } from "../../../utils/investmentStorage.js";
import { getReserve, addToReserve, removeFromReserve, clearReserve } from "../../../utils/emergencyReserveStorage.js";
import { INVESTMENT_MODALITIES, getInvestmentModality } from "../../../utils/accountKind.js";
import { calculateInstallment, formatCurrency } from "../../../utils/financeMath.js";
import "./Investimentos.css";

const EMPTY_GOAL_FORM = { name: "", targetAmount: "", currentAmount: "", deadline: "" };
const EMPTY_SIMULATION_FORM = { totalValue: "", downPayment: "0", monthlyRate: "1.5", installmentsCount: "12" };

const MODALITY_ICONS = {
  caixinha: BoxIcon,
  porquinho: PiggyBankIcon,
  poupanca: CoinsIcon,
  investimento: ChartIcon,
};

// Modalidades que o próprio usuário registra aqui (guardadas só neste navegador), explicadas na
// mesma legenda das que vêm do banco, mas sempre marcadas como "registrado por você".
const MANUAL_MODALITIES = [
  {
    key: "reserva",
    label: "Reserva de emergência",
    color: "#3FA9A1",
    Icon: ShieldIcon,
    description:
      "Dinheiro guardado pra imprevistos (saúde, conserto, perda de renda). O ideal é cobrir de 3 a 6 meses de despesas e poder resgatar na hora.",
  },
  {
    key: "meta",
    label: "Meta de economia",
    color: "#5B86B5",
    Icon: TargetIcon,
    description: "Um objetivo com valor e prazo (viagem, entrada de um imóvel). Você acompanha quanto falta pela barra de progresso.",
  },
];

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
    const key = account.bankCode ?? `account-${account.id}`;
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
    institution.total += account.balance ?? 0;
  }

  return [...institutions.values()].sort((a, b) => b.total - a.total);
}

function ModalityTag({ modality }) {
  const Icon = MODALITY_ICONS[modality.key] ?? ChartIcon;
  return (
    <span className="investimentos__modality" style={{ "--modality-color": modality.color }}>
      <Icon />
      {modality.label}
    </span>
  );
}

function InstitutionsSection({ institutions }) {
  const grandTotal = institutions.reduce((sum, i) => sum + i.total, 0);

  return (
    <Card title="Suas reservas e investimentos por instituição">
      <p className="investimentos__section-hint">
        Contas de caixinha, porquinho, poupança ou investimento que reconhecemos nos bancos que você conectou, com o
        saldo informado pelo próprio banco na última sincronização. A modalidade é identificada automaticamente pelo
        nome e pelo tipo da conta e pode não ser exata. Nada aqui é recomendação de investimento.
      </p>

      {institutions.length === 0 ? (
        <p className="investimentos__hint">
          Nenhuma conta de reserva ou investimento foi reconhecida nos seus bancos conectados até agora. Quando a
          sincronização trouxer uma, ela aparece aqui, agrupada pelo banco.
        </p>
      ) : (
        <>
          <p className="investimentos__grand-total">
            Total reconhecido: <strong>{formatCurrency(grandTotal)}</strong>
          </p>
          <div className="investimentos__institutions">
            {institutions.map((institution) => (
              <section
                key={institution.key}
                className="investimentos__institution"
                style={{ "--institution-color": institution.color ?? "var(--color-border-strong)" }}
              >
                <header className="investimentos__institution-header">
                  <BankLogo name={institution.name} logoUrl={institution.logoUrl} color={institution.color} />
                  <span className="investimentos__institution-name">{institution.name}</span>
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
    </Card>
  );
}

function ModalitiesGuide() {
  const fromBank = Object.values(INVESTMENT_MODALITIES).map((m) => ({ ...m, Icon: MODALITY_ICONS[m.key], source: "Vem do banco" }));
  const manual = MANUAL_MODALITIES.map((m) => ({ ...m, source: "Registrado por você" }));

  return (
    <Card title="Entenda cada modalidade">
      <ul className="investimentos__guide">
        {[...fromBank, ...manual].map(({ key, label, color, Icon, description, source }) => (
          <li key={key} className="investimentos__guide-item" style={{ "--modality-color": color }}>
            <span className="investimentos__guide-icon" aria-hidden="true">
              <Icon />
            </span>
            <div>
              <span className="investimentos__guide-title">
                {label}
                <span className="investimentos__guide-source">{source}</span>
              </span>
              <p>{description}</p>
            </div>
          </li>
        ))}
      </ul>
    </Card>
  );
}

function EmergencyReserveSection() {
  const [reserve, setReserve] = useState(() => getReserve());
  const [panel, setPanel] = useState(null); // null | "add" | "remove"
  const [amountInput, setAmountInput] = useState("");

  function closePanel() {
    setPanel(null);
    setAmountInput("");
  }

  function togglePanel(next) {
    setAmountInput("");
    setPanel((prev) => (prev === next ? null : next));
  }

  function handleSubmit(e) {
    e.preventDefault();
    if (!amountInput) return;
    const amount = Math.max(0, Number(amountInput));
    if (panel === "add") addToReserve(amount);
    else removeFromReserve(amount);
    setReserve(getReserve());
    closePanel();
  }

  function handleClear() {
    clearReserve();
    setReserve(getReserve());
    closePanel();
  }

  return (
    <Card title="Reserva de emergência">
      <p className="investimentos__section-hint">
        Registre aqui quanto você já separou pra imprevistos. Esse valor é anotado por você e fica salvo só neste
        navegador; ele também é usado na simulação de corte de gastos da aba Categorias.
      </p>
      <p className="investimentos__reserve-value">{formatCurrency(reserve.currentAmount)}</p>

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
            <CurrencyInput placeholder="Ex: 300" value={amountInput} onChange={(e) => setAmountInput(e.target.value)} />
          </label>
          <div className="investimentos__actions">
            <Button as="button" type="submit" variant={panel === "add" ? "primary" : "secondary"} size="sm">
              {panel === "add" ? "Adicionar" : "Retirar"}
            </Button>
            {panel === "remove" && (
              <Button as="button" type="button" variant="outline" size="sm" onClick={handleClear}>
                Zerar reserva
              </Button>
            )}
            <Button as="button" type="button" variant="outline" size="sm" onClick={closePanel}>
              Cancelar
            </Button>
          </div>
        </form>
      )}
    </Card>
  );
}

function GoalsSection() {
  const [goals, setGoals] = useState(() => getGoals());
  const [goalForm, setGoalForm] = useState(EMPTY_GOAL_FORM);
  const [editingGoalId, setEditingGoalId] = useState(null);
  const [progressInput, setProgressInput] = useState("");

  function handleCreateGoal(e) {
    e.preventDefault();
    if (!goalForm.name || !goalForm.targetAmount) return;

    saveGoal({
      name: goalForm.name,
      // O input aceita "-" digitado direto (o atributo min="0" do HTML só valida visualmente,
      // não impede o valor de chegar aqui), então trava em zero na hora de gravar.
      targetAmount: Math.max(0, Number(goalForm.targetAmount)),
      currentAmount: Math.max(0, Number(goalForm.currentAmount) || 0),
      deadline: goalForm.deadline,
    });

    setGoals(getGoals());
    setGoalForm(EMPTY_GOAL_FORM);
  }

  function handleDeleteGoal(id) {
    deleteGoal(id);
    setGoals(getGoals());
  }

  function startProgressEdit(goal) {
    setEditingGoalId(goal.id);
    setProgressInput(String(goal.currentAmount));
  }

  function handleSaveProgress(e, id) {
    e.preventDefault();
    updateGoalProgress(id, Math.max(0, Number(progressInput) || 0));
    setGoals(getGoals());
    setEditingGoalId(null);
  }

  return (
    <Card title="Metas de economia">
      <p className="investimentos__section-hint">
        Defina quanto quer juntar, quanto já tem guardado e o prazo (se houver). Depois é só atualizar o valor guardado
        e acompanhar a barra de progresso.
      </p>
      <form className="investimentos__form" onSubmit={handleCreateGoal}>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Nome da meta</span>
          <input
            type="text"
            placeholder="Ex: Viagem pra praia"
            value={goalForm.name}
            onChange={(e) => setGoalForm({ ...goalForm, name: e.target.value })}
            required
          />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Valor alvo</span>
          <CurrencyInput
            placeholder="Ex: 5000"
            value={goalForm.targetAmount}
            onChange={(e) => setGoalForm({ ...goalForm, targetAmount: e.target.value })}
            required
          />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Já guardado (opcional)</span>
          <CurrencyInput
            placeholder="Ex: 1200"
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
        <Button as="button" type="submit" variant="primary" size="md">
          Criar meta
        </Button>
      </form>

      {goals.length === 0 ? (
        <p className="investimentos__hint">Nenhuma meta criada ainda.</p>
      ) : (
        <ul className="investimentos__goals">
          {goals.map((goal) => {
            const progress = goal.targetAmount > 0 ? Math.min(100, (goal.currentAmount / goal.targetAmount) * 100) : 0;
            const remaining = Math.max(0, goal.targetAmount - goal.currentAmount);
            return (
              <li key={goal.id} className="investimentos__goal">
                <div className="investimentos__goal-header">
                  <span className="investimentos__goal-name">{goal.name}</span>
                  <span className="investimentos__goal-percent">{Math.round(progress)}%</span>
                  <button type="button" onClick={() => handleDeleteGoal(goal.id)} aria-label="Remover meta">
                    ×
                  </button>
                </div>
                <div className="investimentos__progress">
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
                {editingGoalId === goal.id && (
                  <form className="investimentos__form investimentos__form--inline" onSubmit={(e) => handleSaveProgress(e, goal.id)}>
                    <label className="investimentos__field">
                      <span className="investimentos__field-label">Valor guardado</span>
                      <CurrencyInput value={progressInput} onChange={(e) => setProgressInput(e.target.value)} />
                    </label>
                    <div className="investimentos__actions">
                      <Button as="button" type="submit" variant="primary" size="sm">
                        Salvar
                      </Button>
                      <Button as="button" type="button" variant="outline" size="sm" onClick={() => setEditingGoalId(null)}>
                        Cancelar
                      </Button>
                    </div>
                  </form>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}

function FinancingSimulator() {
  const [simulationForm, setSimulationForm] = useState(EMPTY_SIMULATION_FORM);
  const [simulation, setSimulation] = useState(null);

  function handleSimulate(e) {
    e.preventDefault();
    const totalValue = Math.max(0, Number(simulationForm.totalValue) || 0);
    const downPayment = Math.max(0, Number(simulationForm.downPayment) || 0);
    const monthlyRate = Math.max(0, (Number(simulationForm.monthlyRate) || 0) / 100);
    const installmentsCount = Math.max(0, Number(simulationForm.installmentsCount) || 0);

    const financedAmount = Math.max(totalValue - downPayment, 0);
    setSimulation(calculateInstallment(financedAmount, monthlyRate, installmentsCount));
  }

  return (
    <Card title="Simulador de financiamento e consórcio">
      <p className="investimentos__section-hint">
        Preencha os dados da proposta que você recebeu (do banco, loja ou administradora de consórcio) pra ver o valor da
        parcela e quanto vai pagar de juros no total, antes de fechar. É uma estimativa pela Tabela Price: não inclui
        IOF, tarifas, seguros nem outros encargos, então o custo efetivo total (CET) informado pela instituição pode ser
        maior.
      </p>
      <form className="investimentos__form" onSubmit={handleSimulate}>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Valor total</span>
          <CurrencyInput
            placeholder="Ex: 30000"
            value={simulationForm.totalValue}
            onChange={(e) => setSimulationForm({ ...simulationForm, totalValue: e.target.value })}
            required
          />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Entrada (opcional)</span>
          <CurrencyInput
            placeholder="Ex: 5000"
            value={simulationForm.downPayment}
            onChange={(e) => setSimulationForm({ ...simulationForm, downPayment: e.target.value })}
          />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Juros ao mês (%)</span>
          <input
            type="number"
            placeholder="Ex: 1.5 (=1,5% ao mês)"
            min="0"
            step="0.01"
            value={simulationForm.monthlyRate}
            onChange={(e) => setSimulationForm({ ...simulationForm, monthlyRate: e.target.value })}
          />
        </label>
        <label className="investimentos__field">
          <span className="investimentos__field-label">Número de parcelas</span>
          <input
            type="number"
            placeholder="Ex: 12"
            min="1"
            step="1"
            value={simulationForm.installmentsCount}
            onChange={(e) => setSimulationForm({ ...simulationForm, installmentsCount: e.target.value })}
            required
          />
        </label>
        <Button as="button" type="submit" variant="primary" size="md">
          Simular
        </Button>
      </form>

      {simulation && (
        <div className="investimentos__simulation">
          <p>
            Parcela mensal: <strong>{formatCurrency(simulation.installmentAmount)}</strong>
          </p>
          <p>
            Total pago: <strong>{formatCurrency(simulation.totalPaid)}</strong>
          </p>
          <p>
            Total de juros: <strong>{formatCurrency(simulation.totalInterest)}</strong>
          </p>
        </div>
      )}
    </Card>
  );
}

// Cada seção guarda o próprio estado: digitar no simulador ou numa meta não re-renderiza a lista
// de instituições (nem refaz o agrupamento), como acontecia com tudo num componente só.
export default function Investimentos() {
  const { accounts, connectors } = useAccount();
  const institutions = useMemo(() => groupInvestmentsByInstitution(accounts, connectors), [accounts, connectors]);

  return (
    <div className="investimentos">
      <SectionHeading kicker="Planeje o futuro" title="Investimentos" align="left" />

      <InstitutionsSection institutions={institutions} />
      <ModalitiesGuide />

      <div className="investimentos__grid">
        <EmergencyReserveSection />
        <GoalsSection />
      </div>

      <FinancingSimulator />
    </div>
  );
}
