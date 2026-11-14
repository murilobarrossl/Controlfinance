import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { getCurrentUser } from "../api/auth.js";
import { setCsrfToken } from "../api/client.js";
import { clearSessionLocalData, getCurrentUserId, setCurrentUserId } from "../utils/authToken.js";

const AuthContext = createContext(null);

// A sessão é um cookie httpOnly que este código não consegue ler: a única forma de saber se
// existe sessão é perguntar ao backend (GET /auth/me) ao abrir a página. Enquanto essa resposta
// não chega, status fica "loading" e as rotas protegidas esperam em vez de mandar pro login.
export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [status, setStatus] = useState("loading"); // loading | authenticated | anonymous | error
  const [sessionError, setSessionError] = useState(null);

  const sessionVersion = useRef(0);
  const pendingCheck = useRef(null);

  const applySession = useCallback((data) => {
    sessionVersion.current += 1;
    setSessionError(null);
    setCsrfToken(data?.csrfToken);
    setUser(data?.user ?? null);
    setCurrentUserId(data?.user?.id ?? null);
    setStatus(data?.user ? "authenticated" : "anonymous");
  }, []);

  // Uma falha de transporte não prova ausência de sessão. Reutilizamos a consulta
  // em andamento e ignoramos respostas anteriores a um login/logout explícito.
  const checkSession = useCallback(() => {
    if (pendingCheck.current) return pendingCheck.current;
    const version = sessionVersion.current;
    const request = getCurrentUser()
      .then((data) => {
        if (version !== sessionVersion.current) return;
        applySession(data);
      })
      .catch((err) => {
        if (version !== sessionVersion.current) return;
        if (err.status === 401) {
          applySession(null);
        } else {
          const unavailable = !err.status || err.status >= 500 || [408, 429].includes(err.status);
          setSessionError(err.status === 403 ? 403 : unavailable ? 503 : 500);
          setStatus((previous) => previous === "authenticated" ? previous : "error");
        }
      })
      .finally(() => {
        if (pendingCheck.current === request) pendingCheck.current = null;
      });
    pendingCheck.current = request;
    return request;
  }, [applySession]);

  useEffect(() => {
    checkSession();
  }, [checkSession]);

  const retrySession = useCallback(() => {
    setStatus((previous) => previous === "authenticated" ? previous : "loading");
    return checkSession();
  }, [checkSession]);

  // Chamado com a resposta de /auth/login ou /auth/register ({ user, csrfToken }); o cookie
  // de sessão já foi gravado pelo próprio navegador.
  const login = applySession;

  const updateUser = useCallback((nextUser) => {
    setUser(nextUser);
  }, []);

  const logout = useCallback(() => {
    sessionVersion.current += 1;
    clearSessionLocalData(getCurrentUserId());
    setSessionError(null);
    setCsrfToken(null);
    setUser(null);
    setStatus("anonymous");
  }, []);

  const value = useMemo(
    () => ({ user, status, sessionError, isAuthenticated: status === "authenticated", login, logout, updateUser, retrySession }),
    [user, status, sessionError, login, logout, updateUser, retrySession],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  return useContext(AuthContext);
}
