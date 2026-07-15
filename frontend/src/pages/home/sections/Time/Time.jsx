import SectionHeading from "../../../../components/ui/SectionHeading/SectionHeading.jsx";
import "./Time.css";

const MEMBROS = [
  { nome: "Murilo Barros", cargo: "Fundador & Desenvolvedor" },
  { nome: "Igor Accioly Lins", cargo: "Cofundador & Desenvolvedor" },
];

export default function Time() {
  return (
    <section id="time" className="time">
      <SectionHeading title="Time" />

      <p className="time__text">
        Dois desenvolvedores. Uma ideia. Um produto construído do zero com o
        objetivo de mudar a forma como os brasileiros lidam com o dinheiro.
      </p>

      <ul className="time__list">
        {MEMBROS.map((membro) => (
          <li key={membro.nome} className="time__item">
            <span className="time__nome">{membro.nome}</span>, {membro.cargo}
          </li>
        ))}
      </ul>
    </section>
  );
}
