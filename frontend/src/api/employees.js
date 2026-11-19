import { apiFetch } from "./client.js";

// Funcionários da empresa (só login CNPJ; o backend recusa os outros). CPF, chave Pix e conta
// nunca voltam inteiros: a API devolve só máscaras.
export const getEmployees = async ({ search, status, department, payment, page, pageSize } = {}, { signal } = {}) => {
  const params = new URLSearchParams();
  if (search) params.set("search", search);
  if (payment) params.set("payment", payment);
  if (status) params.set("status", status);
  if (department) params.set("department", department);
  if (page) params.set("page", page);
  if (pageSize) params.set("pageSize", pageSize);
  const query = params.toString();
  return apiFetch(`/employees${query ? `?${query}` : ""}`, { signal });
};

export const getEmployeesSummary = async ({ signal } = {}) => apiFetch("/employees/summary", { signal });

export const getEmployee = async (id, { signal } = {}) => apiFetch(`/employees/${encodeURIComponent(id)}`, { signal });

export const createEmployee = async (payload) =>
  apiFetch("/employees", { method: "POST", body: JSON.stringify(payload) });

const employeePath = (id, suffix = "") => `/employees/${encodeURIComponent(id)}${suffix}`;

// Dados principais (nome, CPF, cargo, salário, admissão). cpf vazio mantém o atual.
export const updateEmployee = async (id, payload) =>
  apiFetch(employeePath(id), { method: "PUT", body: JSON.stringify(payload) });

// Departamento e vínculo; employmentType nulo = não informado.
export const setEmployeeClassification = async (id, { department, employmentType }) =>
  apiFetch(employeePath(id, "/classification"), {
    method: "PUT",
    body: JSON.stringify({ department: department || null, employmentType: employmentType || null }),
  });

// Métodos de pagamento (Pix ou conta para TED); payload = { kind: "Pix" | "Bank", pix | bank }.
const methodPath = (id, methodId = "", suffix = "") =>
  employeePath(id, `/payment-methods${methodId ? `/${encodeURIComponent(methodId)}` : ""}${suffix}`);

export const addEmployeePaymentMethod = async (id, payload) =>
  apiFetch(methodPath(id), { method: "POST", body: JSON.stringify(payload) });

export const updateEmployeePaymentMethod = async (id, methodId, payload) =>
  apiFetch(methodPath(id, methodId), { method: "PUT", body: JSON.stringify(payload) });

export const removeEmployeePaymentMethod = async (id, methodId) => apiFetch(methodPath(id, methodId), { method: "DELETE" });

export const revealEmployeePixKey = async (id, methodId) => apiFetch(methodPath(id, methodId, "/pix-key"));

export const terminateEmployee = async (id, terminationDate) =>
  apiFetch(`/employees/${encodeURIComponent(id)}/termination`, {
    method: "POST",
    body: JSON.stringify({ terminationDate }),
  });

export const reactivateEmployee = async (id) =>
  apiFetch(`/employees/${encodeURIComponent(id)}/reactivation`, { method: "POST" });

// Exclusão definitiva; o backend só aceita para funcionário já desligado.
export const deleteEmployee = async (id) => apiFetch(employeePath(id), { method: "DELETE" });

// Pago no mês: estado final (decisão manual do mês ou identificação bancária) e a origem dele.
export const getEmployeePaymentMonth = async (id, { signal } = {}) => apiFetch(employeePath(id, "/payment-month"), { signal });

export const setEmployeePaymentMonth = async (id, { referenceMonth, paid }) =>
  apiFetch(employeePath(id, "/payment-month"), { method: "PUT", body: JSON.stringify({ referenceMonth, paid }) });
