export function cardLimits(card) {
  const hasLimit = card.creditLimit != null && card.creditLimit > 0;
  const hasUsage = hasLimit && card.usedLimit != null;
  const overLimit = hasUsage && card.usedLimit > card.creditLimit;
  return {
    hasLimit,
    available: hasUsage ? Math.max(0, card.creditLimit - card.usedLimit) : null,
    usagePercent: hasUsage ? (card.usedLimit / card.creditLimit) * 100 : null,
    overLimit,
    overAmount: overLimit ? card.usedLimit - card.creditLimit : 0,
  };
}

export function isInvoiceOverdue(card) {
  return card.invoiceStatus === "Overdue";
}
