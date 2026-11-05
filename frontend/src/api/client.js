const API_URL = import.meta.env.VITE_API_URL ?? "http://localhost:5080/api";

// Segredo anti-CSRF da sessão atual (ver backend/Services/CsrfProtection.cs). Só em memória:
// nunca em localStorage/sessionStorage. Some ao recarregar a página e é recuperado por
// GET /auth/me, que o AuthContext chama na abertura.
let csrfToken = null;

export function setCsrfToken(token) {
  csrfToken = token || null;
}

const UNSAFE_METHODS = new Set(["POST", "PUT", "PATCH", "DELETE"]);

export const ERROR_MESSAGES = {
  network: "Não foi possível conectar. Verifique sua internet e tente de novo.",
  server: "O serviço está instável no momento. Tente de novo em alguns minutos.",
  timeout: "A resposta demorou demais. Tente de novo em instantes.",
  unknown: "Não foi possível concluir a operação. Tente de novo.",
};

// Endpoints em que 401 é resposta esperada (credencial errada, ou só checando se existe
// sessão), não "a sessão caiu": não disparam o redirecionamento pro login.
const NO_REDIRECT_ENDPOINTS = ["/auth/login", "/auth/register", "/auth/me"];

// A sessão vive num cookie httpOnly que o JavaScript não lê: `credentials: "include"` faz o
// navegador mandá-lo sozinho. Nenhum token é guardado no navegador por este código.
export async function apiRequest(endpoint, options = {}) {
  const method = (options.method || "GET").toUpperCase();

  try {
    return await fetch(`${API_URL}${endpoint}`, {
      ...options,
      method,
      credentials: "include",
      headers: {
        ...(options.body !== undefined && { "Content-Type": "application/json" }),
        ...(UNSAFE_METHODS.has(method) && csrfToken && { "X-CSRF-Token": csrfToken }),
        ...options.headers,
      },
    });
  } catch (err) {
    if (isAbortError(err)) throw err;
    const error = new Error(ERROR_MESSAGES.network);
    error.status = 0;
    error.kind = "network";
    throw error;
  }
}

export async function readError(response) {
  if (response.status === 408 || response.status === 504) return ERROR_MESSAGES.timeout;
  if (response.status >= 500) return ERROR_MESSAGES.server;

  const text = await response.text();
  let data = {};
  try {
    data = text ? JSON.parse(text) : {};
  } catch {
    // resposta não é JSON válido, segue com data = {}
  }
  // Erros de validação (ex: ModelState do ASP.NET) trazem a causa específica em "errors":
  // isso é mais útil pro usuário do que o "title" genérico ("One or more validation errors...").
  const fieldErrors = data.errors ? Object.values(data.errors).flat().join(" ") : "";
  return fieldErrors || data.message || ERROR_MESSAGES.unknown;
}

export const apiFetch = async (endpoint, options = {}) => {
  const response = await apiRequest(endpoint, options);

  if (!response.ok) {
    // 401 num endpoint autenticado significa sessão expirada, revogada ou encerrada em outro
    // aparelho: manda pro login, senão a tela fica presa mostrando erro genérico pra sempre.
    const skipRedirect = NO_REDIRECT_ENDPOINTS.some((path) => endpoint.startsWith(path));
    if (response.status === 401 && !skipRedirect) {
      setCsrfToken(null);
      if (!window.location.pathname.startsWith("/login") && window.location.pathname !== "/") {
        window.location.href = "/loginemail";
      }
    }

    const error = new Error(await readError(response));
    error.status = response.status;
    throw error;
  }

  const text = await response.text();
  try {
    return text ? JSON.parse(text) : {};
  } catch {
    return {};
  }
};

export const isAbortError = (err) => err?.name === "AbortError";
