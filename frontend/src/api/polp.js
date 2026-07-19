import { apiFetch } from "./client.js";

export const createIntegration = async (connectorId) =>
  apiFetch("/polp/integrations", {
    method: "POST",
    body: JSON.stringify({ connectorId }),
  });

export const getIntegrations = async () =>
  apiFetch("/polp/integrations");

export const getIntegrationStatus = async (integrationId) =>
  apiFetch(`/polp/integrations/${integrationId}`);

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

