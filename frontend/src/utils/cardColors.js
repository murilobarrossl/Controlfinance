// Ordem de decisão: produto reconhecível pelo nome → categoria (product_type da Polp, ou palavra
// no nome do cartão manual) → cor da instituição → degradê neutro.

const PRODUCT_RULES = [
  { match: /\bultravioleta\b/, from: "#8A05BE", to: "#0B0B0F" },
];

const BLACK_TIER = { from: "#3A3A3D", to: "#050505" };

const CATEGORY_KEYWORDS = [
  { category: "BLACK", match: /\b(black|infinite)\b/ },
  { category: "PLATINUM", match: /\bplatinum\b/ },
  { category: "GOLD", match: /\bgold\b/ },
];

const CATEGORY_DEFAULTS = {
  PLATINUM: { from: "#9AA3AD", to: "#3D434B" },
  GOLD: { from: "#C9A44A", to: "#5E4A1C" },
};

const NEUTRAL = { from: "#3A3F47", to: "#141619" };

const PLATINUM_TINT = "#2B2F36";
const GOLD_TINT = "#B8912F";
const SHADE = "#000000";

const cache = new Map();

export function getCardGradient({ name, productType, institutionColor }) {
  const key = `${name ?? ""}|${productType ?? ""}|${institutionColor ?? ""}`;
  let result = cache.get(key);
  if (!result) {
    result = withTextColor(resolve(normalize(name), normalizeCategory(productType), toHex(institutionColor)));
    cache.set(key, result);
  }
  return result;
}

function resolve(name, productType, institutionColor) {
  const product = PRODUCT_RULES.find((rule) => rule.match.test(name));
  if (product) return product;

  const category = productType ?? CATEGORY_KEYWORDS.find((rule) => rule.match.test(name))?.category;
  if (category === "BLACK" || category === "INFINITE") return BLACK_TIER;

  if (institutionColor) {
    if (category === "PLATINUM") return { from: institutionColor, to: mix(institutionColor, PLATINUM_TINT, 0.65) };
    if (category === "GOLD") return { from: mix(institutionColor, GOLD_TINT, 0.35), to: mix(institutionColor, SHADE, 0.55) };
    return { from: institutionColor, to: mix(institutionColor, SHADE, 0.5) };
  }

  return CATEGORY_DEFAULTS[category] ?? NEUTRAL;
}

function withTextColor(gradient) {
  const light = luminance(mix(gradient.from, gradient.to, 0.35)) > 0.45;
  return { ...gradient, text: light ? "#141414" : "#FFFFFF", textMuted: light ? "rgba(20, 20, 20, 0.72)" : "rgba(255, 255, 255, 0.72)" };
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

function channels(hex) {
  return [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
}

function mix(a, b, weightOfB) {
  const ca = channels(a);
  const cb = channels(b);
  return `#${ca
    .map((v, i) => Math.round(v + (cb[i] - v) * weightOfB).toString(16).padStart(2, "0"))
    .join("")
    .toUpperCase()}`;
}

function luminance(hex) {
  const [r, g, b] = channels(hex).map((v) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
