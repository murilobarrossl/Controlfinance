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
        <span className="hero__badge">Novo — Agente de IA financeiro</span>

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
            Comece grátis
          </Button>
          <Button as="a" href="#planos" variant="secondary" className="hero__cta">
            Ver planos
          </Button>
        </div>
      </div>

      <div className="hero__cards">
        <div className="hero__card">
          <span className="hero__card-title">AGENTE DE IA:</span>
          <p className="hero__card-text">
            planejamento financeiro personalizado com base na sua conta.
          </p>
          <a href="#agente-ia" className="hero__card-link">
            Conheça nosso agente →
          </a>
        </div>

        <div className="hero__card">
          <span className="hero__card-title">SINCRONIZAÇÃO BANCÁRIA:</span>
          <p className="hero__card-text">
            conecte seus bancos e veja tudo em tempo real.
          </p>
          <a href="#funcionalidades" className="hero__card-link">
            Saiba mais →
          </a>
        </div>
      </div>
    </section>
  );
}
