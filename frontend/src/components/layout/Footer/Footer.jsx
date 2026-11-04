import { Link } from "react-router";
import logo from "../../../assets/images/cflogobranco.svg";
import "./Footer.css";

const PRODUTO_LINKS = [
  { label: "Funcionalidades", href: "/#funcionalidades" },
  { label: "Planos", href: "/#planos" },
  { label: "Sincronização bancária", href: "/#funcionalidades" },
];

const ANO_LANCAMENTO = 2026;

const EMPRESA_LINKS = [
  { label: "Sobre nós", href: "/#sobre-nos" },
  { label: "Time", href: "#time" },
  { label: "Dúvidas frequentes", href: "/#faq" },
];

export default function Footer() {
  const ano = new Date().getFullYear();
  // Ano de lançamento fixo + ano corrente, pra o aviso não ficar desatualizado a cada virada de ano.
  const periodo = ano > ANO_LANCAMENTO ? `${ANO_LANCAMENTO}–${ano}` : `${ANO_LANCAMENTO}`;

  return (
    <footer className="footer">
      <div className="footer__top">
        <div className="footer__brand">
          <img src={logo} alt="Control Finance" className="footer__logo" />
          <p className="footer__tagline">
            Organização financeira para pessoa física e pequenos negócios,
            com conexão bancária via Open Finance.
          </p>
          <p className="footer__security">
            O Control Finance não é instituição financeira. A conexão com bancos usa o
            Open Finance (regulado pelo Banco Central), por meio de fornecedor parceiro.
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
          {/* PENDENTE (empresa): razão social, CNPJ, endereço físico e encarregado/canal de dados
              ainda não foram informados. Exigidos pelo Decreto 7.962/2013 (art. 2º) e pela
              LGPD (art. 9º e art. 41). Não inventar. */}
          Control Finance · Razão social e CNPJ: [PENDENTE] · Endereço: [PENDENTE] · Empresa 100% online
        </p>
        <p className="footer__legal-text">
          As informações desta plataforma têm caráter informativo e não constituem
          recomendação de investimento. Saldos e extratos exibidos são os informados pelo
          banco na última sincronização. A conexão bancária ocorre via Open Finance, mediante
          autorização do usuário, e pode ser desfeita a qualquer momento no painel.
        </p>
      </div>

      <div className="footer__bottom">
        <span>© {periodo} Control Finance. Todos os direitos reservados.</span>
        <span>
          É proibida a reprodução total ou parcial do conteúdo, marca e identidade visual desta
          plataforma sem autorização prévia (Lei nº 9.610/1998).
        </span>
      </div>
    </footer>
  );
}
