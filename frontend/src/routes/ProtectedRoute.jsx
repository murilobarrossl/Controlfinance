import { Navigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext.jsx";

// Estilo inline em vez de uma classe: esse componente roda antes de qualquer página lazy
// montar, então o CSS scoped de página ainda não carregou nesse ponto. index.css (com as
// variáveis de tema) é importado de forma eager em main.jsx, esse sim já está disponível.
const LOADING_STYLE = {
  minHeight: "100vh",
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
  backgroundColor: "var(--color-bg)",
  color: "var(--color-text-secondary)",
  fontFamily: "Prompt, sans-serif",
  fontSize: "0.9rem",
};

export default function ProtectedRoute({ children }) {
  const { isAuthenticated, checkingAuth } = useAuth();

  if (checkingAuth) {
    return <div style={LOADING_STYLE}>Carregando...</div>;
  }

  if (!isAuthenticated) {
    return <Navigate to="/loginemail" replace />;
  }

  return children;
}
