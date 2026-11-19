import { formatMonthLong, toIsoDate } from "./monthLabel.js";
import { getMonthsWindow, monthRange } from "./monthlyTrend.js";

const DAY_LABEL = new Intl.DateTimeFormat("pt-BR", {
  weekday: "short",
  day: "numeric",
  month: "long",
  year: "numeric",
  timeZone: "UTC",
});

const capitalize = (text) => text.charAt(0).toUpperCase() + text.slice(1);

function localToday() {
  const now = new Date();
  return { year: now.getFullYear(), month: now.getMonth(), day: now.getDate() };
}

function dayAt(offset) {
  const { year, month, day } = localToday();
  return new Date(Date.UTC(year, month, day - offset));
}

function dayRange(date) {
  const next = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate() + 1));
  return { from: toIsoDate(date), to: toIsoDate(next) };
}

export function yearRange(year) {
  return { from: `${String(year).padStart(4, "0")}-01-01`, to: `${String(year + 1).padStart(4, "0")}-01-01` };
}

const SHORT_DAY = new Intl.DateTimeFormat("pt-BR", { day: "numeric", month: "short", timeZone: "UTC" });

// Período em andamento comparado com o anterior inteiro mistura um recorte parcial (mais lançamentos
// futuros já agendados) com um período completo. Nesse caso os dois lados vão do início até hoje.
// currentRange nulo = período encerrado, comparação inteiro × inteiro com os totais exibidos.
export function compareMonth(monthDate) {
  const { year, month, day } = localToday();
  const previous = new Date(monthDate);
  previous.setUTCMonth(previous.getUTCMonth() - 1, 1);
  const previousName = formatMonthLong(previous);
  if (monthDate.getUTCFullYear() !== year || monthDate.getUTCMonth() !== month) {
    return { currentRange: null, range: monthRange(previous), trendLabel: `vs ${previousName.toLowerCase()}`, currentName: formatMonthLong(monthDate), previousName };
  }
  const previousEnd = Math.min(Date.UTC(year, month - 1, day + 1), Date.UTC(year, month, 1));
  const span = `1 a ${day}`;
  return {
    currentRange: { from: toIsoDate(monthDate), to: toIsoDate(new Date(Date.UTC(year, month, day + 1))) },
    range: { from: toIsoDate(previous), to: toIsoDate(new Date(previousEnd)) },
    trendLabel: "vs mesmo período do mês anterior",
    currentName: `${formatMonthLong(monthDate)} (${span})`,
    previousName: `${previousName} (${span})`,
  };
}

export function compareYear(targetYear) {
  const { year, month, day } = localToday();
  if (targetYear !== year) {
    return { currentRange: null, range: yearRange(targetYear - 1), trendLabel: "vs ano anterior", currentName: String(targetYear), previousName: String(targetYear - 1) };
  }
  const previousEnd = Math.min(Date.UTC(year - 1, month, day + 1), Date.UTC(year - 1, month + 1, 1));
  const until = SHORT_DAY.format(new Date(Date.UTC(year, month, day))).replace(".", "");
  return {
    currentRange: { from: `${year}-01-01`, to: toIsoDate(new Date(Date.UTC(year, month, day + 1))) },
    range: { from: `${year - 1}-01-01`, to: toIsoDate(new Date(previousEnd)) },
    trendLabel: `vs mesmo período de ${year - 1}`,
    currentName: `${year} (até ${until})`,
    previousName: `${year - 1} (até ${until})`,
  };
}

export const PERIODS = {
  day: {
    key: "day",
    label: "Dia",
    current: (offset) => {
      const date = dayAt(offset);
      return { range: dayRange(date), label: capitalize(DAY_LABEL.format(date)) };
    },
    previous: (offset) => ({ range: dayRange(dayAt(offset + 1)), trendLabel: "vs dia anterior" }),
  },
  month: {
    key: "month",
    label: "Mês",
    current: (offset) => {
      const month = getMonthsWindow(1, offset)[0];
      return { range: monthRange(month), label: formatMonthLong(month) };
    },
    previous: (offset) => compareMonth(getMonthsWindow(1, offset)[0]),
  },
  year: {
    key: "year",
    label: "Ano",
    current: (offset) => {
      const year = localToday().year - offset;
      return { range: yearRange(year), label: String(year) };
    },
    previous: (offset) => compareYear(localToday().year - offset),
  },
};
