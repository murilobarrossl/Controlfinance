// Cálculos de rendimento e de financiamento usados na aba Investimentos. Tudo aqui é estimativa:
// o CDI real varia todo dia útil e cada banco tem suas regras, então a tela sempre manda o usuário
// conferir o número final no app da instituição.

// O CDI é divulgado "ao ano" (base 252 dias úteis). Pra uma simulação mês a mês, converte pra uma
// taxa mensal equivalente em juros compostos. A diferença pra contar os dias úteis exatos é
// pequena perto da incerteza sobre o próprio CDI futuro.
export function annualToMonthlyRate(annualPct) {
  return Math.pow(1 + annualPct / 100, 1 / 12) - 1;
}

// Imposto de renda regressivo dos CDBs e fundos de renda fixa, pelo tempo que o dinheiro ficou
// aplicado. Incide só sobre o rendimento, nunca sobre o valor que a pessoa colocou.
export const INCOME_TAX_BRACKETS = [
  { label: "Até 180 dias", maxDays: 180, rate: 22.5 },
  { label: "De 181 a 360 dias", maxDays: 360, rate: 20 },
  { label: "De 361 a 720 dias", maxDays: 720, rate: 17.5 },
  { label: "Acima de 720 dias", maxDays: Infinity, rate: 15 },
];

export function incomeTaxRate(days) {
  return INCOME_TAX_BRACKETS.find((bracket) => days <= bracket.maxDays).rate / 100;
}

// Regra legal da poupança: com Selic acima de 8,5% ao ano rende 0,5% ao mês (+ TR, que está
// perto de zero e fica de fora); com Selic até 8,5% rende 70% da Selic. Isenta de imposto de renda.
export function poupancaAnnualPct(selicPct) {
  if (selicPct > 8.5) return (Math.pow(1.005, 12) - 1) * 100;
  return selicPct * 0.7;
}

// Evolução mês a mês de um valor inicial mais aportes mensais (feitos no fim de cada mês).
// "net" desconta o IR estimado do rendimento futuro de cada lote, como num resgate naquele mês.
// Sem datas originais, o saldo inicial é tratado como um lote novo. Meses aproximados de 30 dias.
export function simulateGrowth({ initial, monthly, months, annualPct, taxed = true, source, importedInvestment }) {
  if (source === "imported_contract" || importedInvestment != null) {
    throw new Error("Investimentos importados exigem um motor decimal e regras contratuais confirmadas.");
  }
  const rate = annualToMonthlyRate(annualPct);
  const series = [{ month: 0, invested: initial, gross: initial, net: initial }];

  let balance = initial;
  const lots = [{ principal: initial, balance: initial, month: 0 }];
  for (let month = 1; month <= months; month += 1) {
    balance = balance * (1 + rate) + monthly;
    const invested = initial + monthly * month;
    let tax = 0;
    for (const lot of lots) {
      lot.balance *= 1 + rate;
      if (taxed) tax += Math.max(0, lot.balance - lot.principal) * incomeTaxRate((month - lot.month) * 30);
    }
    if (monthly > 0) lots.push({ principal: monthly, balance: monthly, month });
    series.push({ month, invested, gross: balance, net: balance - tax });
  }

  const last = series[series.length - 1];
  const incomeGross = last.gross - last.invested;
  return {
    series,
    summary: {
      invested: last.invested,
      incomeGross,
      tax: last.gross - last.net,
      incomeNet: last.net - last.invested,
      finalNet: last.net,
      taxRate: taxed ? incomeTaxRate(months * 30) : 0,
    },
  };
}

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
