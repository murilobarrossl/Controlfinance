import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
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

  const applySession = useCallback((data) => {
    setSessionError(null);
    setCsrfToken(data?.csrfToken);
    setUser(data?.user ?? null);
    setCurrentUserId(data?.user?.id ?? null);
    setStatus(data?.user ? "authenticated" : "anonymous");
  }, []);

  useEffect(() => {
    let cancelled = false;
    getCurrentUser()
      .then((data) => {
        if (cancelled) return;
        applySession(data);
      })
      .catch((err) => {
        if (cancelled) return;
        setCsrfToken(null);
        if (err.status === 401) {
          setSessionError(null);
          setStatus("anonymous");
        } else {
          // A failed session lookup does not prove the user is logged out.
          const unavailable = !err.status || err.status >= 500 || [408, 429].includes(err.status);
          setSessionError(err.status === 403 ? 403 : unavailable ? 503 : 500);
          setStatus("error");
        }
      });

    return () => {
      cancelled = true;
    };
  }, [applySession]);

  // Chamado com a resposta de /auth/login ou /auth/register ({ user, csrfToken }); o cookie
  // de sessão já foi gravado pelo próprio navegador.
  const login = applySession;

  const updateUser = useCallback((nextUser) => {
    setUser(nextUser);
  }, []);

  const logout = useCallback(() => {
    clearSessionLocalData(getCurrentUserId());
    setSessionError(null);
    setCsrfToken(null);
    setUser(null);
    setStatus("anonymous");
  }, []);

  const value = useMemo(
    () => ({ user, status, sessionError, isAuthenticated: status === "authenticated", login, logout, updateUser }),
    [user, status, sessionError, login, logout, updateUser],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  return useContext(AuthContext);
}
