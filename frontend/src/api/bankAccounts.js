import { apiFetch } from "./client.js";

export const getBankAccounts = async () => apiFetch("/bank-accounts");
