import { useState } from "react";
import Card from "../../../components/ui/Card/Card.jsx";
import { getGoals, saveGoal, deleteGoal } from "../../../utils/investmentStorage.js";
import { calculateInstallment, formatCurrency } from "../../../utils/financeMath.js";
import "./Investimentos.css";

const EMPTY_GOAL_FORM = { name: "", targetAmount: "", currentAmount: "", deadline: "" };
const EMPTY_SIMULATION_FORM = { totalValue: "", downPayment: "0", monthlyRate: "1.5", installmentsCount: "12" };

export default function Investimentos() {
  const [goals, setGoals] = useState(() => getGoals());
  const [goalForm, setGoalForm] = useState(EMPTY_GOAL_FORM);
  const [simulationForm, setSimulationForm] = useState(EMPTY_SIMULATION_FORM);
  const [simulation, setSimulation] = useState(null);

  function handleCreateGoal(e) {
    e.preventDefault();
    if (!goalForm.name || !goalForm.targetAmount) return;

    saveGoal({
      name: goalForm.name,
      targetAmount: Number(goalForm.targetAmount),
      currentAmount: Number(goalForm.currentAmount) || 0,
      deadline: goalForm.deadline,
    });

    setGoals(getGoals());
    setGoalForm(EMPTY_GOAL_FORM);
  }

  function handleDeleteGoal(id) {
    deleteGoal(id);
    setGoals(getGoals());
  }

  function handleSimulate(e) {
    e.preventDefault();
    const totalValue = Number(simulationForm.totalValue) || 0;
    const downPayment = Number(simulationForm.downPayment) || 0;
    const monthlyRate = (Number(simulationForm.monthlyRate) || 0) / 100;
    const installmentsCount = Number(simulationForm.installmentsCount) || 0;

    const financedAmount = Math.max(totalValue - downPayment, 0);
    setSimulation(calculateInstallment(financedAmount, monthlyRate, installmentsCount));
  }

  return (
    <div className="investimentos">
      <Card title="Metas de economia">
        <form className="investimentos__form" onSubmit={handleCreateGoal}>
          <input
            type="text"
            placeholder="Nome da meta"
            value={goalForm.name}
            onChange={(e) => setGoalForm({ ...goalForm, name: e.target.value })}
            required
          />
          <input
            type="number"
            placeholder="Valor alvo"
            min="0"
            step="0.01"
            value={goalForm.targetAmount}
            onChange={(e) => setGoalForm({ ...goalForm, targetAmount: e.target.value })}
            required
          />
          <input
            type="number"
            placeholder="Valor já guardado"
            min="0"
            step="0.01"
            value={goalForm.currentAmount}
            onChange={(e) => setGoalForm({ ...goalForm, currentAmount: e.target.value })}
          />
          <input
            type="date"
            value={goalForm.deadline}
            onChange={(e) => setGoalForm({ ...goalForm, deadline: e.target.value })}
          />
          <button type="submit">Criar meta</button>
        </form>

        {goals.length === 0 ? (
          <p className="investimentos__hint">Nenhuma meta criada ainda.</p>
        ) : (
          <ul className="investimentos__goals">
            {goals.map((goal) => {
              const progress = goal.targetAmount > 0 ? Math.min(100, (goal.currentAmount / goal.targetAmount) * 100) : 0;
              return (
                <li key={goal.id} className="investimentos__goal">
                  <div className="investimentos__goal-header">
                    <span className="investimentos__goal-name">{goal.name}</span>
                    <button type="button" onClick={() => handleDeleteGoal(goal.id)} aria-label="Remover meta">
                      ×
                    </button>
                  </div>
                  <div className="investimentos__progress">
                    <div className="investimentos__progress-fill" style={{ width: `${progress}%` }} />
                  </div>
                  <span className="investimentos__goal-meta">
                    {formatCurrency(goal.currentAmount)} de {formatCurrency(goal.targetAmount)}
                    {goal.deadline && ` · até ${new Date(goal.deadline).toLocaleDateString("pt-BR")}`}
                  </span>
                </li>
              );
            })}
          </ul>
        )}
      </Card>

      <Card title="Simulador de financiamento e consórcio">
        <form className="investimentos__form" onSubmit={handleSimulate}>
          <input
            type="number"
            placeholder="Valor total"
            min="0"
            step="0.01"
            value={simulationForm.totalValue}
            onChange={(e) => setSimulationForm({ ...simulationForm, totalValue: e.target.value })}
            required
          />
          <input
            type="number"
            placeholder="Entrada"
            min="0"
            step="0.01"
            value={simulationForm.downPayment}
            onChange={(e) => setSimulationForm({ ...simulationForm, downPayment: e.target.value })}
          />
          <input
            type="number"
            placeholder="Taxa de juros mensal (%)"
            min="0"
            step="0.01"
            value={simulationForm.monthlyRate}
            onChange={(e) => setSimulationForm({ ...simulationForm, monthlyRate: e.target.value })}
          />
          <input
            type="number"
            placeholder="Número de parcelas"
            min="1"
            step="1"
            value={simulationForm.installmentsCount}
            onChange={(e) => setSimulationForm({ ...simulationForm, installmentsCount: e.target.value })}
            required
          />
          <button type="submit">Simular</button>
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

      <Card className="investimentos__ai-notice">
        <p>
          Em breve, o agente de IA da Control Finance vai acompanhar o progresso das suas metas e tirar dúvidas sobre
          financiamentos, consórcios e investimentos diretamente aqui.
        </p>
      </Card>
    </div>
  );
}
