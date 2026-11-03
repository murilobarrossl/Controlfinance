// Verificação das cores dos cartões: node scripts/verify-card-colors.mjs
// Conversão, composição das camadas e contraste são reimplementados aqui de propósito, para não
// validar cardColors.js com o próprio código dele.
import { readInstitutions } from "./lib/bank-brand-colors.mjs";
import { PRECOMPUTED_PALETTES } from "../src/utils/cardColors.generated.js";
import {
  cardPalette,
  getCardGradient,
  paletteKey,
  precomputedCombinations,
  CHIP_WARM_HUES,
  MAX_TEXT_SHADOW,
  SHEEN_ALPHA,
  SHADE_ALPHA,
  MUTED_ALPHA,
  LARGE_TEXT_CONTRAST,
  SMALL_TEXT_CONTRAST,
  NEUTRAL_MAX_CHROMA,
  DARK_MIN_CHROMA,
  MIN_LIGHTNESS_RANGE,
  TEXT_REGIONS,
  CARD_ASPECT,
  GLOSS_GEOMETRY,
  SHEEN_GEOMETRY,
  SHADE_GEOMETRY,
  FOOTER_GEOMETRY,
  MAX_FOOTER_SHADE,
} from "../src/utils/cardColors.js";

const institutions = readInstitutions();

const srgbToLinear = (c) => (c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4));
const linearToSrgb = (c) => (c <= 0.0031308 ? 12.92 * c : 1.055 * Math.pow(c, 1 / 2.4) - 0.055);
const rgb = (hex) => [0, 2, 4].map((i) => parseInt(hex.slice(1 + i, 3 + i), 16));

function oklchToLinearRgb(L, C, H) {
  const a = C * Math.cos((H / 180) * Math.PI);
  const b = C * Math.sin((H / 180) * Math.PI);
  const l = Math.pow(L + 0.3963377774 * a + 0.2158037573 * b, 3);
  const m = Math.pow(L - 0.1055613458 * a - 0.0638541728 * b, 3);
  const s = Math.pow(L - 0.0894841775 * a - 1.291485548 * b, 3);
  return [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ];
}

function hexToOklch(hex) {
  const [r, g, b] = rgb(hex).map((v) => srgbToLinear(v / 255));
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  const A = 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s;
  const B = 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s;
  return {
    l: 0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s,
    c: Math.sqrt(A * A + B * B),
    h: (((Math.atan2(B, A) * 180) / Math.PI) % 360 + 360) % 360,
  };
}

function toHexFromOklch(L, C, H) {
  return (
    "#" +
    oklchToLinearRgb(L, C, H)
      .map((v) => Math.round(Math.min(1, Math.max(0, linearToSrgb(Math.min(1, Math.max(0, v))))) * 255))
      .map((v) => v.toString(16).padStart(2, "0"))
      .join("")
      .toUpperCase()
  );
}

