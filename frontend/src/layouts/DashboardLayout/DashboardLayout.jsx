import { useEffect, useRef, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router";
import { useAuth } from "../../context/AuthContext.jsx";
import { AccountProvider, useAccount } from "../../context/AccountContext.jsx";
import { DashboardHeaderSlotContext } from "../../context/DashboardHeaderContext.jsx";
import { logout as logoutRequest } from "../../api/auth.js";
import ErrorBoundary from "../../components/ErrorBoundary/ErrorBoundary.jsx";
import NavUserMenu from "../../components/dashboard/NavUserMenu/NavUserMenu.jsx";
import AccountSwitcher from "../../components/dashboard/AccountSwitcher/AccountSwitcher.jsx";
import BalanceSyncButton from "../../components/dashboard/BalanceSyncButton/BalanceSyncButton.jsx";
import Skeleton from "../../components/ui/Skeleton/Skeleton.jsx";
import Avatar from "../../components/ui/Avatar/Avatar.jsx";
import { MenuIcon, CloseIcon } from "../../components/ui/icons/FeatureIcons.jsx";
import logo from "../../assets/images/cflogobranco.svg";
import "./DashboardLayout.css";

const TABS = [
  { label: "Dashboard inteligente", to: "/dashboard", end: true },
  { label: "Cartões", to: "/dashboard/cartoes" },
  { label: "Receitas e despesas", to: "/dashboard/receitas-despesas" },
  { label: "Relatórios", to: "/dashboard/relatorios" },
  { label: "Categorias", to: "/dashboard/categorias" },
  { label: "Investimentos", to: "/dashboard/investimentos" },
];

const ACCOUNT_SCOPED_PATHS = new Set(["/dashboard", "/dashboard/receitas-despesas", "/dashboard/relatorios", "/dashboard/categorias"]);
const CARDS_PATH = "/dashboard/cartoes";

export default function DashboardLayout() {
  return (
    <AccountProvider>
      <DashboardLayoutContent />
    </AccountProvider>
  );
}

function DashboardLayoutContent() {
  const { user, logout } = useAuth();
  const {
    accounts,
    connectors,
    accountsStatus,
    effectiveAccountId,
    setSelectedAccountId,
    disconnectConnection,
    removeManualAccount,
    notice,
    dismissNotice,
  } = useAccount();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [headerSlot, setHeaderSlot] = useState(null);
  const contentRef = useRef(null);
  const toolbarRef = useRef(null);

  const path = pathname.length > 1 ? pathname.replace(/\/+$/, "") : pathname;
  const isCardsTab = path === CARDS_PATH;
  const showToolbar = isCardsTab || ACCOUNT_SCOPED_PATHS.has(path);

  // No desktop a barra fica na linha do título da aba, sobreposta ao topo da página; a largura
  // real dela (que muda com o nome da conta ou o seletor de Cartões) vira o espaço reservado ao
  // lado do título. Escrito direto no estilo, sem estado, pra não re-renderizar o painel.
  useEffect(() => {
    const content = contentRef.current;
    const toolbar = toolbarRef.current;
    if (!content) return undefined;
    if (!toolbar) {
      content.style.removeProperty("--dash-toolbar-space");
      return undefined;
    }
    const observer = new ResizeObserver(() => {
      const width = toolbar.offsetWidth;
      content.style.setProperty("--dash-toolbar-space", width > 0 ? `calc(${width}px + 1.5rem)` : "0px");
    });
    observer.observe(toolbar);
    return () => observer.disconnect();
  }, [showToolbar]);

  useEffect(() => {
    const prefetch = () => {
      import("../../components/charts/AreaTrendChart.jsx");
      import("../../components/charts/CategoryDonutChart.jsx");
      import("../../pages/dashboard/DashboardHome/DashboardHome.jsx");
      import("../../pages/dashboard/Cartoes/Cartoes.jsx");
      import("../../pages/dashboard/ReceitasDespesas/ReceitasDespesas.jsx");
      import("../../pages/dashboard/Relatorios/Relatorios.jsx");
      import("../../pages/dashboard/Categorias/Categorias.jsx");
      import("../../pages/dashboard/Investimentos/Investimentos.jsx");
      import("../../pages/dashboard/Privacidade/Privacidade.jsx");
    };
    if ("requestIdleCallback" in window) {
      const id = requestIdleCallback(prefetch);
      return () => cancelIdleCallback(id);
    }
    const id = setTimeout(prefetch, 1500);
    return () => clearTimeout(id);
  }, []);

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

        <nav className="dashboard-sidebar__tabs" aria-label="Seções do painel">
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
          <NavLink
            to="/dashboard/privacidade"
            onClick={() => setSidebarOpen(false)}
            className="dashboard-sidebar__privacy-link"
          >
            Privacidade e dados
          </NavLink>
          {/* Único jeito de sair no mobile: o menu do avatar (com a opção "Sair") some da
              barra de topo aqui, então esse botão precisa cobrir a função dele. */}
          <button type="button" className="dashboard-sidebar__logout" onClick={handleLogout}>
            Sair
          </button>
        </div>
      </aside>

      <main ref={contentRef} className="dashboard-content">
        {showToolbar && (
          <div ref={toolbarRef} className="dashboard-toolbar">
            {isCardsTab ? (
              <div ref={setHeaderSlot} className="dashboard-toolbar__slot" />
            ) : accountsStatus === "loading" && accounts.length === 0 ? (
              <Skeleton width="12rem" height="2.5rem" />
            ) : (
              <AccountSwitcher
                accounts={accounts}
                activeAccountId={effectiveAccountId}
                connectors={connectors}
                onSelect={setSelectedAccountId}
                onDisconnect={disconnectConnection}
                onRemoveManual={removeManualAccount}
              />
            )}
            <BalanceSyncButton />
          </div>
        )}
        {notice && (
          <div className={`dashboard-notice dashboard-notice--${notice.tone}`} role="status">
            <p>{notice.text}</p>
            <button type="button" onClick={dismissNotice} aria-label="Fechar aviso">
              ×
            </button>
          </div>
        )}
        <DashboardHeaderSlotContext.Provider value={isCardsTab ? headerSlot : null}>
          <ErrorBoundary>
            <Outlet />
          </ErrorBoundary>
        </DashboardHeaderSlotContext.Provider>
      </main>
    </div>
  );
}
