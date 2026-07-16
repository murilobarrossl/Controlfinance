import { apiFetch } from "./client.js";

// Inicia a conexão com um banco: retorna a URL de autenticação do Open Finance
export const createIntegration = async (connectorId) =>
  apiFetch("/polp/integrations", {
    method: "POST",
    body: JSON.stringify({ connectorId }),
  });

// Lista todas as integrações ativas do usuário
export const getIntegrations = async () =>
  apiFetch("/polp/integrations");

// Busca o status de uma integração específica
export const getIntegrationStatus = async (integrationId) =>
  apiFetch(`/polp/integrations/${integrationId}`);

// Dispara a sincronização manual de contas e transações
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

// Lista os bancos/conectores disponíveis
export const getConnectors = async () =>
  apiFetch("/polp/connectors");

