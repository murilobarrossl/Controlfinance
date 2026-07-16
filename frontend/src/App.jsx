import { lazy, Suspense } from "react";
import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { AuthProvider } from "./context/AuthContext.jsx";
import ProtectedRoute from "./routes/ProtectedRoute.jsx";
import usePolpConnectionWatcher from "./hooks/usePolpConnectionWatcher.js";
import HomePage from "./pages/home/HomePage.jsx";

// Só a home entra no bundle inicial: o resto (auth, dashboard, páginas legais) é code-split
// por rota, então quem só visita a landing page não baixa o dashboard inteiro.
const LoginPage = lazy(() => import("./pages/login/Login.jsx"));
const RegisterPage = lazy(() => import("./pages/cadastro/Cadastro.jsx"));
const ConnectBankPage = lazy(() => import("./pages/conectar-banco/ConectarBanco.jsx"));
const DashboardLayout = lazy(() => import("./layouts/DashboardLayout/DashboardLayout.jsx"));
const DashboardHome = lazy(() => import("./pages/dashboard/DashboardHome/DashboardHome.jsx"));
const ReceitasDespesas = lazy(() => import("./pages/dashboard/ReceitasDespesas/ReceitasDespesas.jsx"));
const Relatorios = lazy(() => import("./pages/dashboard/Relatorios/Relatorios.jsx"));
const Categorias = lazy(() => import("./pages/dashboard/Categorias/Categorias.jsx"));
const Investimentos = lazy(() => import("./pages/dashboard/Investimentos/Investimentos.jsx"));
const Orcamento = lazy(() => import("./pages/dashboard/Orcamento/Orcamento.jsx"));
const TermosDeUso = lazy(() => import("./pages/legal/TermosDeUso.jsx"));
const PoliticaDePrivacidade = lazy(() => import("./pages/legal/PoliticaDePrivacidade.jsx"));

function PolpConnectionWatcher() {
  usePolpConnectionWatcher();
  return null;
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <PolpConnectionWatcher />
        <Suspense fallback={null}>
          <Routes>
            <Route path="/" element={<HomePage />} />
            <Route path="/loginemail" element={<LoginPage mode="email" />} />
            <Route path="/logincpf-cnpj" element={<LoginPage mode="cpf" />} />
            <Route path="/register" element={<RegisterPage />} />
            <Route path="/conectar-banco" element={<ConnectBankPage />} />
            <Route path="/termos-de-uso" element={<TermosDeUso />} />
            <Route path="/politica-de-privacidade" element={<PoliticaDePrivacidade />} />
            <Route
              path="/dashboard"
              element={
                <ProtectedRoute>
                  <DashboardLayout />
                </ProtectedRoute>
              }
            >
              <Route index element={<DashboardHome />} />
              <Route path="receitas-despesas" element={<ReceitasDespesas />} />
              <Route path="relatorios" element={<Relatorios />} />
              <Route path="categorias" element={<Categorias />} />
              <Route path="investimentos" element={<Investimentos />} />
              <Route path="orcamento" element={<Orcamento />} />
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </Suspense>
      </BrowserRouter>
    </AuthProvider>
  );
}
