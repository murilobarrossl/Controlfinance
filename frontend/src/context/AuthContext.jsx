import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";
import { getCurrentUser } from "../api/auth.js";

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  // O cookie de sessão é httpOnly (invisível pro JS de propósito), então não dá mais pra saber
  // se tem sessão só olhando o navegador: precisa perguntar pro backend. Enquanto isso não
  // resolve, checkingAuth fica true. Se nada nunca chamar ensureAuthChecked (ver abaixo), isso
  // fica true pra sempre, sem problema, porque nenhuma página pública lê esse valor.
  const [checkingAuth, setCheckingAuth] = useState(true);
  const hasCheckedRef = useRef(false);

  // Só dispara a checagem de sessão (GET /auth/me) quando algo realmente precisa saber se o
  // usuário está logado, hoje só o ProtectedRoute chama isso. Antes essa checagem rodava
  // incondicionalmente pra qualquer página (inclusive a home pública e a tela de login), o que
  // gerava uma requisição (e um 401 esperado, mas visível no DevTools) pra todo visitante
  // anônimo antes mesmo dele tentar entrar.
  const ensureAuthChecked = useCallback(() => {
    if (hasCheckedRef.current) return;
    hasCheckedRef.current = true;

    getCurrentUser()
      .then(setUser)
      .catch(() => {
        // sem sessão válida, a rota protegida cuida de redirecionar pro login
      })
      .finally(() => setCheckingAuth(false));
  }, []);

  // Recebe o usuário direto da resposta de login/cadastro (o cookie de sessão já foi setado
  // pelo backend na mesma resposta): evita uma chamada extra pra /auth/me só pra confirmar
  // algo que acabou de ser confirmado.
  function login(userData) {
    hasCheckedRef.current = true;
    setUser(userData);
    setCheckingAuth(false);
  }

  function logout() {
    // A revogação de verdade (limpar os cookies no backend) já é feita antes disso, pelo
    // fluxo de logout da tela: isso aqui só limpa o estado local.
    setUser(null);
  }

  // Mantém uma cópia não-sensível do id do usuário fora do React, pra utilitários que
  // segmentam localStorage por usuário (metas de investimento, reserva de emergência) e não
  // têm acesso ao contexto. Não é segredo, é só um identificador.
  useEffect(() => {
    if (user) {
      localStorage.setItem("cf_current_user_id", user.id);
    } else {
      localStorage.removeItem("cf_current_user_id");
    }
  }, [user]);

  return (
    <AuthContext.Provider
      value={{ user, isAuthenticated: !!user, checkingAuth, ensureAuthChecked, login, logout }}
    >
      {children}
    </AuthContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  return useContext(AuthContext);
}
