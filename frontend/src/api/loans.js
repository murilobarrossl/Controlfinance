import { apiFetch } from "./client.js";

export const getLoanContracts = async ({ signal } = {}) => apiFetch("/loans", { signal });
