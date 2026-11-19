import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { getIntegrations, getConnectors, getBankConnections, disconnectIntegration } from "../api/polp.js";
import { deleteBankAccount, setDefaultBankAccount, clearDefaultBankAccount } from "../api/bankAccounts.js";
import { isAbortError } from "../api/client.js";
import { getCurrentUserId } from "../utils/authToken.js";
import { buildScopes, resolveScope } from "../utils/accountScopes.js";
import { useAuth } from "./AuthContext.jsx";

const AccountContext = createContext(null);

function storageKey(name) {
  return `${name}:${getCurrentUserId() ?? "anonymous"}`;
}

// O seletor antigo guardava um id de conta; vira "acct:<id>" e resolveScope leva pro banco dela.
function readStoredScopeKey() {
  try {
    const stored = localStorage.getItem(storageKey("selectedScope"));
    if (stored) return stored;
    const legacy = localStorage.getItem(storageKey("selectedAccountId"));
    if (!legacy) return null;
    localStorage.setItem(storageKey("selectedScope"), `acct:${legacy}`);
    localStorage.removeItem(storageKey("selectedAccountId"));
    return `acct:${legacy}`;
  } catch {
    return null;
  }
}

// O banco selecionado precisa ser o mesmo em qualquer aba do dashboard, e sobreviver a um F5.
export function AccountProvider({ children }) {
  const { user } = useAuth();
  return <OwnedAccountProvider key={user?.id ?? "anonymous"}>{children}</OwnedAccountProvider>;
}

