import { getAccountKindLabel, isCreditCardAccount } from "./accountKind.js";

// Um grupo por conexão bancária (cabeçalho + uma linha por conta, sem cartões) e um grupo sem
// cabeçalho por conta manual. Conexão que não está AUTHORISED tem dados congelados, por isso o
// status vai no rótulo do banco.
const STATUS_LABELS = {
  EXPIRED: "conexão expirada",
  REVOKED: "conexão revogada",
  REJECTED: "conexão revogada",
};

const KIND_ORDER = ["Conta corrente", "Conta de pagamento", "Poupança"];

function statusLabel(status) {
  if (!status || status === "AUTHORISED") return null;
  return STATUS_LABELS[status] ?? "conexão inativa";
}

function normalizeColor(raw) {
  if (!raw) return null;
  return raw.startsWith("#") ? raw : `#${raw}`;
}

function kindRank(kindLabel) {
  const rank = KIND_ORDER.indexOf(kindLabel);
  return rank === -1 ? KIND_ORDER.length : rank;
}

function compareItems(a, b) {
  return (
    kindRank(a.kindLabel) - kindRank(b.kindLabel) ||
    a.name.localeCompare(b.name, "pt-BR") ||
    (a.number ?? "").localeCompare(b.number ?? "") ||
    a.accountId.localeCompare(b.accountId)
  );
}

function repeatedNames(accounts) {
  const seen = new Set();
  const repeated = new Set();
  for (const { name } of accounts) (seen.has(name) ? repeated : seen).add(name);
  return repeated;
}

export function buildScopes(accounts, connectors) {
  const connectorsById = new Map(connectors.map((c) => [c.id, c]));
  const byKey = new Map();

  for (const account of accounts) {
    if (isCreditCardAccount(account)) continue;
    const connectionId = account.localIntegrationId ?? null;
    const key = connectionId ? `conn:${connectionId}` : `acct:${account.id}`;
    let group = byKey.get(key);
    if (!group) {
      const connector = connectorsById.get(account.bankCode);
      group = {
        key,
        connectionId,
        baseLabel: (connectionId && connector?.name) || account.name,
        color: normalizeColor(connector?.color),
        status: connectionId ? account.connectionStatus ?? null : null,
        connectedAt: account.connectedAt ?? null,
        lastSyncedAt: account.lastSyncedAt ?? null,
        enrichmentPending: Boolean(account.enrichmentPending),
        canSyncBalance: false,
        accounts: [],
      };
      byKey.set(key, group);
    }
    group.accounts.push(account);
    group.canSyncBalance ||= Boolean(account.canSyncBalance);
  }

  const raw = [...byKey.values()];
  const collisions = new Map();
  for (const g of raw) {
    const id = `${g.baseLabel}|${g.status}`;
    collisions.set(id, (collisions.get(id) ?? 0) + 1);
  }

  const groups = raw
    .map((g) => {
      const since =
        g.connectionId && g.connectedAt && collisions.get(`${g.baseLabel}|${g.status}`) > 1
          ? `desde ${new Date(g.connectedAt).toLocaleDateString("pt-BR")}`
          : null;
      const repeated = repeatedNames(g.accounts);
      const items = g.accounts
        .map((account) => {
          const number = repeated.has(account.name) ? account.number ?? null : null;
          return {
            key: `acct:${account.id}`,
            accountId: account.id,
            connectionId: g.connectionId,
            color: g.color,
            lastSyncedAt: g.lastSyncedAt,
            enrichmentPending: g.enrichmentPending,
            canSyncBalance: g.canSyncBalance,
            name: account.name,
            number,
            kindLabel: getAccountKindLabel(account),
            label: [account.name, number].filter(Boolean).join(" "),
          };
        })
        .sort(compareItems);
      return {
        key: g.key,
        connectionId: g.connectionId,
        baseLabel: g.baseLabel,
        color: g.color,
        label: [g.baseLabel, statusLabel(g.status), since].filter(Boolean).join(" · "),
        items,
      };
    })
    .sort((a, b) => a.label.localeCompare(b.label, "pt-BR") || a.key.localeCompare(b.key));

  return { groups, items: groups.flatMap((g) => g.items) };
}

// "conn:<id>" salvo pelo seletor antigo cai na primeira conta daquele banco; qualquer outro valor
// que não existe mais (conta removida, cartão) cai no primeiro item.
export function resolveScope(items, key) {
  if (items.length === 0) return null;
  const direct = items.find((s) => s.key === key);
  if (direct) return direct;
  const connectionId = key?.startsWith("conn:") ? key.slice("conn:".length) : null;
  return (connectionId && items.find((s) => s.connectionId === connectionId)) || items[0];
}
