import { apiFetch } from "./client.js";

// document: CPF/CNPJ digitado na tela de conexão. Vai direto pra Polp (que exige o documento
// do titular pra criar a integração) e não é guardado nem no navegador nem no backend.
// consentVersion: versão do aviso de conexão que a pessoa confirmou (ver legal/versions.js).
export const createIntegration = async (connectorId, document, consentVersion) =>
  apiFetch("/polp/integrations", {
    method: "POST",
    body: JSON.stringify({ connectorId, document, consentVersion }),
  });

// Desconecta um banco: apaga do Control Finance tudo que veio dessa conexão e pede a exclusão
// da integração na Polp. Devolve { localDataDeleted, providerDeletion: "requested" | "pending" }.
export const disconnectIntegration = async (localIntegrationId) =>
  apiFetch(`/polp/integrations/${localIntegrationId}`, { method: "DELETE" });

export const getIntegrations = async () =>
  apiFetch("/polp/integrations");

export const getIntegrationStatus = async (integrationId) =>
  apiFetch(`/polp/integrations/${integrationId}`);

// integrationId aqui é o Guid local (PolpIntegration.Id), não o id remoto da Polp guardado em
// BankAccount.PolpIntegrationId, que vem como localIntegrationId na resposta de getIntegrations().
export const syncIntegration = async (integrationId) =>
  apiFetch(`/polp/integrations/${integrationId}/sync`, {
    method: "POST",
  });

// Sincroniza todas as integrações do usuário de uma vez (usado ao carregar o dashboard, pra
// saldo/transações não ficarem desatualizados até a próxima reconexão manual do banco)
export const syncAllIntegrations = async () =>
  apiFetch("/polp/integrations/sync-all", {
    method: "POST",
  });

export const getConnectors = async () =>
  apiFetch("/polp/connectors");

