import { apiFetch } from "./client.js";

// document: CPF/CNPJ digitado na tela de conexão. Vai direto pra Polp (que exige o documento
// do titular pra criar a integração) e não é guardado nem no navegador nem no backend.
// consentVersion: versão do aviso de conexão que a pessoa confirmou (ver legal/versions.js).
// operatorDocument: CPF de quem opera a conta, só em conexão de empresa (CNPJ); a Polp exige. Mesmo
// tratamento do document: vai direto pra Polp e não é guardado.
export const createIntegration = async (connectorId, document, consentVersion, operatorDocument) =>
  apiFetch("/polp/integrations", {
    method: "POST",
    body: JSON.stringify({ connectorId, document, consentVersion, operatorDocument: operatorDocument || null }),
  });

// Desconecta um banco: apaga do Control Finance tudo que veio dessa conexão e revoga o
// consentimento na Polp. Devolve { localDataDeleted, providerDeletion: "requested" | "pending" }.
export const disconnectIntegration = async (localIntegrationId) =>
  apiFetch(`/polp/integrations/${localIntegrationId}`, { method: "DELETE" });

export const getIntegrations = async ({ signal } = {}) =>
  apiFetch("/polp/integrations", { signal });

export const getIntegrationStatus = async (integrationId) =>
  apiFetch(`/polp/integrations/${integrationId}`);

// Não existe mais "sincronizar tudo agora": a Polp sincroniza por agenda e avisa o backend por
// webhook. A única consulta na hora que sobrou é o saldo de uma conta, a pedido do usuário
// (limitada pela Polp; devolve 429 com retryAfterSeconds se for repetida em menos de 1 minuto).
export const syncAccountBalance = async (accountId) =>
  apiFetch(`/polp/accounts/${accountId}/balance-sync`, {
    method: "POST",
  });

export const getConnectors = async ({ signal } = {}) =>
  apiFetch("/polp/connectors", { signal });

