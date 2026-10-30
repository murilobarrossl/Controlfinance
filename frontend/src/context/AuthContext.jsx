import { createContext, useContext, useEffect, useState } from "react";
import { getCurrentUser } from "../api/auth.js";
import { setCsrfToken } from "../api/client.js";
import { clearTransactionsCache } from "../api/transactions.js";
import { setCurrentUserId } from "../utils/authToken.js";

const AuthContext = createContext(null);

// A sessão é um cookie httpOnly que este código não consegue ler: a única forma de saber se
// existe sessão é perguntar ao backend (GET /auth/me) ao abrir a página. Enquanto essa resposta
// não chega, status fica "loading" e as rotas protegidas esperam em vez de mandar pro login.
export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [status, setStatus] = useState("loading"); // "loading" | "authenticated" | "anonymous"

  useEffect(() => {
    let cancelled = false;
    getCurrentUser()
      .then((data) => {
        if (cancelled) return;
        applySession(data);
      })
      .catch(() => {
        if (cancelled) return;
        setCsrfToken(null);
        setStatus("anonymous");
      });

    return () => {
      cancelled = true;
    };
  }, []);

  function applySession(data) {
    setCsrfToken(data?.csrfToken);
    setUser(data?.user ?? null);
    setCurrentUserId(data?.user?.id ?? null);
    setStatus(data?.user ? "authenticated" : "anonymous");
  }

  // Chamado com a resposta de /auth/login ou /auth/register ({ user, csrfToken }); o cookie
  // de sessão já foi gravado pelo próprio navegador.
  function login(data) {
    applySession(data);
  }

  function updateUser(nextUser) {
    setUser(nextUser);
  }

  function logout() {
    setCsrfToken(null);
    clearTransactionsCache();
    setUser(null);
    setStatus("anonymous");
  }

  return (
    <AuthContext.Provider
      value={{
        user,
        status,
        isAuthenticated: status === "authenticated",
        login,
        logout,
        updateUser,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  return useContext(AuthContext);
}
