import SectionHeading from "../../../../components/ui/SectionHeading/SectionHeading.jsx";
import "./SobreNos.css";

const DIFERENCIAIS = [
  "Agente de IA financeiro integrado",
  "Sincronização bancária em tempo real",
  "Preço acessível comparado aos concorrentes",
  "Tudo em um só lugar, PF e PJ",
];

export default function SobreNos() {
  return (
    <section id="sobre-nos" className="sobre-nos">
      <SectionHeading kicker="Quem somos" title="Sobre nós" />

      <div className="sobre-nos__grid">
        <div className="sobre-nos__narrative">
          <div className="sobre-nos__block">
            <h3 className="sobre-nos__subtitle">Nossa história</h3>
            <p className="sobre-nos__text">
              70% das famílias brasileiras estão endividadas. A Control Finance existe
              para mudar essa estatística. Isso acontece não por falta de esforço, mas por
              falta de acesso a ferramentas financeiras inteligentes e acessíveis.
              Aplicativos complexos, preços altos e soluções fragmentadas sempre foram a
              realidade de quem tentava organizar as finanças, seja pessoa física ou
              pequeno empresário.
            </p>
            <p className="sobre-nos__text">
              Foi dessa realidade que nasceu o Control Finance. Uma
              plataforma criada para mudar esse cenário, colocando na mão de
              qualquer brasileiro o mesmo nível de inteligência financeira
              que antes só grandes investidores tinham acesso.
            </p>
          </div>

          <div className="sobre-nos__block">
            <h3 className="sobre-nos__subtitle">Nossa missão</h3>
            <p className="sobre-nos__text">
              Democratizar o acesso à inteligência financeira. Queremos que
              qualquer pessoa (independente de quanto ganha ou quanto sabe
              sobre finanças) consiga organizar, planejar e crescer
              financeiramente com o apoio da tecnologia.
            </p>
          </div>
        </div>

        <aside className="sobre-nos__panel">
          <h4 className="sobre-nos__panel-title">Nossos diferenciais</h4>
          <ul className="sobre-nos__list">
            {DIFERENCIAIS.map((item) => (
              <li key={item} className="sobre-nos__list-item">
                <span className="sobre-nos__list-dot" />
                {item}
              </li>
            ))}
          </ul>
        </aside>
      </div>
    </section>
  );
}
