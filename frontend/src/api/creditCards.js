import { apiFetch } from "./client.js";

export const getCreditCards = async () => apiFetch("/credit-cards");
