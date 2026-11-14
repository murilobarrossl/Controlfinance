import { apiFetch } from "./client.js";

function page(path, cursor, signal, integrationId) {
  const params = new URLSearchParams();
  if (cursor) params.set("cursor", cursor);
  if (integrationId) params.set("integrationId", integrationId);
  if (!path.endsWith("connections")) params.set("limit", "100");
  return apiFetch(`${path}?${params}`, { signal });
}
export const investmentPage = (kind, cursor, signal, integrationId) => page(
  kind === "investments" ? "/investments" : `/investments/${kind}`, cursor, signal, integrationId);
export const investmentDetail = (id, signal, reserved = false) => apiFetch(`/investments/${reserved ? "reserved-balances/" : ""}${encodeURIComponent(id)}`, { signal });
export const investmentHistory = (id, reserved, cursor, signal) => page(
  `/investments/${reserved ? "reserved-balances/" : ""}${encodeURIComponent(id)}/positions`, cursor, signal);
export const investmentMovements = (id, cursor, signal) => page(`/investments/${encodeURIComponent(id)}/movements`, cursor, signal);
export const investmentFinancials = (id, month, signal) => apiFetch(
  `/investments/${encodeURIComponent(id)}/financials?${new URLSearchParams({ month })}`, { signal });
// 429 (limite do usuário) e 503 (excesso geral de acessos) não dizem nada sobre os dados: quem
// já tem dados na tela continua com eles.
export const isThrottled = (error) => error?.status === 429 || error?.status === 503;
// O botão "Atualizar dados" pede licença ao servidor antes de reler (intervalo de 30s por usuário).
export const requestInvestmentsRefresh = () => apiFetch("/investments/refresh-requests", { method: "POST" });
