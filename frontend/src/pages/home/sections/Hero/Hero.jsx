import { Link } from "react-router-dom";
import Button from "../../../../components/ui/Button/Button.jsx";
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
        <h1 className="hero__title">
          Inteligência financeira
          <br />
          no seu bolso
        </h1>

        <p className="hero__subtitle">
          Organize suas finanças, simule investimentos
          <br />
          e planeje seu futuro — tudo em um só lugar.
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
