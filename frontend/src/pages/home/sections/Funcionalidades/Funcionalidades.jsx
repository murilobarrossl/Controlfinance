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
  ShieldIcon,
} from "../../../../components/ui/icons/FeatureIcons.jsx";
import "./Funcionalidades.css";

const GRUPOS = [
  {
    titulo: "Conexão com o banco",
    nota:
      "A senha do seu banco não passa por nós. Guardamos só o necessário para o painel, cifrado, e você pode baixar ou apagar tudo quando quiser.",
    itens: [
      {
        titulo: "Sincronização bancária",
        descricao: "Conexão via Open Finance com suas contas e cartões, atualizada automaticamente quando o banco tem dados novos",
        Icon: SyncIcon,
      },
      {
        titulo: "Controle de cartões",
        descricao: "Acompanhe faturas, limites e gastos por cartão em um só lugar",
        Icon: CardIcon,
      },
      {
        titulo: "Lembretes de vencimento",
        descricao: "Avisos por e-mail das contas pendentes que estão perto de vencer",
        Icon: BellIcon,
      },
    ],
  },
  {
    titulo: "Organização do dinheiro",
    itens: [
      {
        titulo: "Gestão de receitas e despesas",
        descricao: "Categorize e monitore cada movimentação financeira com precisão e clareza",
        Icon: WalletIcon,
      },
      {
        titulo: "Histórico financeiro",
        descricao: "Extrato importado automaticamente dos bancos conectados, com filtros por mês e categoria",
        Icon: HistoryIcon,
      },
      {
        titulo: "Relatórios e dashboards",
        descricao: "Visualizações detalhadas do seu patrimônio, fluxo de caixa e tendências de gastos",
        Icon: ChartIcon,
      },
      {
        titulo: "Planejamentos e metas",
        descricao: "Defina objetivos financeiros e acompanhe sua evolução com barras de progresso",
        Icon: TargetIcon,
      },
    ],
  },
  {
    titulo: "Pessoa física, empresa e seus dados",
    itens: [
      {
        titulo: "Suporte a pessoa física e jurídica",
        descricao: "Estrutura adaptável para organização pessoal e gestão financeira de pequenos negócios",
        Icon: BuildingIcon,
      },
      {
        titulo: "Seus dados, seu controle",
        descricao: "Baixe uma cópia de tudo que guardamos, desconecte bancos e exclua a conta direto no painel",
        Icon: ExportIcon,
      },
    ],
  },
];

export default function Funcionalidades() {
  return (
    <section id="funcionalidades" className="funcionalidades">
      <SectionHeading title="Funcionalidades" />

      <div className="funcionalidades__groups">
        {GRUPOS.map(({ titulo, nota, itens }) => (
          <div key={titulo} className="funcionalidades__group">
            <div className="funcionalidades__group-head">
              <h3 className="funcionalidades__group-title">{titulo}</h3>
              {nota && (
                <p className="funcionalidades__note">
                  <ShieldIcon />
                  <span>{nota}</span>
                </p>
              )}
            </div>
            <ul className="funcionalidades__list">
              {itens.map(({ titulo: itemTitulo, descricao, Icon }) => (
                <li key={itemTitulo} className="funcionalidades__item">
                  <span className="funcionalidades__icon" aria-hidden="true">
                    <Icon />
                  </span>
                  <div>
                    <h4 className="funcionalidades__item-title">{itemTitulo}</h4>
                    <p className="funcionalidades__item-desc">{descricao}</p>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </section>
  );
}
