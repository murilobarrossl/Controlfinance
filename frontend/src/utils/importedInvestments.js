const DECIMAL = /^[+-]?\d+(?:\.\d+)?$/;
export function exactDecimal(value) {
  if (typeof value !== "string" || value.length > 260 || !DECIMAL.test(value)) return null;
  const [whole, fraction = ""] = value.replace(/^[+-]/, "").split(".");
  return { units: BigInt(`${value.startsWith("-") ? "-" : ""}${whole}${fraction}`), scale: fraction.length };
}
export function formatExact(value, minimumFraction = 0) {
  if (!exactDecimal(value)) return "Não informado";
  const negative = value.startsWith("-");
  const [raw, fractional = ""] = value.replace(/^[+-]/, "").split(".");
  const whole = raw.replace(/^0+(?=\d)/, "").replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  const fraction = fractional.replace(/0+$/, "").padEnd(minimumFraction, "0");
  return `${negative ? "−" : ""}${whole}${fraction ? `,${fraction}` : ""}`;
}
export function validMoney(value) {
  return value && exactDecimal(value.amount) && /^[A-Z]{3}$/.test(value.currency ?? "") ? value : null;
}
export function formatMoney(value) {
  if (!validMoney(value)) return "Não informado";
  return `${value.currency === "BRL" ? "R$" : value.currency} ${formatExact(value.amount, 2)}`;
}
export const investmentPath = (item) => `/dashboard/investimentos/${item.reserved ? "caixinhas/" : ""}${encodeURIComponent(item.id)}`;
export function formatContractNumber(value) {
  const decimal = exactDecimal(value);
  return decimal ? formatExact(value, decimal.scale) : "Não informado";
}
export function moneyField(document, field, allowLastKnown = false) {
  if (!allowLastKnown && document?.availability?.[field] !== "available") return null;
  return validMoney(document?.values?.[field]);
}
export function dateLabel(value) {
  if (!value) return "Não informado";
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return value.split("-").reverse().join("/");
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Não informado" : date.toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });
}
export const MODALITIES = {
  reserved: { key: "reserved", groupLabel: "Caixinhas e porquinhos", label: "Saldo reservado em conta", color: "var(--color-neutral)" },
  CDB: { key: "CDB", groupLabel: "CDB", label: "CDB", color: "var(--color-neutral)" },
  RDB: { key: "RDB", groupLabel: "RDB", label: "RDB", color: "var(--color-neutral)" },
  LCI: { key: "LCI", groupLabel: "LCI", label: "LCI", color: "var(--color-neutral)" },
  LCA: { key: "LCA", groupLabel: "LCA", label: "LCA", color: "var(--color-neutral)" },
  DEBENTURES: { key: "DEBENTURES", groupLabel: "Debêntures", label: "Debênture", color: "var(--color-neutral)" },
  CRI: { key: "CRI", groupLabel: "CRI", label: "CRI", color: "var(--color-neutral)" },
  CRA: { key: "CRA", groupLabel: "CRA", label: "CRA", color: "var(--color-neutral)" },
  "treasure-titles": { key: "treasure-titles", groupLabel: "Tesouro Direto", label: "Tesouro Direto", color: "var(--color-neutral)" },
  funds: { key: "funds", groupLabel: "Fundos de investimento", label: "Fundo", color: "var(--color-neutral)" },
  "variable-incomes": { key: "variable-incomes", groupLabel: "Renda variável", label: "Renda variável", color: "var(--color-neutral)" },
  other: { key: "other", groupLabel: "Outros investimentos", label: "Investimento", color: "var(--color-neutral)" },
};
export const STATE_TEXT = {
  checking: "Confirmando os dados com o banco.",
  resource_access_unconfirmed: "O banco ainda não confirmou o acesso aos investimentos.",
  available: "Dados disponíveis.",
  awaiting_sync: "Aguardando os dados do banco.",
  sync_delayed: "A atualização está atrasada.",
  temporarily_unavailable: "Dados temporariamente indisponíveis no banco.",
  information_not_provided: "Informação ainda não enviada pelo banco.",
  pending_authorization: "Aguardando autorização no banco.",
  authorization_required: "O acesso aos investimentos não foi autorizado nesta conexão.",
  not_authorized: "O banco ainda não confirmou o acesso a este investimento.",
  expired: "Autorização expirada.",
  revoked: "Autorização revogada.",
  rejected: "Autorização recusada.",
  temporary_error: "Não foi possível atualizar agora. Os últimos dados conhecidos foram mantidos.",
  resource_unavailable: "O banco informou investimentos indisponíveis.",
  none_reported: "O banco não informou investimentos.",
  historical_source: "Dados de uma autorização anterior.",
};
export function normalizeInvestment(resource, reserved, connections) {
  const connection = connections.find((c) => c.id === resource.integrationId);
  const modality = reserved ? MODALITIES.reserved : MODALITIES[resource.productType] ?? MODALITIES[resource.family] ?? MODALITIES.other;
  const identity = resource.identity?.values ?? {};
  const name = resource.name || identity.ticker || modality.label;
  const document = reserved ? resource.document : resource.position?.document;
  const gross = reserved ? null : moneyField(document, "gross_amount", true);
  const net = reserved ? null : moneyField(document, "net_amount", true);
  const amounts = reserved ? (document?.values?.available_amount ?? []).map(validMoney) : [gross ?? net].filter(Boolean);
  const amountField = reserved ? "available_amount" : gross ? "gross_amount" : "net_amount";
  const evidence = document?.provenance?.[amountField];
  const reference = evidence?.reference_at;
  let state = connection ? reserved ? connection.reservedStatus : connection.status : "awaiting_sync";
  if (!reserved && connection?.investmentConsentId && connection.investmentConsentId !== resource.sourceConsentId) state = "historical_source";
  return { ...resource, key: `${reserved ? "reserved" : "investment"}:${resource.id}`, reserved, name, modality,
    institution: { name: connection?.institutionName || "Instituição conectada", logoUrl: connection?.institutionLogoUrl ?? null }, connection, state, amounts, document,
    balanceLabel: reserved ? "Saldo reservado" : gross ? "Saldo bruto" : net ? "Saldo líquido" : "Saldo não informado",
    referenceValue: reference && ["funds", "variable-incomes"].includes(resource.family) ? reference.slice(0, 10)
      : reference ?? document?.values?.reference_date_time ?? document?.values?.reference_date ?? resource.position?.referenceAt,
    valueObservedAt: evidence?.observed_at ?? resource.lastObservedAt,
    currentAmountAvailable: document?.availability?.[amountField] === "available" && state === "available"
      && resource.catalogPresence?.status !== "not_reported_in_last_full_scan",
  };
}
const idsFor = (item, owner) => (item.reserved ? owner?.reservedBalanceIds : owner?.investmentIds) ?? [];
export function goalOf(item, goals) {
  return (goals ?? []).find((goal) => idsFor(item, goal).includes(item.id)) ?? null;
}
// A reserva é identificada pelo servidor (caixinha com nome de reserva); a tela só consulta.
export function isReserve(item, reserve) {
  return item.reserved && (reserve?.reservedBalanceIds ?? []).includes(item.id);
}
export function sumMoney(values) {
  const totals = new Map();
  for (const money of values) {
    const decimal = validMoney(money) && exactDecimal(money.amount);
    if (!decimal) continue;
    const current = totals.get(money.currency) ?? { units: 0n, scale: 0 };
    const scale = Math.max(current.scale, decimal.scale);
    const units = current.units * 10n ** BigInt(scale - current.scale) + decimal.units * 10n ** BigInt(scale - decimal.scale);
    totals.set(money.currency, { units, scale });
  }
  return [...totals.entries()].map(([currency, { units, scale }]) => {
    const negative = units < 0n;
    const digits = (negative ? -units : units).toString().padStart(scale + 1, "0");
    const amount = scale ? `${digits.slice(0, -scale)}.${digits.slice(-scale)}` : digits;
    return { amount: `${negative ? "-" : ""}${amount}`, currency };
  });
}

// Only normalized geometric coordinates enter the chart library. Exact monetary strings
// remain the sole source for labels, tables and tooltips; no Number(amount) or currency sums.
export function chartCoordinates(points) {
  const valid = points.map((p) => exactDecimal(p.money?.amount)).filter(Boolean);
  if (valid.length === 0) return [];
  const scale = Math.max(...valid.map((x) => x.scale));
  const units = valid.map((x) => x.units * 10n ** BigInt(scale - x.scale));
  const min = units.reduce((a, b) => a < b ? a : b);
  const max = units.reduce((a, b) => a > b ? a : b);
  return points.map((point) => {
    const value = exactDecimal(point.money?.amount);
    const coordinate = value ? max === min ? 500000 : Number((value.units * 10n ** BigInt(scale - value.scale) - min) * 1000000n / (max - min)) : null;
    return { ...point, coordinate };
  });
}
