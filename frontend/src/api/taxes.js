import { apiFetch } from "./client.js";

export const getTaxes = async ({ year, month, bankAccountId }, { signal } = {}) => {
  const params = new URLSearchParams({ year: String(year), month: String(month) });
  if (bankAccountId) params.set("bankAccountId", bankAccountId);
  return apiFetch(`/taxes?${params}`, { signal });
};
