const MAX_UPCOMING = 8;

// "Próximos vencimentos" (antes no Dashboard inteligente) junta as três coisas que vencem e que
// esta tela já conhece: fatura de cada cartão (só quando o banco mandou a data de verdade, sem
// estimar a partir do dia de vencimento), a próxima parcela de cada parcelamento e as contas
// pendentes/atrasadas de todas as contas, já que esta página não depende da conta selecionada.
export function buildUpcoming(cards, installments, pendingTransactions) {
  const invoices = cards
    .filter((c) => c.invoiceDueDate)
    .map((c) => ({
      key: `invoice-${c.id}`,
      kind: "Fatura",
      name: `Fatura ${c.name}`,
      meta: c.brand || "Cartão de crédito",
      date: c.invoiceDueDate,
      amount: c.currentInvoice,
      status: "Pending",
    }));
  const nextInstallments = installments.map((i) => ({
    key: `installment-${i.id}`,
    kind: "Parcela",
    name: i.description,
    meta: `${i.currentInstallment}/${i.totalInstallments} · ${i.creditCardName || "Sem cartão vinculado"}`,
    date: i.nextDueDate,
    amount: i.installmentAmount,
    status: "Pending",
  }));
  const bills = pendingTransactions
    .filter((t) => !t.isTransfer)
    .map((t) => ({
      key: `transaction-${t.id}`,
      kind: "Conta",
      name: t.name,
      meta: `${t.categoryName || "Sem categoria"}${t.bankAccountName ? ` · ${t.bankAccountName}` : ""}`,
      date: t.dueDate,
      amount: t.amount,
      status: t.status,
    }));

  return [...invoices, ...nextInstallments, ...bills]
    .sort((a, b) => new Date(a.date) - new Date(b.date))
    .slice(0, MAX_UPCOMING);
}
