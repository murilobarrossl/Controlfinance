import { apiFetch } from "./client.js";

export const deleteBankAccount = async (id) => apiFetch(`/bank-accounts/${id}`, { method: "DELETE" });

// Só completa limite/dia de fechamento/dia de vencimento que a Polp não mandou pra essa conta
// (reconhecida como cartão) — nunca sobrescreve um valor que já veio real da sincronização.
export const setBankAccountCardDetails = async (id, payload) =>
  apiFetch(`/bank-accounts/${id}/card-details`, { method: "PUT", body: JSON.stringify(payload) });
