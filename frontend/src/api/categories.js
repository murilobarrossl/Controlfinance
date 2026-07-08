import { apiFetch } from "./client.js";

export const getCategories = async () => apiFetch("/categories");

export const createCategory = async (payload) =>
  apiFetch("/categories", {
    method: "POST",
    body: JSON.stringify(payload),
  });

export const updateCategory = async (id, payload) =>
  apiFetch(`/categories/${id}`, {
    method: "PUT",
    body: JSON.stringify(payload),
  });

export const deleteCategory = async (id) =>
  apiFetch(`/categories/${id}`, { method: "DELETE" });
