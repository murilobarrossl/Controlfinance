import { Navigate } from "react-router";
import { useAuth } from "../context/AuthContext.jsx";

export default function ProtectedRoute({ children }) {
  const { status } = useAuth();

  // Ainda perguntando ao backend se existe sessão (ver AuthContext): não decide nada antes disso.
  if (status === "loading") return null;

  if (status !== "authenticated") {
    return <Navigate to="/loginemail" replace />;
  }

  return children;
}