function OwnedAccountProvider({ children }) {
  const [accounts, setAccounts] = useState([]);
  const [connections, setConnections] = useState([]);
  const [connectionsStatus, setConnectionsStatus] = useState("loading");
  const [connectionsRefreshing, setConnectionsRefreshing] = useState(false);
  const [connectors, setConnectors] = useState([]);
  const [accountsStatus, setAccountsStatus] = useState("loading");
  const [selectedScopeKey, setSelectedScopeKeyState] = useState(readStoredScopeKey);
  // Incrementado depois de sincronizar/desconectar uma conta: as abas que já buscaram dados
  // reagem a essa mudança (ver dependência nos useEffect de cada página), sem precisar de um
  // recarregamento manual da página.
  const [dataRefreshKey, setDataRefreshKey] = useState(0);
  // Resultado da última desconexão/remoção, mostrado num aviso no topo do dashboard (o menu do
  // seletor fecha ao confirmar, então o resultado não pode ficar só lá dentro).
  const [notice, setNotice] = useState(null);
  // Leitura de contas iniciada antes de uma troca da conta padrão terminar pode trazer a padrão
  // antiga; ela é descartada e a confirmação da própria troca vale.
  const defaultWriteSeq = useRef(0);

  const setSelectedScope = useCallback((key) => {
    setSelectedScopeKeyState(key);
    try {
      if (key) localStorage.setItem(storageKey("selectedScope"), key);
      else localStorage.removeItem(storageKey("selectedScope"));
    } catch {
      // localStorage indisponível, segue só em memória nesta sessão
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;

    let inFlight = false;
    async function refresh() {
      if (signal.aborted || inFlight) return;
      inFlight = true;
      setConnectionsRefreshing(true);
      const defaultSeqAtStart = defaultWriteSeq.current;
      const accountRead = Promise.all([
        getIntegrations({ signal }),
        getConnectors({ signal }).catch((err) => {
          if (isAbortError(err)) throw err;
          return [];
        }),
      ])
      .then(([accountsData, connectorsData]) => {
        if (signal.aborted) return;
        if (defaultSeqAtStart === defaultWriteSeq.current) setAccounts(accountsData);
        setConnectors(connectorsData);
        setAccountsStatus("ready");
      })
      .catch((err) => {
        if (isAbortError(err) || signal.aborted) return;
        setAccountsStatus("error");
      });
      const connectionRead = (async () => {
        const rows = new Map();
        const cursors = new Set();
        let cursor = null;
        do {
          const page = await getBankConnections({ cursor, signal });
          if (!Array.isArray(page.data) || page.data.some((row) => !row.id)) throw new Error("Invalid connection page");
          for (const row of page.data) rows.set(row.id, row);
          cursor = page.nextCursor ?? null;
          if (cursor !== null && (typeof cursor !== "string" || !cursor || cursors.has(cursor) || cursors.size >= 100))
            throw new Error("Invalid connection pagination");
          if (cursor) cursors.add(cursor);
        } while (cursor && !signal.aborted);
        if (signal.aborted) return;
        setConnections([...rows.values()]);
        setConnectionsStatus("ready");
      })().catch((err) => {
        if (!isAbortError(err) && !signal.aborted) setConnectionsStatus("error");
      });
      await Promise.allSettled([accountRead, connectionRead]);
      inFlight = false;
      if (!signal.aborted) setConnectionsRefreshing(false);
    }
    refresh();
    const visibleRefresh = () => { if (document.visibilityState === "visible") refresh(); };
    const timer = setInterval(visibleRefresh, 60000);
    document.addEventListener("visibilitychange", visibleRefresh);
    return () => { controller.abort(); clearInterval(timer); document.removeEventListener("visibilitychange", visibleRefresh); };
  }, [dataRefreshKey]);

  const bumpDataRefresh = useCallback(() => {
    setAccountsStatus("loading");
    setConnectionsRefreshing(true);
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
            ? "Banco desconectado. Os dados que vieram dele foram apagados."
            : "Banco desconectado. Os dados que vieram dele foram apagados. Se quiser, revogue também a autorização no aplicativo do seu banco, na área de Open Finance.",
      });
    } catch (err) {
      setNotice({ tone: "error", text: err.message || "Não foi possível desconectar o banco. Tente novamente." });
    }
    setSelectedScope(null);
    bumpDataRefresh();
  }, [setSelectedScope, bumpDataRefresh]);

  // Só pra contas cadastradas manualmente: apaga a conta, as transações e os parcelamentos dela.
  const removeManualAccount = useCallback(async (accountId) => {
    try {
      await deleteBankAccount(accountId);
      setNotice({ tone: "success", text: "Conta removida, junto com os lançamentos dela." });
    } catch (err) {
      setNotice({ tone: "error", text: err.message || "Não foi possível remover a conta." });
    }
    if (selectedScopeKey === `acct:${accountId}`) setSelectedScope(null);
    bumpDataRefresh();
  }, [selectedScopeKey, setSelectedScope, bumpDataRefresh]);

  const changeDefaultAccount = useCallback(async (accountId) => {
    defaultWriteSeq.current += 1;
    let saved = false;
    // O erro volta para quem chamou (o diálogo o mostra): aviso global aqui duplicaria a mensagem.
    let failure = null;
    try {
      if (accountId) await setDefaultBankAccount(accountId);
      else await clearDefaultBankAccount();
      saved = true;
    } catch (err) {
      failure = err.message || "Não foi possível alterar a conta padrão.";
    }
    const seq = ++defaultWriteSeq.current;
    if (saved) setAccounts((prev) => prev.map((account) => ({ ...account, isDefault: account.id === accountId })));
    try {
      const fresh = await getIntegrations();
      if (seq === defaultWriteSeq.current) setAccounts(fresh);
    } catch {
      return { saved, error: failure };
    }
    return { saved, error: failure };
  }, []);

  const dismissNotice = useCallback(() => setNotice(null), []);

  // Toda tela filtra pelo mesmo item que o seletor mostra como ativo. Sem um escopo explícito os
  // endpoints de lista tratam "sem filtro" como todas as contas juntas.
  const { groups: scopeGroups, items: scopes } = useMemo(() => buildScopes(accounts, connectors), [accounts, connectors]);
  const effectiveScope = useMemo(() => {
    const connectionId = selectedScopeKey?.startsWith("conn:") ? selectedScopeKey.slice(5) : null;
    if (connectionId && !scopes.some((s) => s.connectionId === connectionId)
        && (connectionsStatus !== "ready" || connections.some((c) => c.id === connectionId))) return null;
    return resolveScope(scopes, selectedScopeKey);
  }, [scopes, selectedScopeKey, connections, connectionsStatus]);
  const scopeKey = effectiveScope?.key ?? "";
  const scopeAccountId = effectiveScope?.accountId ?? null;
  const defaultAccountId = useMemo(() => scopes.find((s) => s.isDefault)?.accountId ?? null, [scopes]);
  const scopeQuery = useMemo(() => (scopeAccountId ? { bankAccountId: scopeAccountId } : {}), [scopeAccountId]);

  // Account tabs retain their account scope. Investment products belong to a connection,
  // which may exist without any checking account. Both selections use the same stored key.
  const selectedConnectionId = selectedScopeKey?.startsWith("conn:") ? selectedScopeKey.slice(5)
    : selectedScopeKey === "connections:all" ? null : effectiveScope?.connectionId ?? null;
  const effectiveConnectionId = selectedConnectionId && (connections.some((c) => c.id === selectedConnectionId)
    || (connectionsStatus !== "ready" && accounts.some((a) => a.localIntegrationId === selectedConnectionId))) ? selectedConnectionId : null;
  const connectionScopes = useMemo(() => {
    const labels = new Map();
    for (const c of connections) {
      const name = c.institutionName || "Instituição conectada";
      labels.set(name, (labels.get(name) ?? 0) + 1);
    }
    return [{ key: "connections:all", name: "Todos os bancos", label: "Todos os bancos", kindLabel: "Investimentos", connectionId: null },
      ...connections.map((c) => {
        const name = c.institutionName || "Instituição conectada";
        const connector = connectors.find((item) => item.id === c.institutionId);
        const status = ({ EXPIRED: "Conexão expirada", REVOKED: "Conexão revogada", REJECTED: "Conexão recusada" })[c.connectionStatus];
        return { key: `conn:${c.id}`, connectionId: c.id, name, label: name,
          kindLabel: `${status || "Instituição"}${labels.get(name) > 1 ? ` · conexão …${c.id.slice(-6)}` : ""}`,
          color: connector?.color ? (connector.color.startsWith("#") ? connector.color : `#${connector.color}`) : null };
      }).sort((a, b) => a.name.localeCompare(b.name, "pt-BR"))];
  }, [connections, connectors]);
  const effectiveConnectionScope = useMemo(() => connectionScopes.find((s) => s.connectionId === effectiveConnectionId)
    ?? (effectiveConnectionId ? { key: `conn:${effectiveConnectionId}`, connectionId: effectiveConnectionId,
      name: "Instituição selecionada", label: "Instituição selecionada", kindLabel: "Instituição" } : connectionScopes[0]), [connectionScopes, effectiveConnectionId]);
  const connectionScopeGroups = useMemo(() => connectionScopes.map((s) => ({ key: s.key, connectionId: null, items: [s] })), [connectionScopes]);
  const setSelectedConnection = useCallback((key) => {
    if (key === "connections:all") { setSelectedScope(key); return; }
    const id = key.startsWith("conn:") ? key.slice(5) : null;
    if (!id || !connections.some((c) => c.id === id)) return;
    const account = scopes.find((s) => s.connectionId === id);
    setSelectedScope(account?.key ?? key);
  }, [connections, scopes, setSelectedScope]);

  const value = useMemo(
    () => ({
      accounts,
      connections,
      connectionsStatus,
      connectionsRefreshing,
      effectiveConnectionId,
      effectiveConnectionScope,
      connectionScopeGroups,
      setSelectedConnection,
      connectors,
      accountsStatus,
      scopeGroups,
      effectiveScope,
      scopeKey,
      scopeQuery,
      setSelectedScope,
      dataRefreshKey,
      bumpDataRefresh,
      disconnectConnection,
      removeManualAccount,
      defaultAccountId,
      changeDefaultAccount,
      notice,
      dismissNotice,
    }),
    [accounts, connections, connectionsStatus, connectionsRefreshing, effectiveConnectionId, effectiveConnectionScope, connectionScopeGroups, setSelectedConnection, connectors, accountsStatus, scopeGroups, effectiveScope, scopeKey, scopeQuery, setSelectedScope, dataRefreshKey, bumpDataRefresh, disconnectConnection, removeManualAccount, defaultAccountId, changeDefaultAccount, notice, dismissNotice],
  );

  return <AccountContext.Provider value={value}>{children}</AccountContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAccount() {
  return useContext(AccountContext);
}
