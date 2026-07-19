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
    // 401 num endpoint autenticado significa sessão inválida/expirada/revogada: limpa o token
    // morto e manda pro login, senão a tela fica presa mostrando erro genérico pra sempre.
    // Login/registro ficam de fora: ali um 401 é "credencial errada", não "sessão caiu".
    const isAuthEndpoint = endpoint.startsWith("/auth/login") || endpoint.startsWith("/auth/register");
    if (response.status === 401 && !isAuthEndpoint) {
      localStorage.removeItem("token");
      if (!window.location.pathname.startsWith("/login") && window.location.pathname !== "/") {
        window.location.href = "/loginemail";
      }
    }

    // Erros de validação (ex: ModelState do ASP.NET) trazem a causa específica em "errors":
    // isso é mais útil pro usuário do que o "title" genérico ("One or more validation errors...").
    const fieldErrors = data.errors ? Object.values(data.errors).flat().join(" ") : "";
    const msg = fieldErrors || data.message || data.title || `Erro ${response.status}`;
    throw new Error(msg);
  }

  return data;
};
