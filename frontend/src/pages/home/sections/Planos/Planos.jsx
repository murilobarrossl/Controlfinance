import "./Planos.css";

export default function Planos() {
  return (
    <section id="planos" className="planos">
      <h2 className="planos__title">Planos</h2>

      <div className="planos__grid">
        {/* Mensal */}
        <div className="planos__card">
          <h3 className="planos__plan-name">Mensal</h3>
          <p className="planos__price">
            R$49<span className="planos__price-cents">,90</span>
            <span className="planos__price-period">/mês</span>
          </p>
          <p className="planos__billing-note">cobrado mensalmente</p>

          <ul className="planos__benefits">
            <li>✓ Relatórios e dashboards.</li>
            <li>✓ Agente de IA financeiro.</li>
            <li>✓ Planejamentos e metas.</li>
            <li>✓ Sincronização bancária.</li>
            <li>✓ Suporte a PF e PJ</li>
            <li>✓ Gestão de receitas e despesas.</li>
          </ul>

          <button className="planos__cta">Assine agora mensal</button>
        </div>

        {/* Trimestral - destaque */}
        <div className="planos__card planos__card--highlight">
          <span className="planos__badge">Mais popular</span>

          <h3 className="planos__plan-name">Trimestral</h3>
          <p className="planos__price">
            R$44<span className="planos__price-cents">,90</span>
            <span className="planos__price-period">/mês</span>
          </p>
          <p className="planos__billing-note">Cobrado R$134,70 a cada 3 meses</p>

          <p className="planos__benefits-title">
            Todos os benefícios do mensal e mais:
          </p>
          <ul className="planos__benefits">
            <li>✓ 10% de economia</li>
            <li>✓ suporte prioritário</li>
          </ul>

          <ul className="planos__extra-notes">
            <li>Cobrado R$134,70 a cada três meses</li>
            <li>Economia: 10% off</li>
          </ul>

          <button className="planos__cta">Assine agora trimestral</button>
        </div>

        {/* Anual */}
        <div className="planos__card">
          <h3 className="planos__plan-name">Anual</h3>
          <p className="planos__price">
            R$49<span className="planos__price-cents">,90</span>
            <span className="planos__price-period">/mês</span>
          </p>
          <p className="planos__billing-note">cobrado R$478,80/ano</p>

          <p className="planos__benefits-title">
            Todos os benefícios do trimestral e mais:
          </p>
          <ul className="planos__benefits">
            <li>
              ✓ 20% de economia{" "}
              <span className="planos__highlight-text">— 2 meses grátis</span>
            </li>
            <li>✓ suporte prioritário com respostas mais rápidas</li>
          </ul>

          <ul className="planos__extra-notes">
            <li>Economia: 20% off - 2 meses grátis</li>
          </ul>

          <button className="planos__cta">Assine agora anual</button>
        </div>
      </div>

      <p className="planos__footer-note">
        14 dias grátis em qualquer plano. Cancele quando quiser.
      </p>
      <a href="#beneficios-assinatura" className="planos__footer-link">
        clique aqui para entender os benefícios de assinar a control finance
      </a>
    </section>
  );
}
