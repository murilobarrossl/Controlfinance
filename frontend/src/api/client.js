const API_URL = import.meta.env.VITE_API_URL ?? "http://localhost:5000/api";

// O token de sessão vive num cookie httpOnly agora (o backend manda sozinho, JS nunca lê ele).
// Esse aqui é o outro cookie, o par CSRF (double-submit): esse sim é legível via JS de
// propósito, só serve pra provar que quem chamou a API foi o próprio frontend, não um site
// forjando a requisição só se aproveitando do cookie de sessão sendo enviado automaticamente.
function getCookie(name) {
  const match = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : null;
}

export const apiFetch = async (endpoint, options = {}) => {
  const method = (options.method ?? "GET").toUpperCase();
  const csrfToken = method !== "GET" ? getCookie("XSRF-TOKEN") : null;

  const response = await fetch(`${API_URL}${endpoint}`, {
    ...options,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(csrfToken && { "X-XSRF-TOKEN": csrfToken }),
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
    // 401 num endpoint autenticado significa sessão inválida/expirada/revogada: manda pro
    // login, senão a tela fica presa mostrando erro genérico pra sempre. Login/registro ficam
    // de fora: ali um 401 é "credencial errada", não "sessão caiu".
    const isAuthEndpoint = endpoint.startsWith("/auth/login") || endpoint.startsWith("/auth/register");
    if (response.status === 401 && !isAuthEndpoint) {
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
