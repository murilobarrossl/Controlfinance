import { apiFetch } from "./client.js";

export const getCategories = async ({ signal } = {}) => apiFetch("/categories", { signal });
