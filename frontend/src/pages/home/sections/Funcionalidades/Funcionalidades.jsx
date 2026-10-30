import SectionHeading from "../../../../components/ui/SectionHeading/SectionHeading.jsx";
import {
  WalletIcon,
  SyncIcon,
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
    titulo: "Sincronização Bancária",
    descricao:
      "conexão via Open Finance com suas contas e cartões, atualizada sempre que você abre o painel",
    Icon: SyncIcon,
  },
  {
    titulo: "Planejamentos e Metas",
    descricao:
      "defina objetivos financeiros e acompanhe sua evolução com barras de progresso",
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
    titulo: "Lembretes de Vencimento",
    descricao:
      "avisos por e-mail das contas pendentes que estão perto de vencer",
    Icon: BellIcon,
  },
  {
    titulo: "Seus Dados, Seu Controle",
    descricao:
      "baixe uma cópia de tudo que guardamos, desconecte bancos e exclua a conta direto no painel",
    Icon: ExportIcon,
  },
  {
    titulo: "Histórico Financeiro",
    descricao: "extrato importado do banco conectado e lançamentos manuais, com filtros por mês e categoria",
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
