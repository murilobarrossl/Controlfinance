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

function yearRange(year) {
  return { from: `${year}-01-01`, to: `${year + 1}-01-01` };
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
    previous: (offset) => ({ range: monthRange(getMonthsWindow(1, offset + 1)[0]), trendLabel: "vs mês anterior" }),
  },
  year: {
    key: "year",
    label: "Ano",
    current: (offset) => {
      const year = localToday().year - offset;
      return { range: yearRange(year), label: String(year) };
    },
    // A base inclui pendentes/agendados do ano inteiro. A comparação usa o ano
    // inteiro anterior também, sem misturar um ano completo com um recorte até hoje.
    previous: (offset) => ({ range: yearRange(localToday().year - offset - 1), trendLabel: "vs ano anterior" }),
  },
};
