import { createContext, useContext, useEffect, useState } from "react";
import { getCurrentUser } from "../api/auth.js";

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [token, setToken] = useState(() => localStorage.getItem("token"));
  const [user, setUser] = useState(null);

  // Busca os dados do usuário sempre que existir uma sessão — funciona tanto pra quem
  // acabou de logar quanto pra uma sessão já aberta antes (refresh de página, por exemplo),
  // já que não depende de nada retornado no momento do login/cadastro.
  useEffect(() => {
    if (!token) return;

    let cancelled = false;
    getCurrentUser()
      .then((data) => {
        if (!cancelled) setUser(data);
      })
      .catch(() => {
        // token inválido/expirado — a rota protegida cuida de redirecionar pro login
      });

    return () => {
      cancelled = true;
    };
  }, [token]);

  function login(newToken) {
    localStorage.setItem("token", newToken);
    setToken(newToken);
  }

  function logout() {
    localStorage.removeItem("token");
    setToken(null);
    setUser(null);
  }

  return (
    <AuthContext.Provider value={{ token, user, isAuthenticated: !!token, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  return useContext(AuthContext);
}
