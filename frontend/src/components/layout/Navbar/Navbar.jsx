import { Link } from "react-router-dom";
import Button from "../../ui/Button/Button.jsx";
import logo from "../../../assets/images/control-finance-transparente-branco.svg";
import "./Navbar.css";

const NAV_LINKS = [
  { label: "Sobre Nós", href: "#sobre-nos" },
  { label: "Time", href: "#time" },
  { label: "Funcionalidades", href: "#funcionalidades" },
  { label: "Planos", href: "#planos" },
  { label: "Agente de IA", href: "#agente-ia" },
  { label: "Tire suas dúvidas", href: "#faq" },
];

export default function Navbar() {
  return (
    <header className="navbar">
      <Link to="/" className="navbar__logo">
        <img src={logo} alt="Control Finance" />
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

      <Link to="/loginemail" className="navbar__login">
        Entrar na sua conta
      </Link>

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
