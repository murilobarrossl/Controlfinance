import { apiFetch } from "./client.js";

// Categorias, Relatórios e Receitas x Despesas buscam a mesma lista completa de transações
// de forma independente uma da outra e a filtram no cliente por mês — sem esse cache, trocar
// de aba refaz a mesma requisição de rede. TTL curto porque os dados mudam com frequência
// (CRUD manual, sync do Polp) e ficam invalidados nas mutações abaixo.
const CACHE_TTL_MS = 15_000;
const cache = new Map(); // query string -> { promise, expiresAt }

const buildQuery = ({ status, type } = {}) => {
  const params = new URLSearchParams();
  if (status) params.set("status", status);
  if (type) params.set("type", type);
  return params.toString();
};

export const getTransactions = async (filters = {}) => {
  const query = buildQuery(filters);
  const cached = cache.get(query);
  if (cached && cached.expiresAt > Date.now()) return cached.promise;

  const promise = apiFetch(`/transactions${query ? `?${query}` : ""}`);
  cache.set(query, { promise, expiresAt: Date.now() + CACHE_TTL_MS });

  promise.catch(() => cache.delete(query)); // não guarda respostas com erro
  return promise;
};

const invalidateCache = () => cache.clear();

export const createTransaction = async (payload) => {
  const result = await apiFetch("/transactions", {
    method: "POST",
    body: JSON.stringify(payload),
  });
  invalidateCache();
  return result;
};

export const updateTransaction = async (id, payload) => {
  const result = await apiFetch(`/transactions/${id}`, {
    method: "PUT",
    body: JSON.stringify(payload),
  });
  invalidateCache();
  return result;
};

export const deleteTransaction = async (id) => {
  const result = await apiFetch(`/transactions/${id}`, { method: "DELETE" });
  invalidateCache();
  return result;
};
