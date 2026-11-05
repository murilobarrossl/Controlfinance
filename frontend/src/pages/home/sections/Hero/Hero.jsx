import { Link } from "react-router";
import Button from "../../../../components/ui/Button/Button.jsx";
import "./Hero.css";

export default function Hero() {
  return (
    <section className="hero">
      <div className="hero__overlay" />

      <div className="hero__content">
        <h1 className="hero__title">
          O gestor financeiro
          <br />
          conectado ao seu banco
        </h1>

        <p className="hero__subtitle">
          Você autoriza a conexão no próprio banco, pelo Open Finance, e organiza
          contas, cartões e gastos em um só lugar.
        </p>

        <div className="hero__actions">
          <Button as={Link} to="/register" variant="primary" className="hero__cta">
            Abrir conta
          </Button>
        </div>
      </div>
    </section>
  );
}
