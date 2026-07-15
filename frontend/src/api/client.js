const API_URL = import.meta.env.VITE_API_URL ?? "http://localhost:5000/api";

export const apiFetch = async (endpoint, options = {}) => {
  const token = localStorage.getItem("token");

  const response = await fetch(`${API_URL}${endpoint}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(token && { Authorization: `Bearer ${token}` }),
      ...options.headers,
    },
  });

  const text = await response.text();
  let data = {};
  try {
    data = text ? JSON.parse(text) : {};
  } catch {
    // resposta não é JSON válido, segue com data = {}
  }

  if (!response.ok) {
    // Erros de validação (ex: ModelState do ASP.NET) trazem a causa específica em "errors":
    // isso é mais útil pro usuário do que o "title" genérico ("One or more validation errors...").
    const fieldErrors = data.errors ? Object.values(data.errors).flat().join(" ") : "";
    const msg = fieldErrors || data.message || data.title || `Erro ${response.status}`;
    throw new Error(msg);
  }

  return data;
};