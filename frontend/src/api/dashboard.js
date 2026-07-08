import { apiFetch } from "./client.js";

export const getDashboardSummary = async (bankAccountId) =>
  apiFetch(`/dashboard${bankAccountId ? `?bankAccountId=${bankAccountId}` : ""}`);
