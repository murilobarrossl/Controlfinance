import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import {
  BuildingIcon, CandlestickIcon, CoinsIcon, HomeIcon, LandmarkIcon, LeafIcon, PieChartIcon, PiggyBankIcon, WalletIcon,
} from "../../../components/ui/icons/FeatureIcons.jsx";

// LCI/CRI financiam imóveis e LCA/CRA o agronegócio: o ícone diz o lastro, não o banco.
const ICONS = {
  reserved: PiggyBankIcon,
  CDB: CoinsIcon,
  RDB: CoinsIcon,
  LCI: HomeIcon,
  CRI: HomeIcon,
  LCA: LeafIcon,
  CRA: LeafIcon,
  DEBENTURES: BuildingIcon,
  "treasure-titles": LandmarkIcon,
  funds: PieChartIcon,
  "variable-incomes": CandlestickIcon,
};

export function InvestmentIcon({ item }) {
  const Icon = ICONS[item.modality?.key] ?? WalletIcon;
  return <span className="inv-mark" aria-hidden="true"><Icon /></span>;
}

// Sem logo do conector (ou se a imagem falhar) não mostra nada: o nome do banco já está ao lado,
// e iniciais num quadrado de 24px não acrescentam informação.
export function InstitutionLogo({ institution }) {
  return <BankLogo name={institution?.name} logoUrl={institution?.logoUrl} size={24} fallback="none" />;
}
