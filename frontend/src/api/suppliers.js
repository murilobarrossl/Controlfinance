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

// Métodos de pagamento (Pix ou conta para TED); payload = { kind: "Pix" | "Bank", pix | bank }.
const methodPath = (id, methodId = "", suffix = "") =>
  supplierPath(id, `/payment-methods${methodId ? `/${encodeURIComponent(methodId)}` : ""}${suffix}`);

export const addSupplierPaymentMethod = async (id, payload) =>
  apiFetch(methodPath(id), { method: "POST", body: JSON.stringify(payload) });

export const updateSupplierPaymentMethod = async (id, methodId, payload) =>
  apiFetch(methodPath(id, methodId), { method: "PUT", body: JSON.stringify(payload) });

export const removeSupplierPaymentMethod = async (id, methodId) => apiFetch(methodPath(id, methodId), { method: "DELETE" });

export const revealSupplierPixKey = async (id, methodId) => apiFetch(methodPath(id, methodId, "/pix-key"));
