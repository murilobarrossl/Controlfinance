import SectionHeading from "../../../../components/ui/SectionHeading/SectionHeading.jsx";
import {
  WalletIcon,
  SyncIcon,
  SparkleIcon,
  TargetIcon,
  ChartIcon,
  BuildingIcon,
  CardIcon,
  BellIcon,
  ExportIcon,
  HistoryIcon,
} from "../../../../components/ui/icons/FeatureIcons.jsx";
import "./Funcionalidades.css";

const FUNCIONALIDADES = [
  {
    titulo: "Gestão de Receitas e Despesas",
    descricao:
      "categorize e monitore cada movimentação financeira com precisão e clareza",
    Icon: WalletIcon,
  },
  {
    titulo: "Sincronização Bancária em Tempo Real",
    descricao:
      "integração direta com suas contas e cartões, sem necessidade de lançamentos manuais",
    Icon: SyncIcon,
  },
  {
    titulo: "Agente de IA Financeiro",
    descricao:
      "análise personalizada, simulações de investimento, cálculo de financiamentos e planejamento baseado na sua realidade financeira",
    Icon: SparkleIcon,
  },
  {
    titulo: "Planejamentos e Metas",
    descricao:
      "defina objetivos financeiros e acompanhe sua evolução com projeções inteligentes",
    Icon: TargetIcon,
  },
  {
    titulo: "Relatórios e Dashboards",
    descricao:
      "visualizações detalhadas do seu patrimônio, fluxo de caixa e tendências de gastos",
    Icon: ChartIcon,
  },
  {
    titulo: "Suporte a Pessoa Física e Jurídica",
    descricao:
      "estrutura adaptável para organização pessoal e gestão financeira de pequenos negócios",
    Icon: BuildingIcon,
  },
  {
    titulo: "Controle de Cartões",
    descricao:
      "acompanhe faturas, limites e gastos por cartão em um só lugar",
    Icon: CardIcon,
  },
  {
    titulo: "Alertas e Notificações Inteligentes",
    descricao:
      "avisos automáticos de vencimentos, gastos acima do esperado e oportunidades de economia",
    Icon: BellIcon,
  },
  {
    titulo: "Exportação de Relatórios",
    descricao:
      "geração de planilhas e documentos para análise externa ou declaração de imposto de renda",
    Icon: ExportIcon,
  },
  {
    titulo: "Histórico Financeiro Completo",
    descricao: "acesso ao registro de todas as movimentações desde o primeiro uso",
    Icon: HistoryIcon,
  },
];

export default function Funcionalidades() {
  return (
    <section id="funcionalidades" className="funcionalidades">
      <SectionHeading kicker="O que você encontra aqui" title="Funcionalidades" />

      <div className="funcionalidades__grid">
        {FUNCIONALIDADES.map(({ titulo, descricao, Icon }) => (
          <div key={titulo} className="funcionalidades__card">
            <div className="funcionalidades__card-head">
              <span className="funcionalidades__icon">
                <Icon />
              </span>
              <h3 className="funcionalidades__item-title">{titulo}</h3>
            </div>
            <p className="funcionalidades__item-desc">{descricao}</p>
          </div>
        ))}
      </div>
    </section>
  );
}
