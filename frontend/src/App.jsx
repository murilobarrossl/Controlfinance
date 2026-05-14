import { BrowserRouter, Routes, Route } from 'react-router-dom';
import RegisterPage   from './pages/register/register.jsx';
import LoginPage      from './pages/login/LoginPage.jsx';
import WelcomePage    from './pages/welcome/WelcomePage.jsx';
import OnboardingPage from './pages/onboarding/OnboardingPage.jsx';
import Dashboard      from './pages/dashboard/dashboard.jsx';

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/register"    element={<RegisterPage />} />
        <Route path="/loginemail"  element={<LoginPage mode="email" />} />
        <Route path="/logincpf-cnpj" element={<LoginPage mode="cpf" />} />
        <Route path="/welcome"     element={<WelcomePage />} />
        <Route path="/onboarding"  element={<OnboardingPage />} />
        <Route path="/dashboard"   element={<Dashboard />} />
      </Routes>
    </BrowserRouter>
  );
}
