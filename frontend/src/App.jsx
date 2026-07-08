import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { AuthProvider } from "./context/AuthContext.jsx";
import ProtectedRoute from "./routes/ProtectedRoute.jsx";
import usePolpConnectionWatcher from "./hooks/usePolpConnectionWatcher.js";
import HomePage from "./pages/home/HomePage.jsx";
import LoginPage from "./pages/login/Login.jsx";
import RegisterPage from "./pages/cadastro/Cadastro.jsx";
import ConnectBankPage from "./pages/conectar-banco/ConectarBanco.jsx";
import DashboardLayout from "./layouts/DashboardLayout/DashboardLayout.jsx";
import DashboardHome from "./pages/dashboard/DashboardHome/DashboardHome.jsx";
import ReceitasDespesas from "./pages/dashboard/ReceitasDespesas/ReceitasDespesas.jsx";
import Relatorios from "./pages/dashboard/Relatorios/Relatorios.jsx";
import Categorias from "./pages/dashboard/Categorias/Categorias.jsx";
import Investimentos from "./pages/dashboard/Investimentos/Investimentos.jsx";

function PolpConnectionWatcher() {
  usePolpConnectionWatcher();
  return null;
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <PolpConnectionWatcher />
        <Routes>
          <Route path="/" element={<HomePage />} />
          <Route path="/loginemail" element={<LoginPage mode="email" />} />
          <Route path="/logincpf-cnpj" element={<LoginPage mode="cpf" />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/conectar-banco" element={<ConnectBankPage />} />
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
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
