// Ordem de decisão: produto reconhecível pelo nome → categoria (product_type da Polp, ou palavra
// no nome do cartão manual) → cor da instituição → cor neutra.
//
// O degradê sai da cor base em OKLCH, nunca misturando com preto ou branco: misturar escurecia o
// vermelho até marrom e o amarelo até oliva. O tom escuro gira o matiz para o vizinho mais
// profundo da faixa e não desce abaixo de um piso de luminosidade por faixa, pelo mesmo motivo.
// A base é a cor oficial exata; só os tons claro e escuro são derivados.

import { PRECOMPUTED_PALETTES } from "./cardColors.generated.js";

const PRODUCT_RULES = [{ match: /\bultravioleta\b/, base: "#8A05BE" }];

const CATEGORY_KEYWORDS = [
  { category: "BLACK", match: /\b(black|infinite)\b/ },
  { category: "PLATINUM", match: /\bplatinum\b/ },
  { category: "GOLD", match: /\bgold\b/ },
];

const BLACK_BASE = "#2B2D31";
const CATEGORY_DEFAULTS = { PLATINUM: "#9AA3AD", GOLD: "#C9A44A" };
const NEUTRAL_BASE = "#3A3F47";

export const FINISHES = {
  standard: { gloss: [255, 255, 255, 0.1], edge: "rgba(255, 255, 255, 0.12)" },
  gold: { gloss: [255, 224, 160, 0.12], edge: "rgba(232, 196, 112, 0.38)" },
  platinum: { gloss: [226, 234, 246, 0.12], edge: "rgba(214, 222, 234, 0.38)" },
  black: { gloss: [205, 218, 238, 0.08], edge: "rgba(255, 255, 255, 0.12)" },
};

export const SHEEN_ALPHA = 0.06;
export const SHADE_ALPHA = 0.16;
export const MUTED_ALPHA = 0.85;
export const LARGE_TEXT_CONTRAST = 3;
export const SMALL_TEXT_CONTRAST = 4.5;
export const NEUTRAL_MAX_CHROMA = 0.035;
export const DARK_MIN_CHROMA = 0.05;
export const MIN_LIGHTNESS_RANGE = 0.1;
// Acima disso a sombra do texto passa a ser percebida como contorno (halo) e atrapalha a leitura.
export const MAX_TEXT_SHADOW = 0.15;

// Black/Infinite: fundo grafite, só reflexo e borda com um toque da cor do banco.
export const BLACK_ACCENT = { minLightness: 0.75, maxChroma: 0.1, glossAlpha: 0.1, edgeAlpha: 0.28 };

// Onde cada texto fica no cartão, em frações da largura (x) e da altura (y), com folga. Segue o
// layout de CreditCardVisual: bandeira no canto superior direito, número e nome no meio à
// esquerda, titular e vencimento no rodapé. O contraste é medido só onde o texto aparece.
export const TEXT_REGIONS = {
  large: [
    { x: [0.55, 0.95], y: [0.08, 0.22] },
    { x: [0.05, 0.75], y: [0.35, 0.5] },
    { x: [0.05, 0.8], y: [0.5, 0.68] },
  ],
  small: [
    { x: [0.05, 0.5], y: [0.8, 0.93] },
    { x: [0.5, 0.95], y: [0.8, 0.93] },
  ],
};

// Geometria das camadas do CSS (CreditCardVisual.css): degradê a 135°, reflexo radial de 120% x
// 110% no canto superior esquerdo sumindo em 60%, faixa de brilho a 115° entre 32% e 62% com pico
// em 46%, e a sombra interna `inset 0 -16px 22px -18px`: o "furo" da sombra passa 2px da borda de
// baixo (18 de spread − 16 de deslocamento) e o desfoque é gaussiano com desvio de 11px. A altura
// usada é a do menor cartão (celular), onde a sombra ocupa a maior fração.
export const CARD_ASPECT = 1.586;
export const GLOSS_GEOMETRY = { rx: 1.2, ry: 1.1, end: 0.6 };
export const SHEEN_GEOMETRY = { angle: 115, from: 0.32, peak: 0.46, to: 0.62 };
export const SHADE_GEOMETRY = { gapPx: 2, sigmaPx: 11, minCardHeightPx: 180 };
// Faixa escura do rodapé (só para texto branco que não cabe no limite de sombra): opacidade cheia
// de y = 0,82 para baixo, sumindo linearmente até y = 0,62. No CSS: linear-gradient(to top, cor 0%,
// cor 18%, transparent 38%).
export const FOOTER_GEOMETRY = { solidFromY: 0.82, fadeFromY: 0.62 };
export const MAX_FOOTER_SHADE = 0.5;

