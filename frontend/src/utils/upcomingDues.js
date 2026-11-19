import { formatPastDue } from "./invoiceDue.js";
import { isInvoiceOverdue } from "./cardLimits.js";
import { cardNetwork } from "./cardBrand.js";

// A API já selecionou e ordenou os oito vencimentos. A fatura referencia o cartão do mesmo resumo.
export function buildUpcoming(cards, dueItems) {
  const cardsById = new Map(cards.map((card) => [card.id, card]));
  return dueItems.map((item) => {
    if (item.isInvoice) {
      const card = cardsById.get(item.id);
      return {
        key: `invoice-${card.id}`, kind: "Fatura", name: `Fatura ${card.name}`,
        meta: [card.bankName, cardNetwork(card.brand)?.label].filter(Boolean).join(" · ") || "Cartão de crédito",
        date: item.dueDate, amount: card.currentInvoice, status: card.invoiceStatus,
        pastDueText: isInvoiceOverdue(card) ? formatPastDue(item.dueDate, card.connectionLastSyncedAt) : null,
      };
    }
    return {
      key: `transaction-${item.id}`, kind: "Conta", name: item.name,
      meta: `${item.categoryName || "Sem categoria"}${item.bankAccountName ? ` · ${item.bankAccountName}` : ""}`,
      date: item.dueDate, amount: item.amount, status: item.status,
    };
  });
}
