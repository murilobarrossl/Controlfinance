import { apiFetch } from "./client.js";

export const getDashboardSummary = async (bankAccountId, { signal } = {}) =>
  apiFetch(`/dashboard${bankAccountId ? `?bankAccountId=${bankAccountId}` : ""}`, { signal });
