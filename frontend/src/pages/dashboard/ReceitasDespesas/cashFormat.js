import { formatCurrency } from "../../../utils/financeMath.js";

const DATE_TIME = new Intl.DateTimeFormat("pt-BR", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});

const SHORT_DAY = new Intl.DateTimeFormat("pt-BR", { day: "2-digit", month: "2-digit", timeZone: "UTC" });

export const formatDateTime = (iso) => (iso ? DATE_TIME.format(new Date(iso)) : "—");

export const formatShortDay = (isoDate) => SHORT_DAY.format(new Date(`${isoDate}T00:00:00Z`));

export function formatSignedMoney(value) {
  if (typeof value !== "number") return "—";
  if (value === 0) return formatCurrency(0);
  return `${value < 0 ? "−" : "+"}${formatCurrency(Math.abs(value))}`;
}

export function formatResult(value) {
  if (typeof value !== "number") return "—";
  return value < 0 ? `−${formatCurrency(Math.abs(value))}` : formatCurrency(value);
}

export const monthParam = (date) => `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, "0")}`;

export function parseMonthParam(value) {
  const match = /^(\d{4})-(0[1-9]|1[0-2])$/.exec(value ?? "");
  return match ? new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, 1)) : null;
}

export function currentMonthStart() {
  const now = new Date();
  return new Date(Date.UTC(now.getFullYear(), now.getMonth(), 1));
}

export function dayOffsetFrom(isoDate) {
  const now = new Date();
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  return Math.round((today - Date.parse(`${isoDate}T00:00:00Z`)) / 86400000);
}
