import { apiFetch } from "./client.js";

export const getCashDay = async ({ date, bankAccountId }, { signal } = {}) => {
  const params = new URLSearchParams({ date });
  if (bankAccountId) params.set("bankAccountId", bankAccountId);
  return apiFetch(`/cash-flow/closings/day?${params}`, { signal });
};

export const closeCashDay = async ({ date, bankAccountId, expectedIncome, expectedExpense, expectedCount }) =>
  apiFetch("/cash-flow/closings", {
    method: "POST",
    body: JSON.stringify({ date, bankAccountId: bankAccountId || null, expectedIncome, expectedExpense, expectedCount }),
  });

export const reopenCashDay = async (closingId, version) =>
  apiFetch(`/cash-flow/closings/${encodeURIComponent(closingId)}/reopen`, {
    method: "POST",
    body: JSON.stringify({ version }),
  });

export const getAdjustedCashDays = async ({ bankAccountId } = {}, { signal } = {}) => {
  const params = new URLSearchParams();
  if (bankAccountId) params.set("bankAccountId", bankAccountId);
  const query = params.toString();
  return apiFetch(`/cash-flow/closings/adjusted${query ? `?${query}` : ""}`, { signal });
};
