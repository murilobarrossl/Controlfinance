// Cálculos do simulador de financiamento (Tabela Price). Estimativa: a tela manda conferir no banco.

// Tabela Price mês a mês: cada parcela é fixa e se divide em juros (sobre o saldo devedor) e
// amortização (o que realmente abate a dívida). Nos primeiros meses quase tudo é juro.
export function buildPriceSchedule(financedAmount, monthlyRate, installmentsCount) {
  if (financedAmount <= 0 || installmentsCount <= 0) return [];

  const payment =
    monthlyRate === 0
      ? financedAmount / installmentsCount
      : (financedAmount * monthlyRate) / (1 - Math.pow(1 + monthlyRate, -installmentsCount));

  const rows = [];
  let balance = financedAmount;
  for (let month = 1; month <= installmentsCount; month += 1) {
    const interest = balance * monthlyRate;
    const amortization = payment - interest;
    balance = Math.max(0, balance - amortization);
    rows.push({ month, payment, interest, amortization, balance });
  }
  return rows;
}

// Mesma dívida em prazos diferentes: mostra o quanto alongar o prazo reduz a parcela mas
// aumenta o total de juros.
export function compareTerms(financedAmount, monthlyRate, terms) {
  return terms.map((count) => {
    const rows = buildPriceSchedule(financedAmount, monthlyRate, count);
    const payment = rows[0]?.payment ?? 0;
    const totalPaid = payment * count;
    return { count, payment, totalPaid, totalInterest: totalPaid - financedAmount };
  });
}
