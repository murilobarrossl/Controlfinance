import { apiFetch } from "./client.js";

const buildQuery = ({ status, type, bankAccountId } = {}) => {
  const params = new URLSearchParams();
  if (status) params.set("status", status);
  if (type) params.set("type", type);
  if (bankAccountId) params.set("bankAccountId", bankAccountId);
  return params.toString();
};

const transactionsPath = (filters) => {
  const query = buildQuery(filters);
  return `/transactions${query ? `?${query}` : ""}`;
};

export const getTransactions = async (filters = {}, { signal } = {}) => apiFetch(transactionsPath(filters), { signal });

const buildReportQuery = ({ status, type, search, bankAccountId, year, month, sortBy, sortDir, page, pageSize } = {}) => {
  const params = new URLSearchParams();
  if (status) params.set("status", status);
  if (type) params.set("type", type);
  if (search) params.set("search", search);
  if (bankAccountId) params.set("bankAccountId", bankAccountId);
  if (year) params.set("year", year);
  if (month) params.set("month", month);
  if (sortBy) params.set("sortBy", sortBy);
  if (sortDir) params.set("sortDir", sortDir);
  if (page) params.set("page", page);
  if (pageSize) params.set("pageSize", pageSize);
  return params.toString();
};

const reportPath = (filters) => {
  const query = buildReportQuery(filters);
  return `/transactions/report${query ? `?${query}` : ""}`;
};

export const getTransactionsReport = async (filters = {}, { signal } = {}) => apiFetch(reportPath(filters), { signal });

export const setTransactionDetails = async (id, { name, categoryId }) => {
  return apiFetch(`/transactions/${id}/details`, {
    method: "PUT",
    body: JSON.stringify({ name, categoryId: categoryId || null }),
  });
};

export const getTransactionAggregates = async ({ bankAccountId, from, to }, { signal } = {}) => {
  const params = new URLSearchParams({ from, to });
  if (bankAccountId) params.set("bankAccountId", bankAccountId);
  return apiFetch(`/transactions/aggregates?${params.toString()}`, { signal });
};

export const getTransactionDetails = async (
  { bankAccountId, from, to, type, category, establishment },
  { signal } = {}
) => {
  const params = new URLSearchParams({ from, to });
  if (bankAccountId) params.set("bankAccountId", bankAccountId);
  if (type) params.set("type", type);
  if (category) params.set("category", category);
  if (establishment) params.set("establishment", establishment);
  return apiFetch(`/transactions/details?${params.toString()}`, { signal });
};
