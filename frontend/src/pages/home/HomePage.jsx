import Navbar from "../../components/layout/Navbar/Navbar.jsx";
import Footer from "../../components/layout/Footer/Footer.jsx";
import Hero from "./sections/Hero/Hero.jsx";
import FAQ from "./sections/FAQ/FAQ.jsx";
import Funcionalidades from "./sections/Funcionalidades/Funcionalidades.jsx";
import SobreNos from "./sections/SobreNos/SobreNos.jsx";
import Planos from "./sections/Planos/Planos.jsx";
import usePageTitle from "../../hooks/usePageTitle.js";

export default function HomePage() {
  usePageTitle();
  return (
    <>
      <a href="#conteudo" className="skip-link">
        Pular para o conteúdo
      </a>
      <Navbar />
      <main id="conteudo" tabIndex={-1}>
        <Hero />
        <Funcionalidades />
        <SobreNos />
        <Planos />
        <FAQ />
      </main>
      <Footer />
    </>
  );
}
