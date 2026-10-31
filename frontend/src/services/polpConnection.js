import { getIntegrationStatus } from "../api/polp.js";

// Mesmo nome usado por clearLocalDataFor (utils/authToken.js).
const PENDING_KEY = "pendingPolpIntegrationId";
// A Polp sincroniza contas/transações de forma assíncrona depois da autenticação -
// isso pode levar bem mais que alguns segundos, então damos uma folga generosa (5min).
const POLL_MAX_ATTEMPTS = 100;
const POLL_INTERVAL_MS = 3000;

export function wait(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function getPendingIntegrationId() {
  return localStorage.getItem(PENDING_KEY);
}

export function setPendingIntegrationId(integrationId) {
  localStorage.setItem(PENDING_KEY, integrationId);
}

export function clearPendingIntegrationId() {
  localStorage.removeItem(PENDING_KEY);
}

// Acompanha o status da conexão até ficar ativa, dar erro/rejeição ou esgotar as tentativas.
// Retorna "active" | "login_error" | "timeout". O backend dispara a carga inicial dos dados sozinho
// quando o consentimento é autorizado ("syncing" até ela terminar), então aqui não há nada a pedir.
export async function watchPolpConnection(integrationId, { isCancelled = () => false } = {}) {
  for (let attempt = 0; attempt < POLL_MAX_ATTEMPTS && !isCancelled(); attempt++) {
    try {
      const status = await getIntegrationStatus(integrationId);

      if (status?.status === "active") {
        clearPendingIntegrationId();
        return "active";
      }

      if (status?.status === "login_error") {
        clearPendingIntegrationId();
        return "login_error";
      }
    } catch {
      // ignora falhas pontuais de rede e tenta de novo até esgotar as tentativas
    }

    await wait(POLL_INTERVAL_MS);
  }

  clearPendingIntegrationId();
  return "timeout";
}
