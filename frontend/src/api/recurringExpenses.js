import { apiFetch } from "./client.js";

export function getRecurringExpenses({ year, month, bankAccountId, page, search }, { signal } = {}) {
  const params = new URLSearchParams({ year: String(year), month: String(month) });
  if (bankAccountId) params.set("bankAccountId", bankAccountId);
  if (page > 1) params.set("page", String(page));
  if (search) params.set("search", search);
  return apiFetch(`/recurring-expenses?${params}`, { signal });
}

export const getRecurringExpense = (id, { signal } = {}) =>
  apiFetch(`/recurring-expenses/${encodeURIComponent(id)}`, { signal });

export const getUpcomingRecurring = (bankAccountId, { signal }) =>
  apiFetch(`/recurring-expenses/upcoming${bankAccountId ? "?" + new URLSearchParams({ bankAccountId }) : ""}`, { signal });
