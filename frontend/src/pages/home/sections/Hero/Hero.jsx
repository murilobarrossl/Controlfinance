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
          Você autoriza a conexão no próprio banco, pelo Open Finance, e organiza
          contas, cartões e gastos em um só lugar.
        </p>

        <p className="hero__trust">
          <ShieldIcon />
          A senha do seu banco não passa por nós. Guardamos só o necessário para o
          painel, cifrado, e você pode baixar ou apagar tudo quando quiser.
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
