import { Link } from "react-router";
import Navbar from "../../components/layout/Navbar/Navbar.jsx";
import SectionHeading from "../../components/ui/SectionHeading/SectionHeading.jsx";
import "./LegalPage.css";

export default function LegalPageLayout({ kicker, title, updatedAt, children }) {
  return (
    <>
      <Navbar />
      <div className="legal-page__content">
        <SectionHeading kicker={kicker} title={title} align="left" />
        <p className="legal-page__updated">Última atualização: {updatedAt}</p>
        {children}
        <Link to="/register" className="legal-page__back">
          ← Voltar para o cadastro
        </Link>
      </div>
    </>
  );
}
