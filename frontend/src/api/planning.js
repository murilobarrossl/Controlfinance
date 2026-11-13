import { apiFetch } from "./client.js";
import { clearLegacyReserve, readLegacyReserveAmount } from "../utils/emergencyReserveStorage.js";
import { readLegacyGoals, removeLegacyGoals } from "../utils/investmentStorage.js";

const post = (endpoint, payload, signal) =>
  apiFetch(endpoint, { method: "POST", body: JSON.stringify(payload), signal });

function sameAmount(a, b) {
  return typeof a === "number" && typeof b === "number" && Math.abs(a - b) < 0.005;
}

// Reserva e metas ficavam só no localStorage. Na primeira carga depois da migração, o que houver no
// navegador vai para a conta (o servidor nunca sobrescreve o que já tem) e só então sai daqui.
// legacyReserveAmount volta preenchido quando este navegador tem uma reserva diferente da já salva
// na conta: nada é descartado sem o usuário decidir.
export async function loadPlanning({ signal } = {}) {
  const legacyReserve = readLegacyReserveAmount();
  const legacyGoals = readLegacyGoals().filter((goal) => goal.name.trim() && goal.targetAmount > 0);

  if (legacyReserve == null && legacyGoals.length === 0) {
    return { planning: await apiFetch("/planning", { signal }), legacyReserveAmount: null };
  }

  const result = await post("/planning/import", { reserveAmount: legacyReserve, goals: legacyGoals }, signal);
  removeLegacyGoals(result.storedGoalIds ?? []);

  let legacyReserveAmount = null;
  if (legacyReserve != null) {
    const accountAmount = result.planning.reserve?.currentAmount;
    if (result.reserveImported || legacyReserve === 0 || sameAmount(accountAmount, legacyReserve)) clearLegacyReserve();
    else legacyReserveAmount = legacyReserve;
  }

  return { planning: result.planning, legacyReserveAmount };
}

export const discardLegacyReserve = () => clearLegacyReserve();

export const hasLegacyPlanning = () => readLegacyReserveAmount() != null || readLegacyGoals().length > 0;

export const addToReserve = (amount) => post("/planning/reserve/add", { amount });

export const withdrawFromReserve = (amount) => post("/planning/reserve/withdraw", { amount });

export const setReserve = (amount) =>
  apiFetch("/planning/reserve", { method: "PUT", body: JSON.stringify({ amount }) });

export const createGoal = (payload) => post("/planning/goals", payload);

export const updateGoalProgress = (id, currentAmount) =>
  apiFetch(`/planning/goals/${id}/progress`, { method: "PUT", body: JSON.stringify({ currentAmount }) });

export const deleteGoal = (id) => apiFetch(`/planning/goals/${id}`, { method: "DELETE" });

export const setReserveAccount = (bankAccountId) =>
  apiFetch("/planning/reserve/account", { method: "PUT", body: JSON.stringify({ bankAccountId }) });

export const setInvestmentRate = (accountId, { kind, value, incomeTaxExempt }) =>
  apiFetch(`/planning/investments/${accountId}/rate`, {
    method: "PUT",
    body: JSON.stringify({ kind, value, incomeTaxExempt }),
  });

export const deleteInvestmentRate = (accountId) =>
  apiFetch(`/planning/investments/${accountId}/rate`, { method: "DELETE" });

export const getReferenceRates = ({ signal } = {}) => apiFetch("/market/reference-rates", { signal });
