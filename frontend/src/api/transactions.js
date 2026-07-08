import { apiFetch } from "./client.js";

export const getTransactions = async ({ status, type } = {}) => {
  const params = new URLSearchParams();
  if (status) params.set("status", status);
  if (type) params.set("type", type);
  const query = params.toString();

  return apiFetch(`/transactions${query ? `?${query}` : ""}`);
};

export const createTransaction = async (payload) =>
  apiFetch("/transactions", {
    method: "POST",
    body: JSON.stringify(payload),
  });

export const updateTransaction = async (id, payload) =>
  apiFetch(`/transactions/${id}`, {
    method: "PUT",
    body: JSON.stringify(payload),
  });

export const deleteTransaction = async (id) =>
  apiFetch(`/transactions/${id}`, { method: "DELETE" });
