export function cardLimits(card) {
  const hasLimit = card.creditLimit != null && card.creditLimit > 0;
  const hasUsage = hasLimit && card.usedLimit != null;
  return {
    hasLimit,
    available: hasUsage ? card.creditLimit - card.usedLimit : null,
    usagePercent: hasUsage ? (card.usedLimit / card.creditLimit) * 100 : null,
    overLimit: hasUsage && card.usedLimit > card.creditLimit,
  };
}
