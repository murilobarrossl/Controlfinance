import { createContext, useContext, useEffect, useState } from "react";
import { getCurrentUser } from "../api/auth.js";

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  // O cookie de sessão é httpOnly (invisível pro JS de propósito), então não dá mais pra saber
  // se tem sessão só olhando o navegador: precisa perguntar pro backend. Enquanto isso não
  // resolve, checkingAuth fica true.
  const [checkingAuth, setCheckingAuth] = useState(true);

  // Roda sempre ao montar, não só quando "tem token salvo" (isso não existe mais do lado do
  // cliente): cobre tanto quem acabou de logar (o cookie já está lá, essa chamada só confirma)
  // quanto quem já tinha sessão aberta antes (refresh de página).
  useEffect(() => {
    let cancelled = false;

    getCurrentUser()
      .then((data) => {
        if (!cancelled) setUser(data);
      })
      .catch(() => {
        // sem sessão válida, a rota protegida cuida de redirecionar pro login
      })
      .finally(() => {
        if (!cancelled) setCheckingAuth(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  // Recebe o usuário direto da resposta de login/cadastro (o cookie de sessão já foi setado
  // pelo backend na mesma resposta): evita uma chamada extra pra /auth/me só pra confirmar
  // algo que acabou de ser confirmado.
  function login(userData) {
    setUser(userData);
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
    <AuthContext.Provider value={{ user, isAuthenticated: !!user, checkingAuth, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  return useContext(AuthContext);
}
