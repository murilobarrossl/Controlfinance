import { apiFetch } from "./client.js";

export const deleteBankAccount = async (id) => apiFetch(`/bank-accounts/${id}`, { method: "DELETE" });
