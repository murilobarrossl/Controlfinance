import { getCurrentUserId } from "./authToken.js";

const KEY = "emergencyReserve";

function storageKey() {
  return `${KEY}:${getCurrentUserId() ?? "anonymous"}`;
}

const EMPTY_RESERVE = { currentAmount: 0 };

export function getReserve() {
  try {
    const raw = localStorage.getItem(storageKey());
    return raw ? { ...EMPTY_RESERVE, ...JSON.parse(raw) } : EMPTY_RESERVE;
  } catch {
    return EMPTY_RESERVE;
  }
}

function persist(reserve) {
  localStorage.setItem(storageKey(), JSON.stringify(reserve));
}

export function addToReserve(amount) {
  const reserve = getReserve();
  persist({ ...reserve, currentAmount: reserve.currentAmount + amount });
}
