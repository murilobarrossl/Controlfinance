import { createContext, useContext, useEffect, useState } from "react";
import { getIntegrations, getConnectors } from "../api/polp.js";
import { deleteBankAccount } from "../api/bankAccounts.js";
import { getCurrentUserId } from "../utils/authToken.js";

const AccountContext = createContext(null);

const STORAGE_KEY = "selectedAccountId";

function storageKey() {
  return `${STORAGE_KEY}:${getCurrentUserId() ?? "anonymous"}`;
}

// Qual conta está selecionada (conta corrente, cartão de crédito etc.) precisa ser a mesma em
// qualquer aba do dashboard. Sem isso, cada tela buscava e somava as transações de todas as
// contas juntas, mesmo depois de escolher uma conta específica no seletor. Guardado aqui (em vez
// de dentro de DashboardHome, que é onde o seletor ficava antes) pra sobreviver à troca de aba, e
// persistido por usuário pra sobreviver a um F5 também.
export function AccountProvider({ children }) {
  const [accounts, setAccounts] = useState([]);
  const [connectors, setConnectors] = useState([]);
  const [selectedAccountId, setSelectedAccountIdState] = useState(() => {
    try {
      return localStorage.getItem(storageKey()) || null;
    } catch {
      return null;
    }
  });
  // Incrementado depois de sincronizar/desconectar uma conta: as abas que já buscaram dados
  // reagem a essa mudança (ver dependência nos useEffect de cada página), sem precisar de um
  // recarregamento manual da página.
  const [dataRefreshKey, setDataRefreshKey] = useState(0);

  function setSelectedAccountId(id) {
    setSelectedAccountIdState(id);
    try {
      if (id) localStorage.setItem(storageKey(), id);
      else localStorage.removeItem(storageKey());
    } catch {
      // localStorage indisponível, segue só em memória nesta sessão
    }
  }

  useEffect(() => {
    getIntegrations().then(setAccounts).catch(() => {});
    getConnectors().then(setConnectors).catch(() => {});
  }, [dataRefreshKey]);

  // Se a conta selecionada foi desconectada (ou o id salvo no localStorage não existe mais),
  // volta pra "conta padrão" (null = deixa o backend escolher a primeira ativa) em vez de
  // continuar apontando pra uma conta que já não existe.
  useEffect(() => {
    if (accounts.length === 0) return;
    if (selectedAccountId && !accounts.some((a) => a.id === selectedAccountId)) {
      // Reage à lista de contas (carregada de forma assíncrona, fora do controle deste
      // componente), não a um valor já disponível no render: não dá pra calcular isso na hora
      // do render, precisa esperar a resposta da API chegar.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSelectedAccountId(null);
    }
  }, [accounts, selectedAccountId]);

  function bumpDataRefresh() {
    setDataRefreshKey((key) => key + 1);
  }

  async function disconnectAccount(accountId) {
    await deleteBankAccount(accountId);
    if (selectedAccountId === accountId) setSelectedAccountId(null);
    bumpDataRefresh();
  }

  // selectedAccountId pode ser null (usuário nunca escolheu, ou a conta escolhida sumiu). Nesse
  // caso o resumo do dashboard (GetSummary) já sabe escolher uma conta padrão sozinho no backend,
  // mas os endpoints de lista (GetAll/GetReport, usados por Receitas x despesas/Categorias/
  // Orçamento/Relatórios) tratam "sem conta" como "sem filtro nenhum": voltaria a somar todas as
  // contas juntas. effectiveAccountId resolve isso aqui, uma vez só, pro mesmo id que o seletor já
  // mostra como "ativo" (accounts[0] como fallback, igual o AccountSwitcher faz internamente).
  const effectiveAccountId = selectedAccountId ?? accounts[0]?.id ?? null;

  return (
    <AccountContext.Provider
      value={{
        accounts,
        connectors,
        selectedAccountId,
        effectiveAccountId,
        setSelectedAccountId,
        dataRefreshKey,
        bumpDataRefresh,
        disconnectAccount,
      }}
    >
      {children}
    </AccountContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAccount() {
  return useContext(AccountContext);
}
