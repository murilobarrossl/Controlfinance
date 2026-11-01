import { apiFetch } from "./client.js";

export const getInstallments = async ({ signal } = {}) => apiFetch("/installments", { signal });

export const createInstallment = async (payload) =>
  apiFetch("/installments", {
    method: "POST",
    body: JSON.stringify(payload),
  });

export const deleteInstallment = async (id) =>
  apiFetch(`/installments/${id}`, { method: "DELETE" });