const LIGHT_TEXT = "#FFFFFF";
const DARK_TEXT = "#141414";

const cache = new Map();

export function getCardGradient({ name, productType, institutionColor }) {
  const key = `${name ?? ""}|${productType ?? ""}|${institutionColor ?? ""}`;
  let result = cache.get(key);
  if (!result) {
    const { base, finish, accent } = resolve(normalize(name), normalizeCategory(productType), toHex(institutionColor));
    result = PRECOMPUTED_PALETTES[paletteKey(base, finish, accent)] ?? cardPalette(base, finish, accent);
    cache.set(key, result);
  }
  return result;
}

export function paletteKey(base, finish, accent) {
  return `${base}|${finish}|${accent ?? ""}`;
}

// Tudo o que resolve() pode devolver para as cores de banco conhecidas, mais as regras que não
// dependem de banco. Usado por scripts/generate-card-colors.mjs para montar cardColors.generated.js
// (o cálculo completo leva ~18 ms por cartão) e pelo verificador para conferir que ele está em dia.
export function precomputedCombinations(institutionColors) {
  const combinations = new Map();
  const add = (base, finish, accent = null) => combinations.set(paletteKey(base, finish, accent), { base, finish, accent });
  for (const color of institutionColors) {
    const hex = toHex(color);
    add(hex, "standard");
    add(hex, "gold");
    add(hex, "platinum");
    add(BLACK_BASE, "black", hex);
  }
  for (const rule of PRODUCT_RULES) add(rule.base, "standard");
  add(BLACK_BASE, "black");
  add(CATEGORY_DEFAULTS.GOLD, "gold");
  add(CATEGORY_DEFAULTS.PLATINUM, "platinum");
  add(NEUTRAL_BASE, "standard");
  return [...combinations.values()];
}

function resolve(name, productType, institutionColor) {
  const product = PRODUCT_RULES.find((rule) => rule.match.test(name));
  if (product) return { base: product.base, finish: "standard" };

  const category = productType ?? CATEGORY_KEYWORDS.find((rule) => rule.match.test(name))?.category;
  if (category === "BLACK" || category === "INFINITE") return { base: BLACK_BASE, finish: "black", accent: institutionColor };

  const finish = category === "GOLD" ? "gold" : category === "PLATINUM" ? "platinum" : "standard";
  return { base: institutionColor ?? CATEGORY_DEFAULTS[category] ?? NEUTRAL_BASE, finish };
}

