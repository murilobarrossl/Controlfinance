import { Link } from "react-router";
import Button from "../../../../components/ui/Button/Button.jsx";
import { ShieldIcon } from "../../../../components/ui/icons/FeatureIcons.jsx";
import heroBg from "../../../../assets/images/hero-bg.webp";
import "./Hero.css";

export default function Hero() {
  return (
    <section
      className="hero"
      style={{ backgroundImage: `url(${heroBg})` }}
    >
      <div className="hero__overlay" />

      <div className="hero__content">
        <span className="hero__kicker">Control Finance</span>

        <h1 className="hero__title">
          O gestor financeiro
          <br />
          conectado ao seu banco
        </h1>

        <p className="hero__subtitle">
          Você entra com o login do seu banco, via Open Finance regulamentado
          pelo Banco Central, e organiza tudo em um só lugar.
        </p>

        <p className="hero__trust">
          <ShieldIcon />
          Conexão criptografada de ponta a ponta. Nunca vemos nem guardamos seus dados.
        </p>

        <div className="hero__actions">
          <Button as={Link} to="/register" variant="primary" className="hero__cta">
            Abrir conta
          </Button>
          <Button as="a" href="#planos" variant="secondary" className="hero__cta">
            Ver planos
          </Button>
        </div>
      </div>
    </section>
  );
}
