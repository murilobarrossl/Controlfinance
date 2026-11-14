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
