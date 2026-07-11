import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useAuth } from "../../context/AuthContext.jsx";
import { logout as logoutRequest } from "../../api/auth.js";
import ErrorBoundary from "../../components/ErrorBoundary/ErrorBoundary.jsx";
import logo from "../../assets/images/control-finance-transparente-branco.svg";
import "./DashboardLayout.css";

const TABS = [
  { label: "Dashboard inteligente", to: "/dashboard", end: true },
  { label: "Receitas e despesas", to: "/dashboard/receitas-despesas" },
  { label: "Relatórios", to: "/dashboard/relatorios" },
  { label: "Categorias", to: "/dashboard/categorias" },
  { label: "Investimentos", to: "/dashboard/investimentos" },
];

export default function DashboardLayout() {
  const { logout } = useAuth();
  const navigate = useNavigate();

  async function handleLogout() {
    try {
      await logoutRequest();
    } catch {
      // mesmo se a revogação falhar (ex: token já expirado), ainda limpamos a sessão local
    }
    logout();
    navigate("/loginemail", { replace: true });
  }

  return (
    <div className="dashboard-layout">
      <header className="dashboard-nav">
        <img src={logo} alt="Control Finance" className="dashboard-nav__logo" />

        <nav className="dashboard-nav__tabs">
          {TABS.map((tab) => (
            <NavLink
              key={tab.to}
              to={tab.to}
              end={tab.end}
              className={({ isActive }) =>
                `dashboard-nav__tab ${isActive ? "dashboard-nav__tab--active" : ""}`
              }
            >
              {tab.label}
            </NavLink>
          ))}
        </nav>

        <button type="button" className="dashboard-nav__logout" onClick={handleLogout}>
          Sair
        </button>
      </header>

      <main className="dashboard-content">
        <ErrorBoundary>
          <Outlet />
        </ErrorBoundary>
      </main>
    </div>
  );
}