// A base é sempre a cor oficial, intocada: o contraste nunca altera o cartão. Texto grande
// (nome, número, bandeira) pede 3:1 e texto pequeno (titular, vencimento) 4,5:1, como na WCAG;
// o que faltar é compensado por uma sombra no próprio texto, até MAX_TEXT_SHADOW.
export function cardPalette(baseHex, finish = "standard", accentHex = null) {
  const base = hexToOklch(baseHex);
  const neutral = base.c < NEUTRAL_MAX_CHROMA;
  const stops = neutral ? neutralStops(base) : chromaticStops(base);
  const { gloss, edge } = finishLayers(finish, accentHex);

  // Cada cor de texto tenta primeiro sem ajuste; se não couber no limite de sombra, o texto escuro
  // sobe o tom escuro do degradê e o texto branco ganha a faixa escura do rodapé (e, se o texto
  // grande não couber, desce o tom claro), cada um só o necessário. A base nunca muda. Entre as opções que cabem vence a que menos altera a
  // luminosidade do cartão; sem ajuste nas duas, a de menos sombra; no empate, branco.
  const options = [LIGHT_TEXT, DARK_TEXT]
    .map((text) => fitText(text, stops, base, baseHex, gloss))
    .filter(Boolean);

  if (options.length > 0) {
    const best = options.reduce((a, b) => {
      if (Math.abs(a.cost - b.cost) > 1e-9) return b.cost < a.cost ? b : a;
      if (a.cost === 0) return b.shadow.large + b.shadow.small < a.shadow.large + a.shadow.small - 1e-9 ? b : a;
      return a;
    });
    return build(best, finish, neutral, { gloss, edge });
  }

  // Nenhuma opção cabe nem com os ajustes: não força sombra maior. Usa o texto que chega mais perto
  // do exigido e registra o contraste que faltou (o verificador relata).
  const hexes = [oklchToHex(stops[0]), baseHex, oklchToHex(stops[2])];
  const capped = [LIGHT_TEXT, DARK_TEXT].map((text) => {
    const large = worstContrast(hexes, text, gloss, 0, MAX_TEXT_SHADOW, TEXT_REGIONS.large);
    const small = worstContrast(hexes, text, gloss, 0, MAX_TEXT_SHADOW, TEXT_REGIONS.small);
    return { text, large, small, score: Math.min(large / LARGE_TEXT_CONTRAST, small / SMALL_TEXT_CONTRAST) };
  });
  const worst = capped.reduce((a, b) => (b.score > a.score + 1e-9 ? b : a));
  return build(
    { text: worst.text, stops, hexes, footer: 0, cost: 0, darkRaise: 0, lightDrop: 0, shadow: { large: MAX_TEXT_SHADOW, small: MAX_TEXT_SHADOW } },
    finish,
    neutral,
    { gloss, edge },
    { large: worst.large, small: worst.small },
  );
}

function fitText(text, stops, base, baseHex, gloss) {
  const attempt = (candidate, footer) => {
    const hexes = [oklchToHex(candidate[0]), baseHex, oklchToHex(candidate[2])];
    const large = textShadowAlpha(hexes, text, gloss, footer, LARGE_TEXT_CONTRAST, TEXT_REGIONS.large);
    const small = textShadowAlpha(hexes, text, gloss, footer, SMALL_TEXT_CONTRAST, TEXT_REGIONS.small);
    return large !== null && small !== null ? { text, stops: candidate, hexes, footer, shadow: { large, small } } : null;
  };

  const plain = attempt(stops, 0);
  if (plain) return { ...plain, cost: 0, darkRaise: 0, lightDrop: 0 };

  if (text === DARK_TEXT) {
    // Tom escuro mais claro, até 0,02 abaixo da base; o claro acompanha só se for preciso para
    // manter a variação mínima.
    // Tom escuro mais claro nunca piora o texto escuro: bisseção em passos de 0,01.
    const [light, , dark] = stops;
    const withRaise = (step) => {
      const raised = fitToGamut({ ...dark, l: dark.l + step / 100 });
      return attempt([fitToGamut({ ...light, l: lightLFor(light.l, raised.l) }), stops[1], raised], 0);
    };
    const maxStep = Math.floor((base.l - 0.02 - dark.l) * 100 + 1e-9);
    if (maxStep < 1 || !withRaise(maxStep)) return null;
    let [lo, hi] = [0, maxStep];
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (withRaise(mid)) hi = mid;
      else lo = mid;
    }
    return { ...withRaise(hi), cost: hi / 100, darkRaise: hi / 100, lightDrop: 0 };
  }

  // Texto branco: a faixa do rodapé cuida do titular e do vencimento; se o que não cabe é o texto
  // grande (nome, número, bandeira, que ficam do lado claro do degradê), o tom claro desce até no
  // máximo a base, mantendo a variação mínima. Fica o menor rebaixamento que resolve, com a menor
  // faixa para ele.
  const [light, , dark] = stops;
  const lowest = Math.max(base.l, dark.l + MIN_LIGHTNESS_RANGE + 0.01);
  for (let drop = 0; light.l - drop >= lowest - 1e-9; drop += 0.01) {
    const candidate = drop === 0 ? stops : [fitToGamut({ ...light, l: light.l - drop }), stops[1], dark];
    const footer = minimalFooter((alpha) => attempt(candidate, alpha));
    if (footer !== null) {
      return { ...attempt(candidate, footer), cost: drop + lightnessDrop(base, footer), darkRaise: 0, lightDrop: Math.round(drop * 100) / 100 };
    }
  }
  return null;
}

