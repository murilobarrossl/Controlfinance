import { BrowserRouter, Routes, Route } from 'react-router-dom';
import RegisterPage from './pages/register/register.jsx';
import LoginPage from './pages/login/login.jsx';

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/login" element={<LoginPage />} />
      </Routes>
    </BrowserRouter>
  );
}
