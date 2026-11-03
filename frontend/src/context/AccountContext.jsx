import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { getIntegrations, getConnectors, disconnectIntegration } from "../api/polp.js";
import { deleteBankAccount } from "../api/bankAccounts.js";
import { isAbortError } from "../api/client.js";
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
  const [accountsStatus, setAccountsStatus] = useState("loading");
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
  // Resultado da última desconexão/remoção, mostrado num aviso no topo do dashboard (o menu do
  // seletor fecha ao confirmar, então o resultado não pode ficar só lá dentro).
  const [notice, setNotice] = useState(null);

  const setSelectedAccountId = useCallback((id) => {
    setSelectedAccountIdState(id);
    try {
      if (id) localStorage.setItem(storageKey(), id);
      else localStorage.removeItem(storageKey());
    } catch {
      // localStorage indisponível, segue só em memória nesta sessão
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;

    Promise.all([
      getIntegrations({ signal }),
      getConnectors({ signal }).catch((err) => {
        if (isAbortError(err)) throw err;
        return [];
      }),
    ])
      .then(([accountsData, connectorsData]) => {
        setAccounts(accountsData);
        setConnectors(connectorsData);
        setAccountsStatus("ready");
      })
      .catch((err) => {
        if (isAbortError(err)) return;
        setAccountsStatus("error");
      });

    return () => controller.abort();
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
  }, [accounts, selectedAccountId, setSelectedAccountId]);

  const bumpDataRefresh = useCallback(() => {
    setAccountsStatus("loading");
    setDataRefreshKey((key) => key + 1);
  }, []);

  // Desconecta um banco inteiro (conta corrente + cartão da mesma conexão). O aviso diz o que
  // realmente aconteceu do lado da Polp: "pedido aceito" ou "pendente", nunca "excluído" sem
  // confirmação do fornecedor.
  const disconnectConnection = useCallback(async (localIntegrationId) => {
    try {
      const result = await disconnectIntegration(localIntegrationId);
      setNotice({
        tone: "success",
        text:
          result?.providerDeletion === "requested"
            ? "Banco desconectado. Apagamos do Control Finance os dados que vieram dele, e a Polp aceitou o pedido de exclusão da conexão (ela conclui a exclusão do lado dela em seguida)."
            : "Banco desconectado. Apagamos do Control Finance os dados que vieram dele. Não conseguimos falar com a Polp agora; o pedido de exclusão da conexão fica registrado e será reenviado automaticamente. Se quiser, revogue também a autorização no aplicativo do seu banco, na área de Open Finance.",
      });
    } catch (err) {
      setNotice({ tone: "error", text: err.message || "Não foi possível desconectar o banco. Tente novamente." });
    }
    setSelectedAccountId(null);
    bumpDataRefresh();
  }, [setSelectedAccountId, bumpDataRefresh]);

  // Só pra contas cadastradas manualmente: apaga a conta, as transações e os parcelamentos dela.
  const removeManualAccount = useCallback(async (accountId) => {
    try {
      await deleteBankAccount(accountId);
      setNotice({ tone: "success", text: "Conta removida, junto com os lançamentos dela." });
    } catch (err) {
      setNotice({ tone: "error", text: err.message || "Não foi possível remover a conta." });
    }
    if (selectedAccountId === accountId) setSelectedAccountId(null);
    bumpDataRefresh();
  }, [selectedAccountId, setSelectedAccountId, bumpDataRefresh]);

  const dismissNotice = useCallback(() => setNotice(null), []);

  // selectedAccountId pode ser null (usuário nunca escolheu, ou a conta escolhida sumiu). Nesse
  // caso o resumo do dashboard (GetSummary) já sabe escolher uma conta padrão sozinho no backend,
  // mas os endpoints de lista (GetAll/GetReport, usados por Receitas x despesas/Categorias/
  // Relatórios) tratam "sem conta" como "sem filtro nenhum": voltaria a somar todas as
  // contas juntas. effectiveAccountId resolve isso aqui, uma vez só, pro mesmo id que o seletor já
  // mostra como "ativo" (accounts[0] como fallback, igual o AccountSwitcher faz internamente).
  const effectiveAccountId = selectedAccountId ?? accounts[0]?.id ?? null;

  const value = useMemo(
    () => ({
      accounts,
      connectors,
      accountsStatus,
      selectedAccountId,
      effectiveAccountId,
      setSelectedAccountId,
      dataRefreshKey,
      bumpDataRefresh,
      disconnectConnection,
      removeManualAccount,
      notice,
      dismissNotice,
    }),
    [accounts, connectors, accountsStatus, selectedAccountId, effectiveAccountId, setSelectedAccountId, dataRefreshKey, bumpDataRefresh, disconnectConnection, removeManualAccount, notice, dismissNotice],
  );

  return <AccountContext.Provider value={value}>{children}</AccountContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAccount() {
  return useContext(AccountContext);
}
