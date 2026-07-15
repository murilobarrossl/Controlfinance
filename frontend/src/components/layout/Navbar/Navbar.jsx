import { Link } from "react-router-dom";
import Button from "../../ui/Button/Button.jsx";
import logo from "../../../assets/images/control-finance-transparente-branco.svg";
import "./Navbar.css";

const NAV_LINKS = [
  { label: "Sobre Nós", href: "#sobre-nos" },
  { label: "Funcionalidades", href: "#funcionalidades" },
  { label: "Planos", href: "#planos" },
  { label: "Agente de IA", href: "#agente-ia" },
  { label: "Time", href: "#time" },
  { label: "Dúvidas", href: "#faq" },
];

export default function Navbar() {
  // Clicar na logo já estando na home não navega pra lugar nenhum (mesma rota): sem isso,
  // não tinha como voltar pro topo da página sem usar o scroll manual.
  function handleLogoClick() {
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  return (
    <header className="navbar">
      <Link to="/" className="navbar__logo" onClick={handleLogoClick}>
        <img src={logo} alt="Control Finance" />
      </Link>

      <div className="navbar__actions">
        <Button
          as={Link}
          to="/register"
          variant="primary"
          size="sm"
          className="navbar__cta"
        >
          Abrir conta
        </Button>

        <Link to="/loginemail" className="navbar__login">
          Entrar na sua conta
        </Link>
      </div>

      <nav className="navbar__links">
        {NAV_LINKS.map(({ label, href }) => (
          <a key={href} href={href} className="navbar__link">
            {label}
          </a>
        ))}
      </nav>
    </header>
  );
}
