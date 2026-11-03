export const roundMoney = (value) => Math.round(value * 100) / 100 || 0;

export function formatCurrency(value) {
  if (typeof value !== "number" || !Number.isFinite(value)) return "—";
  return roundMoney(value).toLocaleString("pt-BR", {
    style: "currency",
    currency: "BRL",
  });
}

export function formatPercentage(value) {
  if (typeof value !== "number" || !Number.isFinite(value)) return "—";
  return `${value.toLocaleString("pt-BR", { maximumFractionDigits: 1 })}%`;
}

export function formatDate(dateStr) {
  if (!dateStr || Number.isNaN(new Date(dateStr).getTime())) return "—";
  return new Date(dateStr).toLocaleDateString("pt-BR", { timeZone: "UTC" });
}

// Tabela Price: parcela fixa para um financiamento/consórcio.
// financedAmount = valor total - entrada; monthlyRate em decimal (ex: 0.02 = 2% a.m.)
export function calculateInstallment(financedAmount, monthlyRate, installmentsCount) {
  if (financedAmount <= 0 || installmentsCount <= 0) {
    return { installmentAmount: null, totalPaid: null, totalInterest: null };
  }

  if (monthlyRate === 0) {
    const installmentAmount = financedAmount / installmentsCount;
    return { installmentAmount, totalPaid: financedAmount, totalInterest: 0 };
  }

  const factor = Math.pow(1 + monthlyRate, installmentsCount);
  const installmentAmount = (financedAmount * (monthlyRate * factor)) / (factor - 1);
  const totalPaid = installmentAmount * installmentsCount;
  const totalInterest = totalPaid - financedAmount;

  return { installmentAmount, totalPaid, totalInterest };
}
