import { apiFetch } from "./client.js";

// ── BANK ACCOUNTS ──
export const getBankAccounts = () => apiFetch("/bank-accounts");
export const createBankAccount = (dto) => apiFetch("/bank-accounts", { method: "POST", body: JSON.stringify(dto) });
export const updateBankAccount = (id, dto) => apiFetch(`/bank-accounts/${id}`, { method: "PUT", body: JSON.stringify(dto) });
export const deleteBankAccount = (id) => apiFetch(`/bank-accounts/${id}`, { method: "DELETE" });

// ── TRANSACTIONS ──
export const getTransactions = () => apiFetch("/transactions");
export const createTransaction = (dto) => apiFetch("/transactions", { method: "POST", body: JSON.stringify(dto) });
export const updateTransaction = (id, dto) => apiFetch(`/transactions/${id}`, { method: "PUT", body: JSON.stringify(dto) });
export const deleteTransaction = (id) => apiFetch(`/transactions/${id}`, { method: "DELETE" });

// ── CATEGORIES ──
export const getCategories = () => apiFetch("/categories");
export const createCategory = (dto) => apiFetch("/categories", { method: "POST", body: JSON.stringify(dto) });
export const updateCategory = (id, dto) => apiFetch(`/categories/${id}`, { method: "PUT", body: JSON.stringify(dto) });
export const deleteCategory = (id) => apiFetch(`/categories/${id}`, { method: "DELETE" });

// ── DASHBOARD ──
export const getDashboard = (bankAccountId) =>
  apiFetch(bankAccountId ? `/dashboard?bankAccountId=${bankAccountId}` : "/dashboard");

// ── CREDIT CARDS ──
export const getCreditCards = () => apiFetch("/credit-cards");
export const createCreditCard = (dto) => apiFetch("/credit-cards", { method: "POST", body: JSON.stringify(dto) });
export const deleteCreditCard = (id) => apiFetch(`/credit-cards/${id}`, { method: "DELETE" });