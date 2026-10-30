import { apiFetch } from "./client.js";

export const login = async (identifier, password) =>
  apiFetch("/auth/login", {
    method: "POST",
    body: JSON.stringify({ identifier, password }),
  });

export const register = async (payload) =>
  apiFetch("/auth/register", {
    method: "POST",
    body: JSON.stringify(payload),
  });

// Descobre se já existe sessão (o cookie é httpOnly, o JavaScript não consegue olhar). Devolve
// { user, csrfToken }.
export const getCurrentUser = async () => apiFetch("/auth/me");

// Revoga o token da sessão atual e apaga o cookie no backend.
export const logout = async () => apiFetch("/auth/logout", { method: "POST" });

// Encerra todas as sessões abertas, em qualquer aparelho.
export const logoutAllSessions = async () => apiFetch("/auth/logout-all", { method: "POST" });
