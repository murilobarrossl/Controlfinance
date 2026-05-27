import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import RegisterPage   from "./pages/register/register.jsx";
import LoginPage      from "./pages/login/LoginPage.jsx";
import WelcomePage    from "./pages/welcome/WelcomePage.jsx";
import OnboardingPage from "./pages/onboarding/OnboardingPage.jsx";
import Dashboard      from "./pages/dashboard/dashboard.jsx";
import ContasBancarias from "./pages/contas/ContasBancarias.jsx";

function PrivateRoute({ children }) {
  const token = localStorage.getItem("token");
  return token ? children : <Navigate to="/loginemail" replace />;
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/"              element={<Navigate to="/loginemail" replace />} />
        <Route path="/loginemail"    element={<LoginPage mode="email" />} />
        <Route path="/logincpf-cnpj" element={<LoginPage mode="cpf" />} />
        <Route path="/register"      element={<RegisterPage />} />
        <Route path="/welcome"       element={<PrivateRoute><WelcomePage /></PrivateRoute>} />
        <Route path="/onboarding"    element={<PrivateRoute><OnboardingPage /></PrivateRoute>} />
        <Route path="/dashboard"     element={<PrivateRoute><Dashboard /></PrivateRoute>} />
        <Route path="/contas"         element={<PrivateRoute><ContasBancarias /></PrivateRoute>} />
        <Route path="*"              element={<Navigate to="/loginemail" replace />} />
      </Routes>
    </BrowserRouter>
  );
}
