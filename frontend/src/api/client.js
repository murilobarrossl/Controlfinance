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
  try { data = text ? JSON.parse(text) : {}; } catch (_) {}

  if (!response.ok) {
    const msg = data.message || data.title || data.errors
      ? (data.message ?? data.title ?? Object.values(data.errors ?? {}).flat().join(", "))
      : `Erro ${response.status}`;
    throw new Error(msg);
  }

  return data;
};