import { apiFetch } from "./client.js";

export const deleteBankAccount = async (id) => apiFetch(`/bank-accounts/${id}`, { method: "DELETE" });

// Conta cadastrada à mão, pra quem não conecta banco (ou tem uma conta que o Open Finance não
// cobre). O backend decide PF/PJ pelo documento do cadastro, igual faz com conta sincronizada.
export const createBankAccount = async ({ name, bankCode, balance }) =>
  apiFetch("/bank-accounts", {
    method: "POST",
    body: JSON.stringify({ name, bankCode: bankCode || null, balance }),
  });