// Mais escuro nunca piora o texto branco: bisseção na opacidade da faixa, em passos de 0,01.
function minimalFooter(fits) {
  if (fits(0)) return 0;
  const maxStep = Math.round(MAX_FOOTER_SHADE * 100);
  if (!fits(MAX_FOOTER_SHADE)) return null;
  let [lo, hi] = [0, maxStep];
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (fits(mid / 100)) hi = mid;
    else lo = mid;
  }
  return hi / 100;
}

// Quanto a faixa do rodapé escurece a cor oficial, em luminosidade OKLab: mesma escala do ajuste
// do tom escuro, para comparar as duas opções.
function lightnessDrop(base, alpha) {
  const shaded = over(channels(oklchToHex(base)), [0, 0, 0], alpha).map((v) => Math.round(v));
  return base.l - hexToOklch(`#${shaded.map((v) => v.toString(16).padStart(2, "0")).join("")}`).l;
}

function finishLayers(finish, accentHex) {
  const { gloss, edge } = FINISHES[finish];
  if (finish !== "black" || !accentHex) return { gloss, edge };
  const accent = hexToOklch(accentHex);
  const tint = channels(
    oklchToHex(fitToGamut({ l: Math.max(accent.l, BLACK_ACCENT.minLightness), c: Math.min(accent.c, BLACK_ACCENT.maxChroma), h: accent.h })),
  );
  return { gloss: [...tint, BLACK_ACCENT.glossAlpha], edge: `rgba(${tint.join(", ")}, ${BLACK_ACCENT.edgeAlpha})` };
}

// Menor opacidade (em passos de 0,05) que alcança o contraste, ou null se passar do limite.
// Mais sombra nunca piora o contraste, então dá para buscar por bisseção.
function textShadowAlpha(hexes, text, gloss, footer, required, regions) {
  const passes = (step) => worstContrast(hexes, text, gloss, footer, step / 20, regions, required) >= required;
  const maxStep = Math.round(MAX_TEXT_SHADOW * 20);
  if (passes(0)) return 0;
  if (!passes(maxStep)) return null;
  let [lo, hi] = [0, maxStep];
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (passes(mid)) hi = mid;
    else lo = mid;
  }
  return Math.round(hi * 5) / 100;
}

function shadowColorFor(text) {
  return text === LIGHT_TEXT ? [0, 0, 0] : [255, 255, 255];
}

function textShadowCss(color, alpha) {
  if (alpha === 0) return "none";
  const rgba = `rgba(${color.join(", ")}, ${alpha})`;
  return `0 0 1px ${rgba}, 0 0 3px ${rgba}`;
}

// Chip prateado no Black/Infinite e em toda cor neutra (grafite ou prata); nos demais segue a
// temperatura da cor base, pelo matiz: dourado nas quentes (vermelho, laranja, amarelo, rosa e,
// por ser laranja escuro, marrom), prateado nas frias (verde, ciano, azul, roxo).
export const CHIP_WARM_HUES = { from: 335, to: 115 };

function chipFor({ c, h }, finish) {
  if (finish === "black" || c < NEUTRAL_MAX_CHROMA) return "silver";
  return h >= CHIP_WARM_HUES.from || h < CHIP_WARM_HUES.to ? "gold" : "silver";
}

