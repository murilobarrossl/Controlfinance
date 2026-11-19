import { apiFetch } from "./client.js";

// Custos adicionais dos funcionários: nome e valor informados pela empresa.
const employeePath = (id, suffix = "") => `/payroll/employees/${encodeURIComponent(id)}${suffix}`;
const json = (method, body) => ({ method, body: JSON.stringify(body) });

export const getEmployeeObligations = async (id, { signal } = {}) => apiFetch(employeePath(id), { signal });

// Gastos adicionais do funcionário: nome livre e valor opcional (null = não informado).
export const saveEmployeeCost = async (id, costId, payload) =>
  costId
    ? apiFetch(employeePath(id, `/costs/${encodeURIComponent(costId)}`), json("PUT", payload))
    : apiFetch(employeePath(id, "/costs"), json("POST", payload));

export const deleteEmployeeCost = async (id, costId) =>
  apiFetch(employeePath(id, `/costs/${encodeURIComponent(costId)}`), { method: "DELETE" });
