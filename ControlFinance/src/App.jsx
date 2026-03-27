import { BrowserRouter, Routes, Route } from 'react-router-dom';
import RegisterPage from './pages/register/register.jsx';

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/register" element={<RegisterPage />} />
      </Routes>
    </BrowserRouter>
  );
}
