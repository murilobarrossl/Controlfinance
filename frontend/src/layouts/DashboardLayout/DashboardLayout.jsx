import { useEffect, useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useAuth } from "../../context/AuthContext.jsx";
import { logout as logoutRequest } from "../../api/auth.js";
import ErrorBoundary from "../../components/ErrorBoundary/ErrorBoundary.jsx";
import NavUserMenu from "../../components/dashboard/NavUserMenu/NavUserMenu.jsx";
import Avatar from "../../components/ui/Avatar/Avatar.jsx";
import { MenuIcon, CloseIcon } from "../../components/ui/icons/FeatureIcons.jsx";
import logo from "../../assets/images/control-finance-transparente-branco.svg";
import "./DashboardLayout.css";

const TABS = [
  { label: "Dashboard inteligente", to: "/dashboard", end: true },
  { label: "Receitas e despesas", to: "/dashboard/receitas-despesas" },
  { label: "Relatórios", to: "/dashboard/relatorios" },
  { label: "Categorias", to: "/dashboard/categorias" },
  { label: "Investimentos", to: "/dashboard/investimentos" },
  { label: "Orçamento", to: "/dashboard/orcamento" },
];

export default function DashboardLayout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [sidebarOpen, setSidebarOpen] = useState(false);

  useEffect(() => {
    if (!sidebarOpen) return;

    // Trava o scroll do conteúdo atrás da sidebar aberta e permite fechar com Esc.
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    function handleKeyDown(e) {
      if (e.key === "Escape") setSidebarOpen(false);
    }
    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [sidebarOpen]);

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
        <button
          type="button"
          className="dashboard-nav__toggle"
          onClick={() => setSidebarOpen(true)}
          aria-label="Abrir menu"
          aria-expanded={sidebarOpen}
        >
          <MenuIcon />
        </button>

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

        <NavUserMenu user={user} onLogout={handleLogout} />
      </header>

      <div
        className={`dashboard-sidebar-overlay ${sidebarOpen ? "dashboard-sidebar-overlay--visible" : ""}`}
        onClick={() => setSidebarOpen(false)}
        aria-hidden="true"
      />

      <aside className={`dashboard-sidebar ${sidebarOpen ? "dashboard-sidebar--open" : ""}`}>
        <div className="dashboard-sidebar__header">
          <img src={logo} alt="Control Finance" className="dashboard-sidebar__logo" />
          <button
            type="button"
            className="dashboard-sidebar__close"
            onClick={() => setSidebarOpen(false)}
            aria-label="Fechar menu"
          >
            <CloseIcon />
          </button>
        </div>

        <nav className="dashboard-sidebar__tabs">
          {TABS.map((tab) => (
            <NavLink
              key={tab.to}
              to={tab.to}
              end={tab.end}
              onClick={() => setSidebarOpen(false)}
              className={({ isActive }) =>
                `dashboard-sidebar__tab ${isActive ? "dashboard-sidebar__tab--active" : ""}`
              }
            >
              {tab.label}
            </NavLink>
          ))}
        </nav>

        <div className="dashboard-sidebar__profile">
          <Avatar name={user?.name} size="md" />
          <span className="dashboard-sidebar__profile-name">{user?.name || "Conta"}</span>
          {/* Único jeito de sair no mobile: o menu do avatar (com a opção "Sair") some da
              barra de topo aqui, então esse botão precisa cobrir a função dele. */}
          <button type="button" className="dashboard-sidebar__logout" onClick={handleLogout}>
            Sair
          </button>
        </div>
      </aside>

      <main className="dashboard-content">
        <ErrorBoundary>
          <Outlet />
        </ErrorBoundary>
      </main>
    </div>
  );
}
