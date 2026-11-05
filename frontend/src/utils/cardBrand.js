function normalizeName(value) {
  return String(value ?? "")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
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
  if (source.isSynced === false) return { name: "Cartão manual", isManual: true };
  const name = source.bankName || "Banco conectado";
  const connector = connectorsMap.get(normalizeName(name));
  return {
    name,
    logoUrl: connector?.logoUrl,
    color: normalizeColor(connector?.color ?? source.color ?? source.institutionColor),
  };
}

export function cardPickerOption(card, brand) {
  return {
    value: card.id,
    title: brand.name,
    subtitle: card.name,
    color: brand.color,
    logo: brand.isManual ? null : brand,
  };
}
