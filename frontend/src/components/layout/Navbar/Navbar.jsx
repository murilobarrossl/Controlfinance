import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import Button from "../../ui/Button/Button.jsx";
import { MenuIcon, CloseIcon } from "../../ui/icons/FeatureIcons.jsx";
import logo from "../../../assets/images/cflogobranco.svg";
import "./Navbar.css";

const NAV_LINKS = [
  { label: "Funcionalidades", href: "/#funcionalidades" },
  { label: "Sobre nós", href: "/#sobre-nos" },
  { label: "Planos", href: "/#planos" },
  { label: "Dúvidas", href: "/#faq" },
];

export default function Navbar() {
  const [menuOpen, setMenuOpen] = useState(false);
  const rootRef = useRef(null);
  const toggleRef = useRef(null);

  // Quatro links não cabem numa linha só em telas de celular: no mobile eles ficam atrás
  // desse botão, mesmo padrão de fechar ao clicar fora já usado no AccountSwitcher/NavUserMenu.
  useEffect(() => {
    function handleClickOutside(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) setMenuOpen(false);
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  useEffect(() => {
    if (!menuOpen) return undefined;
    function handleKeyDown(e) {
      if (e.key !== "Escape") return;
      setMenuOpen(false);
      toggleRef.current?.focus();
    }
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [menuOpen]);

  // Clicar na logo já estando na home não navega pra lugar nenhum (mesma rota): sem isso,
  // não tinha como voltar pro topo da página sem usar o scroll manual.
  function handleLogoClick() {
    window.scrollTo({ top: 0, behavior: "smooth" });
    setMenuOpen(false);
  }

  return (
    <header className="navbar" ref={rootRef}>
      <Link to="/" className="navbar__logo" onClick={handleLogoClick}>
        <img src={logo} alt="Control Finance" />
      </Link>

      <div className="navbar__actions">
        <Link to="/loginemail" className="navbar__login">
          <span className="navbar__login-full">Entrar na sua conta</span>
          <span className="navbar__login-short">Entrar</span>
        </Link>

        <Button
          as={Link}
          to="/register"
          variant="primary"
          size="sm"
          className="navbar__cta"
        >
          Abrir conta
        </Button>

        <button
          ref={toggleRef}
          type="button"
          className="navbar__toggle"
          onClick={() => setMenuOpen((prev) => !prev)}
          aria-expanded={menuOpen}
          aria-controls="navbar-links"
          aria-label={menuOpen ? "Fechar menu" : "Abrir menu"}
        >
          {menuOpen ? <CloseIcon /> : <MenuIcon />}
        </button>
      </div>

      <nav id="navbar-links" className={`navbar__links ${menuOpen ? "navbar__links--open" : ""}`}>
        {NAV_LINKS.map(({ label, href }) => (
          <a key={href} href={href} className="navbar__link" onClick={() => setMenuOpen(false)}>
            {label}
          </a>
        ))}
      </nav>
    </header>
  );
}
