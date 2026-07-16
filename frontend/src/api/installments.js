import { apiFetch } from "./client.js";

export const getInstallments = async () => apiFetch("/installments");

export const createInstallment = async (payload) =>
  apiFetch("/installments", {
    method: "POST",
    body: JSON.stringify(payload),
  });

export const deleteInstallment = async (id) =>
  apiFetch(`/installments/${id}`, { method: "DELETE" });
