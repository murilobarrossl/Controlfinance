// Illustrative application templates, never bank/product artwork or physical card colors.
// Only the structured category selects a family; editable names and bank colors do not.
const FAMILY_PALETTES = {
  BLACK: { family: "black", label: "Black", base: "#181D26", detail: "#9BAABD", finish: "satin" },
  PLATINUM: { family: "platinum", label: "Platinum", base: "#434B59", detail: "#D0DBED", finish: "brushed" },
  GOLD: { family: "gold", label: "Gold", base: "#51432F", detail: "#E4C58A", finish: "brushed" },
  INFINITE: { family: "infinite", label: "Infinite", base: "#272D49", detail: "#B0BFE9", finish: "satin" },
  GRAFITE: { family: "grafite", label: "Grafite", base: "#343D43", detail: "#B4C5CE", finish: "woven" },
  NANQUIM: { family: "nanquim", label: "Nanquim", base: "#233436", detail: "#A2C6C1", finish: "woven" },
  MAIS: { family: "mais", label: "Mais", base: "#2C3B4A", detail: "#A5C8E1", finish: "woven" },
  SIGNATURE: { family: "signature", label: "Signature", base: "#383247", detail: "#C9BADF", finish: "woven" },
  WORLD_LEGEND: { family: "world-legend", label: "World Legend", base: "#302D40", detail: "#C8C2E2", finish: "satin" },
};

const GENERIC_PALETTE = { family: null, label: null, base: "#202936", detail: "#A8BED6", finish: "matte" };

export function cardPalette({ productType } = {}) {
  if (typeof productType !== "string") return GENERIC_PALETTE;
  const category = productType.trim().toUpperCase().replace(/[\s-]+/g, "_");
  return Object.hasOwn(FAMILY_PALETTES, category) ? FAMILY_PALETTES[category] : GENERIC_PALETTE;
}

// Contrast helper also used by institution initials in BankLogo.
const LIGHT_TEXT = "#FFFFFF";
const DARK_TEXT = "#000000";

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

