import SectionHeading from "../../../../components/ui/SectionHeading/SectionHeading.jsx";
import "./SobreNos.css";

// Só o que o produto entrega e dá pra demonstrar: "tempo real" saiu porque a sincronização é
// automática, quando há informações novas do banco, e não instantânea; a comparação de preço com
// concorrentes saiu porque não havia levantamento guardado que a sustentasse (CDC art. 36,
// parágrafo único).
const DIFERENCIAIS = [
  "Conexão com bancos pelo Open Finance, com sua autorização",
  "Coleta mínima: o CPF não fica guardado e e-mails não levam valores",
  "Tudo em um só lugar, PF e PJ",
];

export default function SobreNos() {
  return (
    <section id="sobre-nos" className="sobre-nos">
      <SectionHeading title="Sobre nós" />

      <div className="sobre-nos__grid">
        <div className="sobre-nos__narrative">
          <div className="sobre-nos__block">
            <h3 className="sobre-nos__subtitle">Nossa história</h3>
            <p className="sobre-nos__text">
              {/* PENDENTE: o dado de endividamento das famílias só deve voltar com fonte citada
                  (ex.: pesquisa e mês de referência), guardada pela empresa (CDC art. 36,
                  parágrafo único). */}
              Muitas famílias brasileiras vivem endividadas, e a Control Finance existe para
              ajudar a mudar isso. O problema raramente é falta de esforço: é falta de acesso a
              ferramentas financeiras simples e acessíveis.
              Aplicativos complexos, preços altos e soluções fragmentadas sempre foram a
              realidade de quem tentava organizar as finanças, seja pessoa física ou
              pequeno empresário.
            </p>
            <p className="sobre-nos__text">
              Foi dessa realidade que nasceu o Control Finance. Uma
              plataforma criada para mudar esse cenário, colocando na mão de
              qualquer brasileiro o mesmo nível de organização financeira
              que antes só grandes investidores tinham acesso.
            </p>
          </div>

          <div className="sobre-nos__block">
            <h3 className="sobre-nos__subtitle">Nossa missão</h3>
            <p className="sobre-nos__text">
              Democratizar o acesso à organização financeira. Queremos que
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
