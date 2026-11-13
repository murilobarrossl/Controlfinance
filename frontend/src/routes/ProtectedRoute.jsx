import { Navigate } from "react-router";
import { useAuth } from "../context/AuthContext.jsx";
import ErrorPage, { PageLoading } from "../pages/errors/ErrorPage.jsx";

export default function ProtectedRoute({ children }) {
  const { status, sessionError } = useAuth();

  // Ainda perguntando ao backend se existe sessão (ver AuthContext): não decide nada antes disso.
  if (status === "loading") return <PageLoading />;
  if (status === "error") return <ErrorPage code={sessionError ?? 503} />;

  if (status !== "authenticated") {
    return <Navigate to="/loginemail" replace />;
  }

  return children;
}
