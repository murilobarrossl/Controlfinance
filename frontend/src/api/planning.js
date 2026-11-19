import { apiFetch } from "./client.js";
import { clearLegacyReserve, readLegacyReserveAmount } from "../utils/emergencyReserveStorage.js";
import { readLegacyGoals, removeLegacyGoals } from "../utils/investmentStorage.js";

const post = (endpoint, payload, signal) =>
  apiFetch(endpoint, { method: "POST", body: JSON.stringify(payload), signal });

// Reserva e metas ficavam só no localStorage. Na primeira carga depois da migração, as metas do
// navegador vão para a conta (o servidor nunca sobrescreve o que já tem) e só então saem daqui. O
// valor anotado da reserva e o "já guardado" das metas não vão: o planejamento mostra só saldos
// informados pelos bancos, então esses números digitados são descartados.
export async function loadPlanning({ signal } = {}) {
  if (readLegacyReserveAmount() != null) clearLegacyReserve();
  const legacyGoals = readLegacyGoals().filter((goal) => goal.name.trim() && goal.targetAmount > 0)
    .map(({ id, name, targetAmount, deadline, createdAt }) => ({ id, name, targetAmount, deadline, createdAt }));

  if (legacyGoals.length === 0) return apiFetch("/planning", { signal });

  const result = await post("/planning/import", { goals: legacyGoals }, signal);
  removeLegacyGoals(result.storedGoalIds ?? []);
  return result.planning;
}

export const createGoal = ({ name, targetAmount, deadline }) => post("/planning/goals", { name, targetAmount, deadline });

export const deleteGoal = (id) => apiFetch(`/planning/goals/${id}`, { method: "DELETE" });

const allocation = (item, enabled) => JSON.stringify({ investmentId: item.reserved ? null : item.id, reservedBalanceId: item.reserved ? item.id : null, enabled });

export const setGoalResource = (goalId, item, enabled) =>
  apiFetch(`/planning/goals/${goalId}/allocations`, { method: "PUT", body: allocation(item, enabled) });

export const setInvestmentRate = (accountId, { kind, value, incomeTaxExempt }) =>
  apiFetch(`/planning/investments/${accountId}/rate`, {
    method: "PUT",
    body: JSON.stringify({ kind, value, incomeTaxExempt }),
  });

export const deleteInvestmentRate = (accountId) =>
  apiFetch(`/planning/investments/${accountId}/rate`, { method: "DELETE" });

export const getReferenceRates = ({ signal } = {}) => apiFetch("/market/reference-rates", { signal });
