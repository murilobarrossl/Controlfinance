import { apiFetch } from "./client.js";

export const getCreditCards = async () => apiFetch("/credit-cards");

export const getCreditCardsSummary = async () => apiFetch("/credit-cards/summary");

export const createCreditCard = async (payload) =>
  apiFetch("/credit-cards", { method: "POST", body: JSON.stringify(payload) });
