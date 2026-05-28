import "./loginfooter.css";

export default function Footer() {
  return (
    <footer className="footer">
      <div className="footer-inner">
        <div className="footer-about">
          <p>
            O Control Finance é uma plataforma de gestão financeira desenvolvida
            para ajudar pessoas e empresas a terem controle total sobre suas
            finanças. Acompanhe receitas, despesas, cartões de crédito e
            investimentos em um único lugar, com relatórios claros e uma
            experiência simples.
          </p>
        </div>
      </div>

      <div className="footer-bottom">
        <span>© {new Date().getFullYear()} Control Finance. Todos os direitos reservados.</span>
      </div>
    </footer>
  );
}
