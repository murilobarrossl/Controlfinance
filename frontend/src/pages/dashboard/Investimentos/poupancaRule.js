import { formatPercentage } from "../../../utils/financeMath.js";

// Mesmo corte de poupancaAnnualPct: acima de 8,5% de Selic rende 0,5% ao mês, abaixo 70% da Selic ao
// ano, sempre mais a TR, que as estimativas desta aba não incluem.
export function poupancaRuleLabel(selicPct) {
  return selicPct > 8.5 ? "0,5% ao mês + TR" : `${formatPercentage(selicPct * 0.7)} ao ano + TR`;
}
