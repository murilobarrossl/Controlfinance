import { useState } from "react";
import SectionHeading from "../../../../components/ui/SectionHeading/SectionHeading.jsx";
import "./FAQ.css";

const PERGUNTAS = [
  {
    pergunta: "Preciso instalar alguma coisa ou é tudo online?",
    resposta:
      "É tudo online. A Control Finance funciona direto no navegador, no computador ou no celular — não precisa baixar nem instalar nada.",
  },
  {
    pergunta: "Como funciona a cobrança nos planos trimestral e anual?",
    resposta:
      "No trimestral, a cobrança é de R$134,70 a cada 3 meses (10% de desconto em relação ao mensal). No anual, R$478,80 uma vez por ano (20% de desconto, com 2 meses grátis). Em ambos, a cobrança é recorrente e automática, e você pode cancelar quando quiser — todo plano tem 14 dias grátis para testar antes de decidir.",
  },
  {
    pergunta: "Quais bancos são suportados pela sincronização?",
    resposta:
      "A sincronização funciona com os bancos e instituições financeiras participantes do Open Finance no Brasil — a mesma estrutura usada pelos grandes bancos e fintechs para compartilhar dados com segurança.",
  },
  {
    pergunta: "Meus dados bancários estão seguros?",
    resposta:
      "Sim. Suas informações financeiras são criptografadas e a sincronização acontece via Open Finance, regulado pelo Banco Central — a Control Finance nunca tem acesso à senha do seu banco e não compartilha seus dados com terceiros.",
  },
  {
    pergunta: "O Agente de IA funciona para pequenos negócios?",
    resposta:
      "Sim. A plataforma foi pensada para pessoa física e jurídica — o Agente de IA analisa receitas, despesas e fluxo de caixa tanto de quem organiza as finanças pessoais quanto de quem administra um pequeno negócio.",
  },
  {
    pergunta: "Como entro em contato com o suporte?",
    resposta:
      "Pelo e-mail suporte@controlfinance.app.br, nosso time responde por lá. Quem assina o plano trimestral ou anual conta com suporte prioritário.",
  },
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
      <SectionHeading kicker="Dúvidas" title="Tire suas dúvidas" />
      <ul className="faq__list">
        {PERGUNTAS.map(({ pergunta, resposta }, index) => {
          const isOpen = openIndex === index;
          const answerId = `faq-answer-${index}`;
          return (
            <li key={pergunta} className="faq__item">
              <button
                className="faq__question"
                onClick={() => toggle(index)}
                aria-expanded={isOpen}
                aria-controls={answerId}
              >
                {pergunta}
                <span className={`faq__chevron ${isOpen ? "faq__chevron--open" : ""}`}>
                  <ChevronIcon />
                </span>
              </button>
              <div
                id={answerId}
                role="region"
                className={`faq__answer ${isOpen ? "faq__answer--open" : ""}`}
              >
                <div className="faq__answer-inner">
                  <p>{resposta}</p>
                </div>
              </div>
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
