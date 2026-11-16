import { apiFetch } from "./client.js";

const supplierPath = (id, suffix = "") => `/suppliers/${encodeURIComponent(id)}${suffix}`;

export const getSuppliers = async ({ search, page, pageSize } = {}, { signal } = {}) => {
  const params = new URLSearchParams();
  if (search) params.set("search", search);
  if (page) params.set("page", page);
  if (pageSize) params.set("pageSize", pageSize);
  const query = params.toString();
  return apiFetch(`/suppliers${query ? `?${query}` : ""}`, { signal });
};

export const getSuppliersSummary = async ({ signal } = {}) => apiFetch("/suppliers/summary", { signal });

export const getSupplier = async (id, { signal } = {}) => apiFetch(supplierPath(id), { signal });

export const createSupplier = async (payload) => apiFetch("/suppliers", { method: "POST", body: JSON.stringify(payload) });

export const updateSupplier = async (id, payload) => apiFetch(supplierPath(id), { method: "PUT", body: JSON.stringify(payload) });

export const deleteSupplier = async (id) => apiFetch(supplierPath(id), { method: "DELETE" });

export const setSupplierPix = async (id, payload) => apiFetch(supplierPath(id, "/pix"), { method: "PUT", body: JSON.stringify(payload) });

export const removeSupplierPix = async (id) => apiFetch(supplierPath(id, "/pix"), { method: "DELETE" });

export const setSupplierBankAccount = async (id, payload) =>
  apiFetch(supplierPath(id, "/bank-account"), { method: "PUT", body: JSON.stringify(payload) });

export const removeSupplierBankAccount = async (id) => apiFetch(supplierPath(id, "/bank-account"), { method: "DELETE" });
