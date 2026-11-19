import { apiFetch } from "./client.js";

export const getCreditCardsSummary = async ({ signal } = {}) => apiFetch("/credit-cards/summary", { signal });

export const getCreditCardsOverview = ({ signal }) => apiFetch("/credit-cards/overview", { signal });

export function getCardCategoryExpenses({ month, annual, cardId, category, page }, { signal }) {
  const params = new URLSearchParams({ month });
  if (annual) params.set("annual", "true");
  if (cardId) params.set("cardId", cardId);
  if (category) {
    params.set("category", category);
    params.set("page", String(page));
  }
  return apiFetch(`/credit-cards/category-expenses?${params}`, { signal });
}
