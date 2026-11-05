import { readInstitutions } from "./lib/bank-brand-colors.mjs";
import { cardTextColor, BLACK_BASE, CATEGORY_DEFAULTS, NEUTRAL_BASE, PRODUCT_BASES } from "../src/utils/cardColors.js";

const MIN_CONTRAST = 4.5;

const luminance = (hex) => {
  const [r, g, b] = [0, 2, 4].map((i) => {
    const c = parseInt(hex.slice(1 + i, 3 + i), 16) / 255;
    return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
const contrast = (a, b) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

const bases = [
  ...readInstitutions().map(({ name, hex }) => ({ name, hex })),
  ...PRODUCT_BASES.map((hex) => ({ name: "produto", hex })),
  { name: "Black/Infinite", hex: BLACK_BASE },
  ...Object.entries(CATEGORY_DEFAULTS).map(([name, hex]) => ({ name, hex })),
  { name: "neutra", hex: NEUTRAL_BASE },
];

let failures = 0;
for (const { name, hex } of bases) {
  const text = cardTextColor(hex);
  const ratio = contrast(hex, text);
  if (ratio < MIN_CONTRAST) {
    failures++;
    console.error(`FALHA ${name} ${hex}: texto ${text} com contraste ${ratio.toFixed(2)}:1`);
  }
}

if (failures > 0) process.exit(1);
console.log(`${bases.length} cores conferidas: todo texto do cartão tem contraste de pelo menos ${MIN_CONTRAST}:1.`);
