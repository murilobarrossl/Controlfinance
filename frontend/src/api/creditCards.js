import { apiFetch } from "./client.js";

export const getCreditCardsSummary = async ({ signal } = {}) => apiFetch("/credit-cards/summary", { signal });
