import { apiFetch, apiRequest, readError } from "./client.js";

// Baixa a cópia dos dados (JSON) direto como arquivo, sem passar o conteúdo por nenhum estado
// da aplicação nem guardar no navegador além do download em si.
export async function downloadMyData() {
  const response = await apiRequest("/account/export");
  if (!response.ok) throw new Error(await readError(response));

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `control-finance-meus-dados-${new Date().toISOString().slice(0, 10)}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

// Prazos de retenção configurados no servidor (podem ser nulos = sem prazo definido).
export const getDataPolicy = async () => apiFetch("/account/data-policy");

export const updateNotificationPreferences = async (preferences) =>
  apiFetch("/account/notifications", { method: "PUT", body: JSON.stringify(preferences) });

// Exclusão definitiva da conta. Devolve { accountDeleted, providerDeletion: "requested" | "pending" }.
export const deleteMyAccount = async (password) =>
  apiFetch("/account/deletion", { method: "POST", body: JSON.stringify({ password }) });
