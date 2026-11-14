import { useState } from "react";
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
// e iniciais num quadrado cinza não acrescentam informação.
export function InstitutionLogo({ institution }) {
  const [failed, setFailed] = useState(false);
  if (!institution?.logoUrl || failed) return null;
  // A imagem vem de um servidor de terceiros (URL informada pela Polp): sem Referer, ele não
  // fica sabendo de qual página do painel o pedido saiu.
  return <img className="inv-institution-logo" src={institution.logoUrl} alt="" width={14} height={14}
    loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)} />;
}
