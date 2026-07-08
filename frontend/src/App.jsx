import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import HomePage from "./pages/home/HomePage.jsx";
import LoginPage from "./pages/login/Login.jsx";
import RegisterPage from "./pages/cadastro/Cadastro.jsx";
import ConnectBankPage from "./pages/conectar-banco/ConectarBanco.jsx";

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/loginemail" element={<LoginPage mode="email" />} />
        <Route path="/logincpf-cnpj" element={<LoginPage mode="cpf" />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/conectar-banco" element={<ConnectBankPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}
