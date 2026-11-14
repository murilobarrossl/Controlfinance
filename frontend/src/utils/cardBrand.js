function normalizeName(value) {
  return String(value ?? "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]/g, "");
}

function normalizeColor(raw) {
  if (!raw) return undefined;
  return raw.startsWith("#") ? raw : `#${raw}`;
}

export function connectorsByName(connectors) {
  return new Map(connectors.map((c) => [normalizeName(c.name), c]));
}

export function cardBrand(source, connectorsMap) {
  const name = source.bankName || "Banco conectado";
  const connector = connectorsMap.get(normalizeName(name));
  return {
    name,
    logoUrl: connector?.logoUrl,
    color: normalizeColor(connector?.color ?? source.color ?? source.institutionColor),
  };
}

// Bandeira a partir de credit_card_network. "OUTRAS" não identifica nada e não é exibida; um valor
// fora da lista aparece como texto, sem símbolo.
const NETWORKS = [
  { id: "mastercard", label: "Mastercard", match: /^master[ _]?card$/ },
  { id: "visa", label: "Visa", match: /^visa$/ },
  { id: "amex", label: "American Express", match: /^(american[ _]express|amex)$/ },
  { id: "diners", label: "Diners Club", match: /^diners([ _]club)?$/ },
  { id: "hipercard", label: "Hipercard", match: /^hipercard$/ },
  { id: "elo", label: "Elo", match: /^elo$/ },
  { id: null, label: "Bandeira própria", match: /^bandeira[ _]?propria$/ },
  { id: null, label: "Cheque eletrônico", match: /^cheque[ _]?eletronico$/ },
];

export function cardNetwork(raw) {
  const value = String(raw ?? "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .trim()
    .toLowerCase();
  if (!value || value === "outras" || value === "outra") return null;
  const known = NETWORKS.find((n) => n.match.test(value));
  if (known) return { id: known.id, label: known.label };
  const label = value.replace(/_/g, " ").replace(/\b\p{L}/gu, (c) => c.toUpperCase());
  return { id: null, label };
}

export function cardPickerOption(card, brand) {
  return {
    value: card.id,
    title: brand.name,
    subtitle: card.name,
    color: brand.color,
    logo: brand,
  };
}
