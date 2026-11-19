export const frequencyLabel = (months) => ({ 1: "Mensal", 2: "Bimestral", 3: "Trimestral", 6: "Semestral", 12: "Anual" })[months] ?? "Não identificada";
export const activityLabel = (activity) => ({
  RecentCharges: "Com cobranças recentes",
  NoRecentCharge: "Sem cobrança recente",
  OutdatedData: "Dados sem atualização recente",
})[activity] ?? "Situação desconhecida";
export const accountLabel = (expense) => [expense.institutionName, expense.accountName, expense.accountNumber].filter(Boolean).join(" · ");
const PAYMENT_METHODS = {
  BOLETO: "Boleto",
  PIX: "Pix",
  TED: "TED",
  DOC: "DOC",
  TRANSFERENCIA_MESMA_INSTITUICAO: "Transferência",
  CONVENIO_ARRECADACAO: "Pagamento de convênio",
  PACOTE_TARIFA_SERVICOS: "Tarifa bancária",
};
export const paymentMethodLabel = (expense) => expense.isCard ? "Cartão de crédito"
  : ["Conta bancária", PAYMENT_METHODS[expense.paymentMethod]].filter(Boolean).join(" · ");
