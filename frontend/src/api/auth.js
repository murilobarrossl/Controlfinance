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

// Revoga o token atual no backend — sem isso, "sair" só esquecia o token no
// navegador, mas ele continuava válido (JWT é stateless) até expirar sozinho.
export const logout = async () =>
  apiFetch("/auth/logout", { method: "POST" });