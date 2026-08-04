import { Link } from "react-router";
import logo from "../../../assets/images/control-finance-transparente-branco.svg";
import { ShieldIcon } from "../../ui/icons/FeatureIcons.jsx";
import "./Footer.css";

const PRODUTO_LINKS = [
  { label: "Funcionalidades", href: "#funcionalidades" },
  { label: "Planos", href: "#planos" },
  { label: "Agente de IA", href: "#agente-ia" },
  { label: "Sincronização bancária", href: "#funcionalidades" },
];

const EMPRESA_LINKS = [
  { label: "Sobre nós", href: "#sobre-nos" },
  { label: "Time", href: "#time" },
  { label: "Dúvidas frequentes", href: "#faq" },
];

export default function Footer() {
  const ano = new Date().getFullYear();

  return (
    <footer className="footer">
      <div className="footer__top">
        <div className="footer__brand">
          <img src={logo} alt="Control Finance" className="footer__logo" />
          <p className="footer__tagline">
            Inteligência financeira para pessoa física e pequenos negócios,
            com sincronização bancária via Open Finance.
          </p>
          <p className="footer__security">
            <ShieldIcon />
            Estrutura regulamentada pelo Banco Central do Brasil
          </p>
        </div>

        <div className="footer__col">
          <span className="footer__col-title">Produto</span>
          {PRODUTO_LINKS.map((link) => (
            <a key={link.label} href={link.href} className="footer__link">
              {link.label}
            </a>
          ))}
        </div>

        <div className="footer__col">
          <span className="footer__col-title">Empresa</span>
          {EMPRESA_LINKS.map((link) => (
            <a key={link.label} href={link.href} className="footer__link">
              {link.label}
            </a>
          ))}
        </div>

        <div className="footer__col">
          <span className="footer__col-title">Legal</span>
          <Link to="/termos-de-uso" className="footer__link">Termos de uso</Link>
          <Link to="/politica-de-privacidade" className="footer__link">Política de privacidade</Link>
        </div>

        <div className="footer__col">
          <span className="footer__col-title">Contato</span>
          <a href="mailto:suporte@controlfinance.app.br" className="footer__link">
            suporte@controlfinance.app.br
          </a>
        </div>
      </div>

      <div className="footer__legal">
        <p className="footer__legal-text">
          Control Finance · CNPJ [preencher antes de publicar] · Empresa 100% online
        </p>
        <p className="footer__legal-text">
          As informações desta plataforma têm caráter informativo e não constituem
          recomendação de investimento. A sincronização de contas ocorre exclusivamente
          via Open Finance, mediante autorização do usuário, podendo ser revogada a
          qualquer momento.
        </p>
      </div>

      <div className="footer__bottom">
        <span>© {ano} Control Finance. Todos os direitos reservados.</span>
      </div>
    </footer>
  );
}
