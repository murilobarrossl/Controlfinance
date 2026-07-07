import "./Funcionalidades.css";

const FUNCIONALIDADES = [
  {
    titulo: "Gestão de Receitas e Despesas",
    descricao:
      "categorize e monitore cada movimentação financeira com precisão e clareza",
  },
  {
    titulo: "Sincronização Bancária em Tempo Real",
    descricao:
      "integração direta com suas contas e cartões, sem necessidade de lançamentos manuais",
  },
  {
    titulo: "Agente de IA Financeiro",
    descricao:
      "análise personalizada, simulações de investimento, cálculo de financiamentos e planejamento baseado na sua realidade financeira",
  },
  {
    titulo: "Planejamentos e Metas",
    descricao:
      "defina objetivos financeiros e acompanhe sua evolução com projeções inteligentes",
  },
  {
    titulo: "Relatórios e Dashboards",
    descricao:
      "visualizações detalhadas do seu patrimônio, fluxo de caixa e tendências de gastos",
  },
  {
    titulo: "Suporte a Pessoa Física e Jurídica",
    descricao:
      "estrutura adaptável para organização pessoal e gestão financeira de pequenos negócios",
  },
  {
    titulo: "Controle de Cartões",
    descricao:
      "acompanhe faturas, limites e gastos por cartão em um só lugar",
  },
  {
    titulo: "Alertas e Notificações Inteligentes",
    descricao:
      "avisos automáticos de vencimentos, gastos acima do esperado e oportunidades de economia",
  },
  {
    titulo: "Exportação de Relatórios",
    descricao:
      "geração de planilhas e documentos para análise externa ou declaração de imposto de renda",
  },
  {
    titulo: "Histórico Financeiro Completo",
    descricao: "acesso ao registro de todas as movimentações desde o primeiro uso",
  },
];

export default function Funcionalidades() {
  return (
    <section id="funcionalidades" className="funcionalidades">
      <h2 className="funcionalidades__title">Funcionalidades</h2>

      <ul className="funcionalidades__list">
        {FUNCIONALIDADES.map((item) => (
          <li key={item.titulo} className="funcionalidades__item">
            <span className="funcionalidades__item-title">{item.titulo}</span>{" "}
            — {item.descricao}
          </li>
        ))}
      </ul>
    </section>
  );
}
