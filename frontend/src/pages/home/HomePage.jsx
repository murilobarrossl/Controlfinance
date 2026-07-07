import Navbar from "../../components/layout/Navbar/Navbar.jsx";
import Hero from "./sections/Hero/Hero.jsx";
import AgenteIA from "./sections/AgenteIA/AgenteIA.jsx";
import FAQ from "./sections/FAQ/FAQ.jsx";
import Funcionalidades from "./sections/Funcionalidades/Funcionalidades.jsx";
import SobreNos from "./sections/SobreNos/SobreNos.jsx";
import Time from "./sections/Time/Time.jsx";
import Planos from "./sections/Planos/Planos.jsx";


export default function HomePage() {
  return (
    <>
      <Navbar />
      <Hero />
      <AgenteIA />
      <FAQ /> 
      <Funcionalidades />
      <SobreNos />
      <Time />
      <Planos />
    </>
  );
}
