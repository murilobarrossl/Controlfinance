import "./AgenteIA.css";

const CAPACIDADES = [
  "Explicar termos e conceitos financeiros de forma simples",
  "Simular qualquer modalidade de crédito e investimento — financiamentos, empréstimos, consórcios, parcelas e muito mais",
  "Analisar sua situação financeira atual e sugerir melhorias",
  "Criar planejamentos personalizados de economia e investimento",
  "Pesquisar o mercado financeiro e indicar onde investir baseado no seu perfil",
  "Gerar planilhas e projeções dentro do app.",
];

export default function AgenteIA() {
  return (
    <section id="agente-ia" className="agente-ia">
      <h2 className="agente-ia__title">Agente de IA</h2>

      <div className="agente-ia__block">
        <h3 className="agente-ia__subtitle">O que ele pode fazer:</h3>
        <ul className="agente-ia__list">
          {CAPACIDADES.map((item) => (
            <li key={item} className="agente-ia__list-item">
              {item}
            </li>
          ))}
        </ul>
      </div>

      <div className="agente-ia__block">
        <h3 className="agente-ia__subtitle">Seus dados estão seguros</h3>
        <p className="agente-ia__text">
          O Agente de IA analisa suas informações financeiras dentro de um
          ambiente seguro e criptografado. Seus dados pessoais, saldo e
          transações nunca são compartilhados com terceiros. A inteligência
          artificial processa apenas o necessário para responder sua pergunta
          — sem armazenar histórico de conversas fora da plataforma.
        </p>
        <p className="agente-ia__text agente-ia__text--highlight">
          <span className="agente-ia__highlight-label">
            Como funciona na prática:
          </span>{" "}
          O agente acessa os dados reais da sua conta — receitas, despesas,
          saldo e histórico — e usa essas informações para dar respostas
          personalizadas, não genéricas. Você não precisa explicar sua
          situação do zero.
        </p>
      </div>
    </section>
  );
}
