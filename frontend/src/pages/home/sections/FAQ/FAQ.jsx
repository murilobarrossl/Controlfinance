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

function ChevronIcon() {
  return (
    <svg
      className="faq__chevron-icon"
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        d="M5.70711 9.71069C5.31658 10.1012 5.31658 10.7344 5.70711 11.1249L10.5993 16.0123C11.3805 16.7927 12.6463 16.7924 13.4271 16.0117L18.3174 11.1213C18.708 10.7308 18.708 10.0976 18.3174 9.70708C17.9269 9.31655 17.2937 9.31655 16.9032 9.70708L12.7176 13.8927C12.3271 14.2833 11.6939 14.2832 11.3034 13.8927L7.12132 9.71069C6.7308 9.32016 6.09763 9.32016 5.70711 9.71069Z"
        fill="currentColor"
      />
    </svg>
  );
}

export default function FAQ() {
  const [openIndex, setOpenIndex] = useState(null);

  function toggle(index) {
    setOpenIndex((prev) => (prev === index ? null : index));
  }

  return (
    <section id="faq" className="faq">
      <h2 className="faq__title">Tire suas dúvidas</h2>
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
                  <ChevronIcon />
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
