import { useAuth } from "../context/AuthContext.jsx";
import { isBusinessHolder } from "../utils/holder.js";
import ErrorPage from "../pages/errors/ErrorPage.jsx";

// Roda dentro do ProtectedRoute do painel, então a sessão já foi resolvida aqui. Esconder a aba não
// basta: a URL digitada direto também precisa cair no 404 para o outro tipo de titular.
export function BusinessOnlyRoute({ children }) {
  const { user } = useAuth();
  return isBusinessHolder(user) ? children : <ErrorPage code={404} embedded />;
}

// Tipo desconhecido segue a regra de holder.js: é tratado como pessoa física.
export function PersonalOnlyRoute({ children }) {
  const { user } = useAuth();
  return isBusinessHolder(user) ? <ErrorPage code={404} embedded /> : children;
}

export function HolderSwitch({ personal, business }) {
  const { user } = useAuth();
  return isBusinessHolder(user) ? business : personal;
}
