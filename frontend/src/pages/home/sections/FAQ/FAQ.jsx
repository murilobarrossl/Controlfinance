import { useState } from "react";
import "./FAQ.css";

const PERGUNTAS = [
  "Meus dados bancários estão seguros?",
  "O Agente de IA funciona para pequenos negócios?",
  "Quais bancos são suportados pela sincronização?",
  "Como funciona a cobrança nos planos trimestral e anual?",
  "Preciso instalar alguma coisa ou é tudo online?",
  "Preciso instalar alguma coisa ou é tudo online?",
  "Como entro em contato com o suporte?",
];

export default function FAQ() {
  const [openIndex, setOpenIndex] = useState(null);

  function toggle(index) {
    setOpenIndex((prev) => (prev === index ? null : index));
  }

  return (
    <section id="faq" className="faq">
      <h2 className="faq__title">Agente de IA</h2>

      <ul className="faq__list">
        {PERGUNTAS.map((pergunta, index) => {
          const isOpen = openIndex === index;
          return (
            <li key={`${pergunta}-${index}`} className="faq__item">
              <button
                className="faq__question"
                onClick={() => toggle(index)}
                aria-expanded={isOpen}
              >
                <span className={`faq__chevron ${isOpen ? "faq__chevron--open" : ""}`}>
                  ⌄
                </span>
                {pergunta}
              </button>
            </li>
          );
        })}
      </ul>

      <p className="faq__footer">
        Não encontrou sua resposta? Fale conosco{" "}
        <a href="mailto:suporte@controlfinance.app.br" className="faq__link">
          suporte@controlfinance.app.br
        </a>
      </p>
    </section>
  );
}
