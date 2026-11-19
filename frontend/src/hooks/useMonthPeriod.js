import { useMemo } from "react";
import { formatMonthLong, toIsoDate } from "../utils/monthLabel.js";
import { getMonthsWindow, monthRange } from "../utils/monthlyTrend.js";
import { yearRange } from "../utils/periods.js";

export function periodParams(params, key, mode) {
  const next = new URLSearchParams(params ?? undefined);
  next.set("mes", key);
  next.set("periodo", mode);
  return next;
}

export default function useMonthPeriod(value, selectedMode, min = "0001-01") {
  const max = toIsoDate(getMonthsWindow(1)[0]).slice(0, 7);
  const key = /^\d{4}-(0[1-9]|1[0-2])$/.test(value) && value >= min && value <= max ? value : max;
  const date = useMemo(() => new Date(key + "-01T00:00:00Z"), [key]);
  const annual = selectedMode === "ano";
  const range = useMemo(() => annual ? yearRange(date.getUTCFullYear()) : monthRange(date), [date, annual]);
  const label = annual ? key.slice(0, 4) : formatMonthLong(date);
  return { key, date, min, max, mode: annual ? "ano" : "mes", annual, range, label };
}
