import { useState } from "react";
import SectionHeading from "../../../../components/ui/SectionHeading/SectionHeading.jsx";
import { PLANS, FREE_TRIAL_DAYS, formatPrice } from "../../../../utils/pricing.js";
import "./FAQ.css";

const { monthly, quarterly, annual } = PLANS;

const PERGUNTAS = [
  {
    pergunta: "Preciso instalar alguma coisa ou é tudo online?",
    resposta:
      "É tudo online. A Control Finance funciona direto no navegador, no computador ou no celular, não precisa baixar nem instalar nada.",
  },
  {
    pergunta: "Como funciona a cobrança nos planos?",
    resposta: `O mensal custa ${formatPrice(monthly.monthlyPrice)} por mês. No trimestral, a cobrança é de ${formatPrice(
      quarterly.totalPrice
    )} a cada 3 meses (equivale a ${formatPrice(quarterly.monthlyPrice)}/mês, ${
      quarterly.discountPercent
    }% de economia em relação ao mensal). No anual, ${formatPrice(annual.totalPrice)} uma vez por ano (equivale a ${formatPrice(
      annual.monthlyPrice
    )}/mês, ${annual.discountPercent}% de economia, ou ${formatPrice(
      annual.savingsPerPeriod
    )} a menos por ano). A contratação paga ainda não está aberta e nenhuma cobrança é feita hoje. Quando abrir, cada plano terá ${FREE_TRIAL_DAYS} dias grátis, e as regras de renovação, cancelamento e reembolso serão publicadas antes de qualquer cobrança.`,
  },
  {
    pergunta: "Quais bancos são suportados pela sincronização?",
    resposta:
      "A sincronização funciona com os bancos e instituições financeiras participantes do Open Finance no Brasil, a mesma estrutura usada pelos grandes bancos e fintechs para compartilhar dados com segurança.",
  },
  {
    pergunta: "Como vocês tratam meus dados bancários?",
    resposta:
      "Você autoriza a conexão no ambiente do próprio banco, pelo Open Finance, e a senha do banco não passa pelo Control Finance. Os dados chegam por meio da Polp, nossa fornecedora de integração. Guardamos só o necessário para o painel (contas, saldos, extrato, dados de cartão e os 4 últimos dígitos do número da conta), cifrados no banco de dados. Seu CPF não fica guardado. Os dados passam por fornecedores indispensáveis ao serviço (integração bancária, hospedagem e envio de e-mail), todos listados na Política de Privacidade. Não vendemos dados e não usamos para publicidade.",
  },
  {
    pergunta: "Vocês compartilham meus dados com terceiros?",
    resposta:
      "Só com os fornecedores sem os quais o serviço não funciona: a Polp (e a instituição participante do Open Finance que ela usa) para trazer os dados do banco, a hospedagem onde o sistema roda e a Resend para enviar e-mails. Os e-mails levam apenas o seu endereço e um aviso genérico, sem nomes nem valores. A lista completa, com a finalidade de cada um, está na Política de Privacidade.",
  },
  {
    pergunta: "Qual a diferença entre conta corrente e cartão de crédito no painel?",
    resposta:
      "A conta corrente mostra o dinheiro que você tem hoje: saldo, entradas e saídas. O cartão de crédito mostra o que você gastou usando crédito e ainda vai pagar na fatura. Quando um banco conectado tem os dois, eles aparecem juntos na mesma conexão, mas os valores nunca são somados. Fatura, limite, vencimentos e parcelamentos ficam reunidos na aba Cartões.",
  },
  {
    pergunta: "O que eu encontro no painel?",
    resposta:
      "Um dashboard com saldo, receitas e despesas do mês e principais categorias de gasto; a aba Cartões, com fatura, limite, próximos vencimentos e parcelamentos; Receitas e despesas e Categorias, para entender para onde vai o dinheiro; Relatórios, com o histórico de lançamentos filtrável por mês ou por todo o período; Investimentos, com as contas de reserva e investimento reconhecidas nos seus bancos conectados, metas de economia e um simulador de financiamento; e Privacidade e dados, onde você baixa uma cópia dos seus dados, desconecta bancos e exclui a conta. Os dados do banco são atualizados automaticamente quando há informações novas, não em tempo real, e o painel mostra quando foi a última sincronização.",
  },
  {
    pergunta: "Vou receber lembretes de vencimento?",
    resposta:
      "Sim, se você quiser. O lembrete avisa quantas contas pendentes vencem nos próximos 3 dias, sem nomes nem valores (os detalhes ficam só no painel). Dá para desligar em Privacidade e dados.",
  },
  {
    pergunta: "Posso desconectar um banco ou excluir minha conta?",
    resposta:
      "Pode, direto no painel. Ao desconectar um banco, apagamos na hora tudo que veio dele e pedimos à Polp que exclua a conexão; você também pode revogar a autorização no aplicativo do banco, na área de Open Finance. Ao excluir a conta, apagamos todos os seus dados dos nossos servidores. Cópias de segurança da hospedagem só deixam de conter os dados quando expiram, no prazo informado na Política de Privacidade.",
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
      <div className="faq__intro">
        <SectionHeading title="Tire suas dúvidas" />
        <p className="faq__footer">
          Não encontrou sua resposta? Fale conosco{" "}
          <a href="mailto:suporte@controlfinance.app.br" className="faq__link">
            suporte@controlfinance.app.br
          </a>
        </p>
      </div>

      <ul className="faq__list">
        {PERGUNTAS.map(({ pergunta, resposta }, index) => {
          const isOpen = openIndex === index;
          const answerId = `faq-answer-${index}`;
          const questionId = `faq-question-${index}`;
          return (
            <li key={pergunta} className="faq__item">
              <button
                id={questionId}
                type="button"
                className="faq__question"
                onClick={() => toggle(index)}
                aria-expanded={isOpen}
                aria-controls={answerId}
              >
                {pergunta}
                <span className={`faq__chevron ${isOpen ? "faq__chevron--open" : ""}`} aria-hidden="true">
                  <ChevronIcon />
                </span>
              </button>
              <div
                id={answerId}
                role="region"
                aria-labelledby={questionId}
                inert={!isOpen}
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
    </section>
  );
}