function build({ text, stops, hexes: [light, base, dark], footer, darkRaise, lightDrop, shadow }, finish, neutral, layers, shortfall = null) {
  const [r, g, b] = channels(text);
  const [gr, gg, gb, ga] = layers.gloss;
  const shadowColor = shadowColorFor(text);
  return {
    light,
    base,
    dark,
    text,
    textMuted: `rgba(${r}, ${g}, ${b}, ${MUTED_ALPHA})`,
    textShadowLarge: textShadowCss(shadowColor, shadow.large),
    textShadowSmall: textShadowCss(shadowColor, shadow.small),
    shadowColor,
    shadowAlpha: shadow,
    gloss: `rgba(${gr}, ${gg}, ${gb}, ${ga})`,
    glossLayer: layers.gloss,
    edge: layers.edge,
    chip: chipFor(hexToOklch(base), finish),
    // O ruído só empurra o fundo para longe da cor do texto (escurece atrás de texto branco,
    // clareia atrás de texto escuro), então nunca reduz o contraste.
    noise: text === LIGHT_TEXT ? "darken" : "lighten",
    shortfall,
    // Ajustes que a regra precisou aplicar para o texto caber no limite de sombra.
    footerAlpha: footer,
    footer: footer > 0 ? `rgba(0, 0, 0, ${footer})` : "transparent",
    darkRaise,
    lightDrop,
    sheen: `rgba(255, 255, 255, ${SHEEN_ALPHA})`,
    shade: `rgba(0, 0, 0, ${SHADE_ALPHA})`,
    finish,
    neutral,
    oklch: stops,
  };
}

// ── Derivação dos tons ──────────────────────────────────────────────────────

// Grafite para as escuras, prata para as claras. Sem girar matiz: o croma é baixo demais para o
// matiz significar alguma coisa.
function neutralStops({ l, c, h }) {
  const silver = l >= 0.6;
  const dark = fitToGamut({ l: Math.max(l - (silver ? 0.14 : 0.09), 0), c, h });
  return [fitToGamut({ l: lightLFor(l + (silver ? 0.07 : 0.1), dark.l), c, h }), { l, c, h }, dark];
}

// Variação ampla garantida pelo lado claro quando o escuro não tem para onde descer (cor quase
// preta, ou tom escuro que precisou subir para manter o croma).
function lightLFor(lightL, darkL) {
  return Math.min(1, Math.max(lightL, darkL + MIN_LIGHTNESS_RANGE + 0.01));
}

function chromaticStops({ l, c, h }) {
  const band = hueBand(h);

  const darkL = Math.max(l - (0.1 + 0.08 * l), Math.min(band.floor, l - 0.05), 0.12);
  const dark = darkStop({ l: darkL, c: c * 1.06, h: wrap(h + band.turn(h)) }, { l, c, h }, Math.min(DARK_MIN_CHROMA, c));

  const lightL = lightLFor(Math.max(l, Math.min(0.97, l + 0.06 + 0.04 * (1 - l))), dark.l);
  const lightC = c * (1 - 0.18 * ramp((l - 0.72) / 0.2) - 0.15 * ramp((c - 0.17) / 0.1));

  return [fitToGamut({ l: lightL, c: lightC, h }), { l, c, h }, dark];
}

// Matiz em graus OKLCH. floor: luminosidade mínima do tom escuro, abaixo da qual a faixa vira
// marrom/oliva/cinza sujo. turn: giro do matiz no tom escuro, em direção ao vizinho mais profundo.
const BANDS = [
  { from: 345, to: 405, floor: 0.38, turn: () => -12 },                         // vermelhos e rosas → carmim/magenta
  { from: 45, to: 80, floor: 0.56, turn: () => -12 },                           // laranjas → laranja-avermelhado
  { from: 80, to: 115, floor: 0.64, turn: () => -15 },                          // amarelos → âmbar
  { from: 115, to: 180, floor: 0.34, turn: () => 12 },                          // verdes → verde-azulado
  { from: 180, to: 275, floor: 0.18, turn: () => 12 },                          // azuis e cianos → índigo
  { from: 275, to: 345, floor: 0.22, turn: (h) => clamp(295 - h, -12, 12) },    // roxos e violetas → violeta profundo
];

