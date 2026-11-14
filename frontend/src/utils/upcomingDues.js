import { formatPastDue } from "./invoiceDue.js";
import { isInvoiceOverdue } from "./cardLimits.js";
import { cardNetwork } from "./cardBrand.js";

const MAX_UPCOMING = 8;

// "Próximos vencimentos" junta a fatura de cada cartão (só quando o banco mandou a data de verdade,
// sem estimar a partir do dia de vencimento) e as contas pendentes/atrasadas de todas as contas, já
// que esta página não depende da conta selecionada.
export function buildUpcoming(cards, pendingTransactions) {
  const invoices = cards
    .filter((c) => c.invoiceDueDate)
    .map((c) => ({
      key: `invoice-${c.id}`,
      kind: "Fatura",
      name: `Fatura ${c.name}`,
      meta: [c.bankName, cardNetwork(c.brand)?.label].filter(Boolean).join(" · ") || "Cartão de crédito",
      date: c.invoiceDueDate,
      amount: c.currentInvoice,
      status: c.invoiceStatus,
      pastDueText: isInvoiceOverdue(c) ? formatPastDue(c.invoiceDueDate, c.connectionLastSyncedAt) : null,
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

  return [...invoices, ...bills]
    .sort((a, b) => new Date(a.date) - new Date(b.date))
    .slice(0, MAX_UPCOMING);
}