const luminance = (c) => {
  const [r, g, b] = c.map((v) => srgbToLinear(v / 255));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
const ratio = (a, b) => {
  const x = luminance(a);
  const y = luminance(b);
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
};
const blend = (bg, fg, alpha) => bg.map((v, i) => v * (1 - alpha) + fg[i] * alpha);
const hueDistance = (a, b) => Math.abs(((a - b + 540) % 360) - 180);

// Mede o texto nas regiões onde ele aparece, com a mesma ordem de pintura do CSS: degradê,
// reflexo, sombra interna, sombra do texto (camada da cor dela sob a letra), texto e, por cima,
// a faixa de brilho do ::after.
function pointBackground(p, x, y) {
  const [light, base, dark] = [p.light, p.base, p.dark].map(rgb);
  const W = CARD_ASPECT;
  const along = (x * W * Math.SQRT1_2 + y * Math.SQRT1_2) / ((W + 1) * Math.SQRT1_2);
  let bg = along <= 0.5 ? blend(light, base, along * 2) : blend(base, dark, (along - 0.5) * 2);
  const [gr, gg, gb, ga] = p.glossLayer;
  const radius = Math.sqrt((x / GLOSS_GEOMETRY.rx) ** 2 + (y / GLOSS_GEOMETRY.ry) ** 2);
  if (radius < GLOSS_GEOMETRY.end) bg = blend(bg, [gr, gg, gb], ga * (1 - radius / GLOSS_GEOMETRY.end));
  // Faixa do rodapé: linear-gradient(to top, cor 0%, cor 18%, transparent 38%).
  const { solidFromY, fadeFromY } = FOOTER_GEOMETRY;
  const footerWeight = y >= solidFromY ? 1 : y <= fadeFromY ? 0 : (y - fadeFromY) / (solidFromY - fadeFromY);
  bg = blend(bg, [0, 0, 0], p.footerAlpha * footerWeight);
  const fromBottom = (1 - y) * SHADE_GEOMETRY.minCardHeightPx;
  return blend(bg, [0, 0, 0], SHADE_ALPHA * gaussianTail((fromBottom + SHADE_GEOMETRY.gapPx) / SHADE_GEOMETRY.sigmaPx));
}

// P(Z > z) por integração numérica (Simpson), independente da aproximação usada em cardColors.js.
function gaussianTail(z) {
  const n = 2000;
  const upper = z + 12;
  const h = (upper - z) / n;
  const f = (x) => Math.exp((-x * x) / 2) / Math.sqrt(2 * Math.PI);
  let sum = f(z) + f(upper);
  for (let i = 1; i < n; i++) sum += f(z + i * h) * (i % 2 ? 4 : 2);
  return (sum * h) / 3;
}

function pointSheen(x, y) {
  const rad = (SHEEN_GEOMETRY.angle / 180) * Math.PI;
  const W = CARD_ASPECT;
  const length = Math.abs(W * Math.sin(rad)) + Math.abs(Math.cos(rad));
  const pos = ((x * W - W / 2) * Math.sin(rad) - (y - 0.5) * Math.cos(rad)) / length + 0.5;
  const { from, peak, to } = SHEEN_GEOMETRY;
  if (pos <= from || pos >= to) return 0;
  return SHEEN_ALPHA * (pos <= peak ? (pos - from) / (peak - from) : (to - pos) / (to - peak));
}

function minContrast(p, shadowAlpha, regions) {
  const text = rgb(p.text);
  const shadow = p.text === "#FFFFFF" ? [0, 0, 0] : [255, 255, 255];
  let worst = Infinity;
  for (const region of regions)
    for (let i = 0; i <= 16; i++)
      for (let j = 0; j <= 12; j++) {
        const x = region.x[0] + ((region.x[1] - region.x[0]) * i) / 16;
        const y = region.y[0] + ((region.y[1] - region.y[0]) * j) / 12;
        const behind = blend(pointBackground(p, x, y), shadow, shadowAlpha);
        const sheen = pointSheen(x, y);
        const bg = blend(behind, [255, 255, 255], sheen);
        for (const glyph of [text, blend(behind, text, MUTED_ALPHA)]) worst = Math.min(worst, ratio(blend(glyph, [255, 255, 255], sheen), bg));
      }
  return worst;
}

function check(label, baseHex, finish, accentHex = null) {
  const p = cardPalette(baseHex, finish, accentHex);
  const base = hexToOklch(baseHex);
  const problems = [];

  if (p.base !== baseHex.toUpperCase()) problems.push(`base ${p.base} diferente da cor oficial`);

  for (const [name, s] of [["claro", p.oklch[0]], ["base", p.oklch[1]], ["escuro", p.oklch[2]]])
    if (oklchToLinearRgb(s.l, s.c, s.h).some((v) => v < -1e-6 || v > 1 + 1e-6)) problems.push(`tom ${name} fora do gamut`);

  const [light, dark] = [hexToOklch(p.light), hexToOklch(p.dark)];
  if (!(light.l >= base.l - 0.002 && base.l >= dark.l - 0.002)) problems.push("ordem claro ≥ base ≥ escuro quebrada");
  if (light.l - dark.l < MIN_LIGHTNESS_RANGE - 0.002) problems.push(`variação de luminosidade ${(light.l - dark.l).toFixed(3)}`);

  if (!p.neutral) {
    const required = Math.min(DARK_MIN_CHROMA, base.c);
    if (p.oklch[2].c < required - 1e-9) problems.push(`croma escuro ${p.oklch[2].c.toFixed(4)} < ${required.toFixed(4)}`);
    if (dark.c < required - 0.004) problems.push(`croma escuro renderizado ${dark.c.toFixed(4)}`);
    if (hueDistance(p.oklch[2].h, base.h) > 15 + 1e-9) problems.push("giro de matiz acima de 15°");
  } else if (base.c >= NEUTRAL_MAX_CHROMA) {
    problems.push("cor com croma tratada como neutra");
  } else if (p.oklch.some((s) => s.h !== base.h)) {
    problems.push("neutra com matiz girado");
  }

  for (const size of ["large", "small"])
    if (!(p.shadowAlpha[size] >= 0 && p.shadowAlpha[size] <= MAX_TEXT_SHADOW + 1e-9))
      problems.push(`sombra do texto ${size === "large" ? "grande" : "pequeno"} ${p.shadowAlpha[size]} acima de ${MAX_TEXT_SHADOW}`);

  if (p.noise !== (p.text === "#FFFFFF" ? "darken" : "lighten")) problems.push("ruído na direção da cor do texto");

  // Ajustes automáticos: faixa do rodapé só com texto branco, tom escuro elevado só com texto
  // escuro, nunca os dois, e o tom escuro elevado continua abaixo da base.
  if (p.footerAlpha < 0 || p.footerAlpha > MAX_FOOTER_SHADE + 1e-9) problems.push(`faixa do rodapé ${p.footerAlpha} fora do limite`);
  if (p.footerAlpha > 0 && p.text !== "#FFFFFF") problems.push("faixa do rodapé com texto escuro");
  if (p.darkRaise > 0 && p.text === "#FFFFFF") problems.push("tom escuro elevado com texto branco");
  if (p.lightDrop > 0 && p.text !== "#FFFFFF") problems.push("tom claro rebaixado com texto escuro");
  if (p.lightDrop > 0 && light.l < base.l - 0.003) problems.push(`tom claro rebaixado abaixo da base`);
  if ((p.footerAlpha > 0 || p.lightDrop > 0) && p.darkRaise > 0) problems.push("ajustes de texto branco e escuro ao mesmo tempo");
  if (p.footer !== (p.footerAlpha > 0 ? `rgba(0, 0, 0, ${p.footerAlpha})` : "transparent")) problems.push("faixa do rodapé no CSS diferente da calculada");
  if (p.darkRaise > 0 && dark.l > base.l - 0.02 + 0.003) problems.push(`tom escuro elevado ${dark.l.toFixed(3)} acima de base − 0,02`);
  const expectedChip =
    finish === "black" || base.c < NEUTRAL_MAX_CHROMA
      ? "silver"
      : base.h >= CHIP_WARM_HUES.from || base.h < CHIP_WARM_HUES.to ? "gold" : "silver";
  if (p.chip !== expectedChip) problems.push(`chip ${p.chip}, esperado ${expectedChip} (acabamento ${finish}, matiz ${base.h.toFixed(0)}°)`);

  // Contraste abaixo do exigido só é aceito quando a própria regra declarou que nenhuma das duas
  // cores de texto cabe no limite de sombra: vira aviso no relatório, não falha silenciosa.
  const large = minContrast(p, p.shadowAlpha.large, TEXT_REGIONS.large);
  const small = minContrast(p, p.shadowAlpha.small, TEXT_REGIONS.small);
  const shortfalls = [];
  if (large < LARGE_TEXT_CONTRAST) shortfalls.push(`texto grande ${large.toFixed(2)} < ${LARGE_TEXT_CONTRAST}`);
  if (small < SMALL_TEXT_CONTRAST) shortfalls.push(`texto pequeno ${small.toFixed(2)} < ${SMALL_TEXT_CONTRAST}`);
  if (shortfalls.length > 0 && !p.shortfall) problems.push(...shortfalls.map((x) => `${x} sem aviso da regra`));
  if (shortfalls.length === 0 && p.shortfall) problems.push("regra declarou falta de contraste que não existe");

  return { label, baseHex, finish, palette: p, large, small, problems, shortfalls };
}

const finishes = ["standard", "gold", "platinum"];
const cases = [];

for (const { name, hex } of institutions) for (const finish of finishes) cases.push([`instituição ${name}`, hex, finish]);

const sweep = new Set();
for (let h = 0; h < 360; h += 15)
  for (const l of [0.12, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.97])
    for (const c of [0.01, 0.03, 0.05, 0.08, 0.12, 0.16, 0.2, 0.26, 0.32]) {
      let fit = c;
      while (oklchToLinearRgb(l, fit, h).some((v) => v < 0 || v > 1) && fit > 0) fit = Math.max(0, fit - 0.002);
      sweep.add(toHexFromOklch(l, fit, h));
    }
for (const hex of ["#000000", "#FFFFFF", "#111111", "#242424", "#555555", "#808080", "#A9A9A9", "#C0C0C0", "#E5E4E2", "#F5F5F5", "#FAFAFA", "#0A0A0A", "#FFFF00", "#FF0000", "#00FF00", "#0000FF", "#00FFFF", "#FF00FF", "#FFA500", "#8B4513", "#808000"])
  sweep.add(hex);
for (const hex of sweep) for (const finish of finishes) cases.push(["varredura", hex, finish]);

// Black/Infinite: fundo grafite com o toque de cada cor de banco (e de toda a varredura) no
// reflexo e na borda, e o caso sem banco.
const blackBase = getCardGradient({ name: "Cartão Black" }).base;
cases.push(["black sem banco", blackBase, "black", null]);
for (const { name, hex } of institutions) cases.push([`black ${name}`, blackBase, "black", hex]);
for (const hex of sweep) cases.push(["black varredura", blackBase, "black", hex]);

const results = cases.map(([label, hex, finish, accent]) => check(label, hex, finish, accent));

const resolved = [
  { name: "Nubank Ultravioleta", institutionColor: "#820AD1" },
  { name: "Cartão Black", institutionColor: "#EC7000" },
  { name: "Visa Infinite" },
  { name: "Cartão Gold" },
  { name: "Cartão Platinum" },
  { name: "Cartão" },
];
for (const card of resolved) {
  const p = getCardGradient(card);
  results.push(check(`regra: ${card.name}`, p.base, p.finish, p.finish === "black" ? card.institutionColor ?? null : null));
}

// Módulo pré-calculado: mesmas chaves que as combinações atuais (BankBrandColors.cs) e cada
// entrada idêntica ao cálculo em tempo real (cardColors.js). Qualquer diferença = desatualizado.
const stale = [];
const expected = precomputedCombinations(institutions.map((i) => i.hex));
const expectedKeys = new Set(expected.map(({ base, finish, accent }) => paletteKey(base, finish, accent)));
for (const key of expectedKeys) if (!(key in PRECOMPUTED_PALETTES)) stale.push(`falta a combinação ${key}`);
for (const key of Object.keys(PRECOMPUTED_PALETTES)) if (!expectedKeys.has(key)) stale.push(`combinação que não existe mais: ${key}`);
for (const { base, finish, accent } of expected) {
  const key = paletteKey(base, finish, accent);
  if (key in PRECOMPUTED_PALETTES && JSON.stringify(PRECOMPUTED_PALETTES[key]) !== JSON.stringify(cardPalette(base, finish, accent)))
    stale.push(`entrada ${key} diferente do cálculo atual`);
}
for (const { hex } of institutions) {
  const p = getCardGradient({ name: "Cartão", institutionColor: hex });
  if (p !== PRECOMPUTED_PALETTES[paletteKey(hex, "standard", null)]) stale.push(`getCardGradient não usa o pré-cálculo para ${hex}`);
}

const failures = results.filter((r) => r.problems.length > 0);
const warnings = results.filter((r) => r.shortfalls.length > 0);
const shadowed = (key) => results.filter((r) => r.palette.shadowAlpha[key] > 0).map((r) => r.palette.shadowAlpha[key]);
const summary = (values) => (values.length ? `${values.length} (máx ${Math.max(...values).toFixed(2)}, mediana ${values.sort((a, b) => a - b)[Math.floor(values.length / 2)].toFixed(2)})` : "0");
console.log(`Cores verificadas: ${results.length} (instituições: ${institutions.length} × ${finishes.length} acabamentos; varredura: ${sweep.size} cores × ${finishes.length}; black com cor de banco: ${institutions.length + sweep.size + 1}; regras: ${resolved.length})`);
console.log(`Base idêntica à cor oficial: ${results.filter((r) => r.palette.base === r.baseHex.toUpperCase()).length}/${results.length}`);
console.log(`Neutras: ${results.filter((r) => r.palette.neutral).length} | texto claro: ${results.filter((r) => r.palette.text === "#FFFFFF").length} | texto escuro: ${results.filter((r) => r.palette.text !== "#FFFFFF").length}`);
console.log(`Contraste mínimo: texto grande ${Math.min(...results.map((r) => r.large)).toFixed(2)}:1 (exigido ${LARGE_TEXT_CONTRAST}:1) | texto pequeno ${Math.min(...results.map((r) => r.small)).toFixed(2)}:1 (exigido ${SMALL_TEXT_CONTRAST}:1)`);
console.log(`Com sombra no texto: grande ${summary(shadowed("large"))} | pequeno ${summary(shadowed("small"))}`);
console.log(`Variação mínima de luminosidade claro→escuro: ${Math.min(...results.map((r) => hexToOklch(r.palette.light).l - hexToOklch(r.palette.dark).l)).toFixed(3)} (exigido ${MIN_LIGHTNESS_RANGE})`);
console.log(`Sombra máxima do texto: ${Math.max(...results.flatMap((r) => [r.palette.shadowAlpha.large, r.palette.shadowAlpha.small]))} (limite ${MAX_TEXT_SHADOW})`);
console.log(`Ajustes automáticos: tom escuro elevado ${summary(results.filter((r) => r.palette.darkRaise > 0).map((r) => r.palette.darkRaise))} | faixa no rodapé ${summary(results.filter((r) => r.palette.footerAlpha > 0).map((r) => r.palette.footerAlpha))} | tom claro rebaixado ${summary(results.filter((r) => r.palette.lightDrop > 0).map((r) => r.palette.lightDrop))}`);
console.log(`Módulo pré-calculado: ${Object.keys(PRECOMPUTED_PALETTES).length} entradas, ${stale.length === 0 ? "idêntico ao cálculo em tempo real" : `DESATUALIZADO — rode npm run generate:card-colors`}`);
for (const s of stale.slice(0, 20)) console.log(`  ✗ ${s}`);
console.log(`Falhas: ${failures.length}`);
for (const f of failures.slice(0, 30)) console.log(`  ✗ ${f.label} ${f.baseHex} [${f.finish}]: ${f.problems.join("; ")}`);

const warnedInstitutions = warnings.filter((w) => w.label.startsWith("instituição"));
console.log(`\nSem solução dentro do limite de sombra (${MAX_TEXT_SHADOW}): ${warnings.length} casos — ${warnedInstitutions.length} de instituições, ${warnings.filter((w) => w.label === "varredura").length} da varredura, ${warnings.filter((w) => w.label.startsWith("black")).length} de Black`);
for (const w of warnedInstitutions)
  console.log(`  ! ${w.label} ${w.baseHex} [${w.finish}] texto ${w.palette.text} sombra ${w.palette.shadowAlpha.small}: ${w.shortfalls.join("; ")}`);

if (process.argv.includes("--sample")) {
  console.log("\nAmostra (acabamento padrão):");
  for (const key of ["itau", "inter", "santander", "mercado pago", "sicredi", "original"]) {
    const { name, hex } = institutions.find((i) => i.name === key);
    const r = check(name, hex, "standard");
    const p = r.palette;
    const fmt = (s) => `L${s.l.toFixed(3)} C${s.c.toFixed(3)} H${s.h.toFixed(0)}`;
    const adjustment =
      [p.darkRaise > 0 && `tom escuro +${p.darkRaise} L`, p.footerAlpha > 0 && `faixa no rodapé ${p.footerAlpha}`, p.lightDrop > 0 && `tom claro −${p.lightDrop} L`]
        .filter(Boolean)
        .join(", ") || "nenhum";
    console.log(`  ${name.padEnd(16)} claro ${p.light} | base ${p.base} | escuro ${p.dark} | texto ${p.text} | ajuste: ${adjustment} | sombra grande ${p.shadowAlpha.large} pequeno ${p.shadowAlpha.small} | contraste grande ${r.large.toFixed(2)} pequeno ${r.small.toFixed(2)}`);
    console.log(`  ${"".padEnd(16)} ${p.oklch.map(fmt).join(" | ")}`);
  }
}

process.exit(failures.length > 0 || stale.length > 0 ? 1 : warnings.length > 0 ? 2 : 0);
