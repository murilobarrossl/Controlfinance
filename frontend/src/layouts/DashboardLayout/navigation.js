import {
  BoxIcon,
  BuildingIcon,
  CardIcon,
  CashFlowIcon,
  LayoutIcon,
  ReportIcon,
  UsersIcon,
} from "../../components/ui/icons/FeatureIcons.jsx";
import { isBusinessHolder } from "../../utils/holder.js";

const PERSONAL_TABS = [
  { label: "Dashboard inteligente", to: "/dashboard", end: true },
  { label: "Cartões", to: "/dashboard/cartoes" },
  { label: "Receitas e despesas", to: "/dashboard/receitas-despesas" },
  { label: "Relatórios", to: "/dashboard/relatorios" },
  { label: "Categorias", to: "/dashboard/categorias" },
  { label: "Investimentos", to: "/dashboard/investimentos" },
];

const BUSINESS_TABS = [
  { label: "Dashboard inteligente", to: "/dashboard", end: true, Icon: LayoutIcon },
  { label: "Cartões", to: "/dashboard/cartoes", Icon: CardIcon },
  { label: "Folha de pagamento", to: "/dashboard/folha-de-pagamento", Icon: UsersIcon },
  { label: "Fornecedores", to: "/dashboard/fornecedores", Icon: BoxIcon },
  { label: "Fluxo de caixa", to: "/dashboard/fluxo-de-caixa", Icon: CashFlowIcon },
  { label: "Patrimônio", to: "/dashboard/patrimonio", Icon: BuildingIcon },
  { label: "Relatórios", to: "/dashboard/relatorios", Icon: ReportIcon },
];

export function getNavigation(user) {
  return isBusinessHolder(user) ? BUSINESS_TABS : PERSONAL_TABS;
}
