import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { financialTone, transactionTone } from "../../src/utils/financialTone.js";
import { computeTrend } from "../../src/utils/trend.js";
import { donutSliceColors, OTHERS_COLOR, resolveCategoryColor } from "../../src/utils/categoryColors.js";
import { DONUT_SHADES, NEUTRAL_DONUT_SHADES } from "../../src/components/charts/chartTheme.js";

const css = readFileSync(new URL("../../src/index.css", import.meta.url), "utf8");
const tokens = Object.fromEntries([...css.matchAll(/(--[\w-]+):\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]));
const resolve = (value) => value.replace(/var\((--[\w-]+)\)/g, (_, name) => resolve(tokens[name]));
const rgb = (hex) => hex.slice(1).match(/../g).map((value) => parseInt(value, 16));
function luminance(channels) {
  const [r, g, b] = channels.map((value) => {
    const c = value / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return r * 0.2126 + g * 0.7152 + b * 0.0722;
}
function contrast(a, b) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((a, b) => b - a);
  return (hi + 0.05) / (lo + 0.05);
}

test("texto semântico atende AA nas superfícies escuras e nos fundos de status", () => {
  for (const role of ["positive", "negative", "warning", "neutral"]) {
    const fg = rgb(resolve(tokens[`--color-${role}`]));
    const opacity = Number(resolve(tokens[`--color-${role}-bg`]).match(/(\d+)%/)[1]) / 100;
    for (const surface of ["bg", "bg-alt", "surface", "surface-alt", "surface-hover"]) {
      const bg = rgb(tokens[`--color-${surface}`]);
      const tinted = fg.map((c, i) => c * opacity + bg[i] * (1 - opacity));
      for (const background of [bg, tinted]) {
        const ratio = contrast(fg, background);
        assert.ok(ratio >= 4.5, `${role} sobre ${surface}: ${ratio.toFixed(2)}:1`);
      }
    }
  }
  assert.ok(contrast(rgb(tokens["--color-danger-strong"]), rgb(tokens["--color-text-primary"])) >= 4.5);
});

test("paletas de rosca têm contraste não textual de pelo menos 3:1", () => {
  for (const color of [...DONUT_SHADES, ...NEUTRAL_DONUT_SHADES]) {
    for (const surface of ["surface", "surface-alt", "surface-hover"]) {
      assert.ok(contrast(rgb(resolve(color)), rgb(tokens[`--color-${surface}`])) >= 3);
    }
  }
});

test("zero exibido, dados ausentes e transferências permanecem neutros", () => {
  for (const value of [0, -0, 0.001, -0.001, null, undefined, NaN, Infinity]) {
    assert.equal(financialTone(value), "default");
    assert.equal(transactionTone("Expense", value), "default");
  }
  assert.equal(financialTone(10), "income");
  assert.equal(financialTone(-10), "expense");
  assert.equal(transactionTone("Income", 100, true), "default");
  assert.equal(transactionTone("Expense", 100, true), "default");
  assert.equal(transactionTone("Unknown", 100), "default");
  assert.equal(transactionTone("Expense", 100), "expense");
  assert.equal(transactionTone("Income", 100), "income");
});

test("comparações mantêm a inversão de despesas e neutralizam variações exibidas como zero", () => {
  assert.equal(computeTrend(120, 100).tone, "positive");
  assert.equal(computeTrend(120, 100, { invertTone: true }).tone, "negative");
  assert.equal(computeTrend(80, 100, { invertTone: true }).tone, "positive");
  assert.equal(computeTrend(80, 100, { invertTone: true }).direction, "down");
  for (const invertTone of [false, true]) {
    assert.equal(computeTrend(100, 100, { invertTone }).tone, "neutral");
    assert.equal(computeTrend(100.001, 100, { invertTone }).tone, "neutral");
  }
});

test("categorias antigas não reintroduzem marca e a paleta não modifica os dados", () => {
  const categories = [{ name: "Moradia", color: "#ED4A31" }, { name: "Salário", color: "#4ECDC4" }];
  const before = JSON.stringify(categories);
  for (const { name } of categories) {
    const color = resolveCategoryColor(name, categories);
    assert.match(color, /^var\(--color-category-\d\)$/);
    assert.equal(color, resolveCategoryColor(name, [...categories].reverse()));
  }
  assert.equal(JSON.stringify(categories), before);
  assert.deepEqual(donutSliceColors([{ name: "Moradia" }, { name: "Outros", color: OTHERS_COLOR }, { name: "Saúde" }]), [DONUT_SHADES[0], OTHERS_COLOR, DONUT_SHADES[1]]);
});
