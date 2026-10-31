const OTHERS_KEY = "outros";

const byInvoiceDesc = (a, b) => (b.currentInvoice ?? 0) - (a.currentInvoice ?? 0);

function sum(cards, pick) {
  return cards.reduce((total, card) => total + (pick(card) ?? 0), 0);
}

export function summarizeCards(cards) {
  const usedLimit = sum(cards, (c) => c.usedLimit);
  const availableLimit = sum(cards, (c) => (c.creditLimit != null ? Math.max(0, c.creditLimit - (c.usedLimit ?? 0)) : 0));
  const openInvoices = sum(cards, (c) => c.currentInvoice);

  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const dated = cards
    .filter((c) => c.invoiceDueDate)
    .map((c) => ({ card: c, date: new Date(c.invoiceDueDate) }))
    .sort((a, b) => a.date - b.date);
  const next = dated.find((d) => d.date >= today) ?? dated[0] ?? null;

  return {
    usedLimit,
    availableLimit,
    openInvoices,
    nextDue: next ? { date: next.card.invoiceDueDate, label: next.card.bankName || next.card.name } : null,
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
    return b.openInvoice - a.openInvoice;
  });

  const showHeaders = !(list.length === 1 && !list[0].isOthers);
  return { groups: list, showHeaders };
}
