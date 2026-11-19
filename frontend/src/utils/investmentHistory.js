import { dateLabel, formatMoney, moneyField, sumMoney, validMoney } from "./importedInvestments.js";

function observationMoney(row, reserved, field) {
  if (!reserved) return moneyField(row.payload, field);
  const amounts = row.payload?.values?.available_amount;
  return row.payload?.availability?.available_amount === "available" && amounts?.length === 1 ? validMoney(amounts[0]) : null;
}
export function historyPoints(rows, reserved, field) {
  const grouped = new Map();
  for (const row of rows) {
    const reference = reserved ? row.observedAt : row.referenceAt;
    if (!reference || Number.isNaN(new Date(reference).getTime()) || new Date(reference).getTime() > Date.now()) continue;
    const list = grouped.get(reference) ?? []; list.push(row); grouped.set(reference, list);
  }
  return [...grouped.entries()].sort(([a], [b]) => new Date(a) - new Date(b)).map(([reference, versions]) => {
    const timestamps = versions.map((row) => new Date(row.payload?.values?.updated_at).getTime());
    const stamped = timestamps.every((value) => Number.isFinite(value));
    const newest = stamped ? Math.max(...timestamps) : null;
    const candidates = stamped ? versions.filter((_, index) => timestamps[index] === newest) : versions;
    const monies = candidates.map((row) => observationMoney(row, reserved, field));
    const conflict = monies.some((m) => !m) || new Set(monies.filter(Boolean).map((m) => `${m.currency}:${m.amount}`)).size > 1;
    const original = candidates[0]?.payload?.values;
    return { key: reference, referenceAt: reference, period: dateLabel(reserved ? reference : original?.reference_date_time ?? original?.reference_date ?? reference),
      money: conflict ? null : monies[0], time: new Date(reference).getTime(), collected: reserved };
  });
}
function negate(amount) {
  return amount.startsWith("-") ? amount.slice(1) : `-${amount}`;
}
export function signedMoney(value) {
  const zero = !/[1-9]/.test(value.amount);
  return `${zero ? "" : value.amount.startsWith("-") ? "−" : "+"}${formatMoney({ ...value, amount: value.amount.replace(/^[+-]/, "") })}`;
}
// Diferença entre o primeiro e o último saldo registrados pelo banco (mesma moeda), em decimal exato. Mostra
// como o saldo mudou; não é rendimento: aportes, resgates e impostos também mexem no saldo.
export function balanceVariation(points) {
  const valid = points.filter((point) => point.money);
  if (valid.length < 2) return null;
  const first = valid[0]; const last = valid[valid.length - 1];
  const [difference] = sumMoney([last.money, { ...first.money, amount: negate(first.money.amount) }]);
  return difference ? { money: difference, from: first.period, to: last.period } : null;
}
