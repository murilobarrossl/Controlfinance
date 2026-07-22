const MONTH_SHORT_LABEL = new Intl.DateTimeFormat("pt-BR", { month: "short", timeZone: "UTC" });
const MONTH_LONG_LABEL = new Intl.DateTimeFormat("pt-BR", { month: "long", year: "numeric", timeZone: "UTC" });

export function monthKey(date) {
  return `${date.getUTCFullYear()}-${date.getUTCMonth()}`;
}

export function formatMonthShort(date) {
  const label = MONTH_SHORT_LABEL.format(date).replace(".", "");
  const capitalized = label.charAt(0).toUpperCase() + label.slice(1);
  return `${capitalized}/${String(date.getUTCFullYear()).slice(2)}`;
}

export function formatMonthLong(date) {
  const label = MONTH_LONG_LABEL.format(date);
  return label.charAt(0).toUpperCase() + label.slice(1);
}
