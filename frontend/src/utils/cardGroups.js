import { isPastDue } from "./invoiceDue.js";

const OTHERS_KEY = "outros";

const byInvoiceDesc = (a, b) => (b.currentInvoice ?? 0) - (a.currentInvoice ?? 0);

function sum(cards, pick) {
  let total = 0;
  for (const card of cards) {
    const value = pick(card);
    if (value == null) return null;
    total += value;
  }
  return total;
}

export function summarizeCards(cards) {
  const usedLimit = sum(cards, (c) => c.usedLimit);
  const availableLimit = sum(cards, (c) =>
    c.creditLimit != null && c.usedLimit != null ? c.creditLimit - c.usedLimit : null);
  const openInvoices = sum(cards, (c) => c.currentInvoice);

  const dated = cards
    .filter((c) => c.invoiceDueDate)
    .map((c) => ({ card: c, date: String(c.invoiceDueDate).slice(0, 10) }))
    .sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0));
  const next = dated[0] ?? null;

  return {
    usedLimit,
    availableLimit,
    openInvoices,
    nextDue: next
      ? {
          date: next.card.invoiceDueDate,
          label: [next.card.bankName, next.card.name].filter(Boolean).join(" · "),
          pastDue: isPastDue(next.card.invoiceDueDate),
          syncedAt: next.card.connectionLastSyncedAt,
        }
      : null,
  };
}

export function groupCards(cards) {
  const groups = new Map();

  for (const card of cards) {
    const key = card.isSynced && card.connectionId ? card.connectionId : OTHERS_KEY;
    let group = groups.get(key);
    if (!group) {
      group = {
        key,
        isOthers: key === OTHERS_KEY,
        bankName: key === OTHERS_KEY ? "Outros cartões" : card.bankName || "Banco conectado",
        color: key === OTHERS_KEY ? null : card.institutionColor,
        lastSyncedAt: key === OTHERS_KEY ? null : card.connectionLastSyncedAt,
        cards: [],
      };
      groups.set(key, group);
    }
    group.cards.push(card);
  }

  const list = [...groups.values()].map((group) => ({
    ...group,
    cards: [...group.cards].sort(byInvoiceDesc),
    openInvoice: sum(group.cards, (c) => c.currentInvoice),
    usedLimit: sum(group.cards, (c) => c.usedLimit),
  }));

  list.sort((a, b) => {
    if (a.isOthers !== b.isOthers) return a.isOthers ? 1 : -1;
    return (b.openInvoice ?? 0) - (a.openInvoice ?? 0);
  });

  const showHeaders = !(list.length === 1 && !list[0].isOthers);
  return { groups: list, showHeaders };
}
