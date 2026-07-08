export function formatCurrency(value) {
  return (value ?? 0).toLocaleString("pt-BR", {
    style: "currency",
    currency: "BRL",
  });
}

export function formatPercentage(value) {
  return `${(value ?? 0).toLocaleString("pt-BR", { maximumFractionDigits: 1 })}%`;
}

// Tabela Price: parcela fixa para um financiamento/consórcio.
// financedAmount = valor total - entrada; monthlyRate em decimal (ex: 0.02 = 2% a.m.)
export function calculateInstallment(financedAmount, monthlyRate, installmentsCount) {
  if (financedAmount <= 0 || installmentsCount <= 0) {
    return { installmentAmount: 0, totalPaid: 0, totalInterest: 0 };
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
