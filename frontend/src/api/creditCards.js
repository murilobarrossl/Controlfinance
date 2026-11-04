import { apiFetch } from "./client.js";

export const getCreditCardsSummary = async ({ signal } = {}) => apiFetch("/credit-cards/summary", { signal });

export const createCreditCard = async (payload) =>
  apiFetch("/credit-cards", { method: "POST", body: JSON.stringify(payload) });

export const updateCreditCard = async (id, payload) =>
  apiFetch(`/credit-cards/${id}`, { method: "PUT", body: JSON.stringify(payload) });

export const setPrimaryCard = async (id) => apiFetch(`/credit-cards/primary/${id}`, { method: "PUT" });