function hueBand(h) {
  const hue = wrap(h);
  return BANDS.find((band) => (hue >= band.from && hue < band.to) || (hue + 360 >= band.from && hue + 360 < band.to));
}

// Garante o croma mínimo (nunca acima do croma da própria base). Se naquele matiz e luminosidade
// o sRGB não comporta o mínimo, sobe a luminosidade até no máximo a da base; se ainda não couber
// (cor quase preta, já na borda do gamut), desfaz o giro de matiz aos poucos. Sem giro, na
// luminosidade da base, a própria base cabe: sempre existe solução.
function darkStop(target, base, minChroma) {
  let l = clamp(target.l, 0, base.l);
  while (maxChroma(l, target.h) < minChroma && l + 0.01 <= base.l) l += 0.01;
  if (maxChroma(l, target.h) >= minChroma) return fitToGamut({ l, c: Math.max(target.c, minChroma), h: target.h });

  const turn = ((target.h - base.h + 540) % 360) - 180;
  for (let fraction = 0.9; fraction > 0; fraction -= 0.1) {
    const h = wrap(base.h + turn * fraction);
    if (maxChroma(base.l, h) >= minChroma) return fitToGamut({ l: base.l, c: Math.max(target.c, minChroma), h });
  }
  return { l: base.l, c: base.c, h: base.h };
}

function fitToGamut({ l, c, h }) {
  return { l, c: Math.min(c, maxChroma(l, h)), h };
}

function maxChroma(l, h) {
  let lo = 0;
  let hi = 0.4;
  for (let i = 0; i < 24; i++) {
    const mid = (lo + hi) / 2;
    if (inGamut(oklchToLinear({ l, c: mid, h }))) lo = mid;
    else hi = mid;
  }
  return lo;
}

// ── Contraste ───────────────────────────────────────────────────────────────

// Pior caso nas regiões onde o texto aparece. Ordem de pintura do CSS: degradê, reflexo, faixa do
// rodapé, sombra interna, sombra do texto, texto e, por cima de tudo (::after), a faixa de brilho. O texto
// secundário (com transparência) também precisa passar.
function worstContrast(hexes, text, gloss, footer, shadowAlpha, regions, stopBelow = 0) {
  const textRgb = channels(text);
  const shadow = shadowColorFor(text);
  let worst = Infinity;
  for (const { x: [x0, x1], y: [y0, y1] } of regions) {
    for (let i = 0; i <= 16; i++) {
      for (let j = 0; j <= 12; j++) {
        const x = x0 + ((x1 - x0) * i) / 16;
        const y = y0 + ((y1 - y0) * j) / 12;
        const behind = over(backgroundAt(hexes, gloss, footer, x, y), shadow, shadowAlpha);
        const sheen = sheenAt(x, y);
        const bg = over(behind, [255, 255, 255], sheen);
        for (const glyph of [textRgb, over(behind, textRgb, MUTED_ALPHA)])
          worst = Math.min(worst, contrast(over(glyph, [255, 255, 255], sheen), bg));
        if (worst < stopBelow) return worst;
      }
    }
  }
  return worst;
}

function backgroundAt([light, base, dark], [gr, gg, gb, ga], footer, x, y) {
  const t = (x * CARD_ASPECT + y) / (CARD_ASPECT + 1);
  let bg = t <= 0.5 ? lerp(light, base, t * 2) : lerp(base, dark, (t - 0.5) * 2);
  const r = Math.hypot(x / GLOSS_GEOMETRY.rx, y / GLOSS_GEOMETRY.ry);
  if (r < GLOSS_GEOMETRY.end) bg = over(bg, [gr, gg, gb], ga * (1 - r / GLOSS_GEOMETRY.end));
  if (footer > 0) bg = over(bg, [0, 0, 0], footer * ramp((y - FOOTER_GEOMETRY.fadeFromY) / (FOOTER_GEOMETRY.solidFromY - FOOTER_GEOMETRY.fadeFromY)));
  return over(bg, [0, 0, 0], shadeAt(y));
}

