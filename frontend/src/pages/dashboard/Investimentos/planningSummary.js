import { sumMoney } from "../../../utils/importedInvestments.js";

// Enquanto algum vínculo carrega, não há total: um número parcial seria lido como o saldo real.
export function summarize(entries) {
  const ready = entries.filter((entry) => entry.status === "ready");
  const amountsOf = (entry) => (entry.item.amounts ?? []).filter(Boolean);
  return {
    count: entries.length,
    loading: entries.some((entry) => entry.status === "loading"),
    // Item que não pôde ser lido agora: o total ficaria parcial, então não é exibido.
    failed: entries.some((entry) => entry.status === "error"),
    totals: sumMoney(ready.flatMap(amountsOf)),
    withoutAmount: entries.filter((entry) => entry.status !== "error").length - ready.filter((entry) => amountsOf(entry).length).length,
    stale: ready.some((entry) => amountsOf(entry).length && entry.item.currentAmountAvailable === false),
  };
}

export function summaryNote({ count, withoutAmount, stale }) {
  const parts = [count === 1 ? "1 investimento" : `${count} investimentos`];
  if (withoutAmount) parts.push(withoutAmount === 1 ? "1 sem saldo informado" : `${withoutAmount} sem saldo informado`);
  if (stale) parts.push("inclui último saldo informado");
  return parts.join(" · ");
}

// Conta de banco vinculada à reserva antes da mudança para investimentos: o saldo é o do banco.
export function legacyAccountEntry(account) {
  const balance = typeof account.balance === "number" && Number.isFinite(account.balance) ? account.balance.toFixed(2) : null;
  return {
    key: `account:${account.id}`,
    status: "ready",
    legacy: true,
    item: { key: `account:${account.id}`, name: account.name, institution: { name: account.name }, amounts: balance ? [{ amount: balance, currency: "BRL" }] : [] },
  };
}
