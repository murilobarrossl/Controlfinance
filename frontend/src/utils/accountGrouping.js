// Contas com o mesmo polpIntegrationId vieram da mesma conexão bancária (ex.: conta corrente +
// cartão de crédito do mesmo banco, sincronizados juntos). Agrupa pra deixar isso visualmente
// claro tanto no seletor de contas (AccountSwitcher) quanto no aviso abaixo do cabeçalho do
// Dashboard. Cada conta continua com sua própria visão de dados ao ser selecionada, nada é
// somado entre elas; contas sem polpIntegrationId (criadas manualmente) formam grupo de uma só.
export function groupAccounts(accounts, connectorsById) {
  const groups = new Map();
  for (const account of accounts) {
    const key = account.polpIntegrationId ?? `single-${account.id}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(account);
  }
  return [...groups.values()].map((group) => ({
    accounts: group,
    label: connectorsById.get(group[0].bankCode)?.name,
  }));
}
