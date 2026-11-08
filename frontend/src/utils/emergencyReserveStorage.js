import { getCurrentUserId } from "./authToken.js";

// Leitura do que ficou no navegador antes da reserva ir para o servidor (api/planning.js importa
// uma vez e só apaga daqui depois que o servidor confirma).
const KEY = "emergencyReserve";

function storageKey() {
  return `${KEY}:${getCurrentUserId() ?? "anonymous"}`;
}

export function readLegacyReserveAmount() {
  try {
    const raw = localStorage.getItem(storageKey());
    if (!raw) return null;
    const amount = Number(JSON.parse(raw)?.currentAmount);
    return Number.isFinite(amount) && amount >= 0 ? amount : null;
  } catch {
    return null;
  }
}

export function clearLegacyReserve() {
  try {
    localStorage.removeItem(storageKey());
  } catch {
    // localStorage indisponível: nada a apagar
  }
}