function shadeAt(y) {
  const { gapPx, sigmaPx, minCardHeightPx } = SHADE_GEOMETRY;
  const distance = (1 - y) * minCardHeightPx;
  return SHADE_ALPHA * normalCdf(-(distance + gapPx) / sigmaPx);
}

// Abramowitz e Stegun 7.1.26 (erro < 1,5e-7).
function normalCdf(z) {
  const x = Math.abs(z) / Math.SQRT2;
  const t = 1 / (1 + 0.3275911 * x);
  const erf = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.exp(-x * x);
  return z >= 0 ? (1 + erf) / 2 : (1 - erf) / 2;
}

function sheenAt(x, y) {
  const a = (SHEEN_GEOMETRY.angle * Math.PI) / 180;
  const [dx, dy] = [Math.sin(a), -Math.cos(a)];
  const t = ((x - 0.5) * CARD_ASPECT * dx + (y - 0.5) * dy) / (CARD_ASPECT * Math.abs(dx) + Math.abs(dy)) + 0.5;
  const { from, peak, to } = SHEEN_GEOMETRY;
  if (t <= from || t >= to) return 0;
  return SHEEN_ALPHA * (t <= peak ? (t - from) / (peak - from) : (to - t) / (to - peak));
}

function lerp(a, b, t) {
  const ca = channels(a);
  const cb = channels(b);
  return ca.map((v, i) => v + (cb[i] - v) * t);
}

function over(bg, color, alpha) {
  return bg.map((v, i) => v * (1 - alpha) + color[i] * alpha);
}

function contrast(a, b) {
  const [hi, lo] = [relativeLuminance(a), relativeLuminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

function relativeLuminance(rgb) {
  const [r, g, b] = rgb.map((v) => toLinear(v / 255));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

// ── OKLCH ───────────────────────────────────────────────────────────────────

export function hexToOklch(hex) {
  const [r, g, b] = channels(hex).map((v) => toLinear(v / 255));
  const l_ = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m_ = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s_ = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  const L = 0.2104542553 * l_ + 0.793617785 * m_ - 0.0040720468 * s_;
  const a = 1.9779984951 * l_ - 2.428592205 * m_ + 0.4505937099 * s_;
  const bb = 0.0259040371 * l_ + 0.7827717662 * m_ - 0.808675766 * s_;
  return { l: L, c: Math.hypot(a, bb), h: wrap((Math.atan2(bb, a) * 180) / Math.PI) };
}

function oklchToLinear({ l, c, h }) {
  const a = c * Math.cos((h * Math.PI) / 180);
  const b = c * Math.sin((h * Math.PI) / 180);
  const l_ = (l + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m_ = (l - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s_ = (l - 0.0894841775 * a - 1.291485548 * b) ** 3;
  return [
    4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_,
    -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_,
    -0.0041960863 * l_ - 0.7034186147 * m_ + 1.707614701 * s_,
  ];
}

function inGamut(rgb) {
  return rgb.every((v) => v >= -1e-7 && v <= 1 + 1e-7);
}

function oklchToHex(stop) {
  return `#${oklchToLinear(stop)
    .map((v) => Math.round(clamp(fromLinear(clamp(v, 0, 1)), 0, 1) * 255).toString(16).padStart(2, "0"))
    .join("")
    .toUpperCase()}`;
}

function toLinear(c) {
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function fromLinear(c) {
  return c <= 0.0031308 ? c * 12.92 : 1.055 * c ** (1 / 2.4) - 0.055;
}

// ── Utilitários ─────────────────────────────────────────────────────────────

function clamp(v, min, max) {
  return Math.min(max, Math.max(min, v));
}

function ramp(v) {
  return clamp(v, 0, 1);
}

function wrap(h) {
  return ((h % 360) + 360) % 360;
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
