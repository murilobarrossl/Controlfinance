const PRODUCT_RULES = [{ match: /\bultravioleta\b/, base: "#8A05BE" }];

const CATEGORY_KEYWORDS = [
  { category: "BLACK", match: /\b(black|infinite)\b/ },
  { category: "PLATINUM", match: /\bplatinum\b/ },
  { category: "GOLD", match: /\bgold\b/ },
];

export const BLACK_BASE = "#2B2D31";
export const CATEGORY_DEFAULTS = { PLATINUM: "#9AA3AD", GOLD: "#C9A44A" };
export const NEUTRAL_BASE = "#3A3F47";
export const PRODUCT_BASES = PRODUCT_RULES.map((rule) => rule.base);

const LIGHT_TEXT = "#FFFFFF";
const DARK_TEXT = "#000000";

export function getCardBase({ name, productType, institutionColor }) {
  const normalizedName = normalize(name);
  const product = PRODUCT_RULES.find((rule) => rule.match.test(normalizedName));
  if (product) return product.base;

  const category = normalizeCategory(productType) ?? CATEGORY_KEYWORDS.find((rule) => rule.match.test(normalizedName))?.category;
  if (category === "BLACK" || category === "INFINITE") return BLACK_BASE;
  return toHex(institutionColor) ?? CATEGORY_DEFAULTS[category] ?? NEUTRAL_BASE;
}

export function cardTextColor(baseHex) {
  const l = relativeLuminance(baseHex);
  return 1.05 / (l + 0.05) >= (l + 0.05) / 0.05 ? LIGHT_TEXT : DARK_TEXT;
}

function relativeLuminance(hex) {
  const [r, g, b] = [1, 3, 5].map((i) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function normalize(value) {
  return (value ?? "").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
}

function normalizeCategory(productType) {
  return productType ? productType.trim().toUpperCase() : null;
}

function toHex(color) {
  if (!color) return null;
  const hex = color.startsWith("#") ? color : `#${color}`;
  return /^#[0-9a-f]{6}$/i.test(hex) ? hex.toUpperCase() : null;
}
