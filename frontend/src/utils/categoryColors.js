import { DONUT_SHADES, FALLBACK_COLORS } from "../components/charts/chartTheme.js";

// Cinza neutro, fora da paleta de matizes: "Outros" é o que sobra, não uma categoria a mais.
export const OTHERS_COLOR = "#8a8f98";

// Cor de cada fatia da rosca, por posição (os dados já chegam do maior para o menor). Fica aqui, e
// não só no componente, pra legenda, o resumo de Categorias e a rosca usarem a mesma cor. "Outros"
// mantém o cinza e não consome um tom do laranja.
export function donutSliceColors(data) {
  let shade = 0;
  return data.map((entry) =>
    entry.color === OTHERS_COLOR ? OTHERS_COLOR : DONUT_SHADES[shade++ % DONUT_SHADES.length]
  );
}

const PALETTE = new Set([...FALLBACK_COLORS, OTHERS_COLOR].map((c) => c.toLowerCase()));

function nameHash(categoryName) {
  let hash = 0;
  for (const char of categoryName) hash = (hash * 31 + char.codePointAt(0)) >>> 0;
  return hash;
}

const assignmentCache = new WeakMap();

// Uma cor da paleta por categoria, sem repetir enquanto houver cor livre (com 8 cores, até 8
// categorias ficam todas distintas). Só pelo hash do nome, duas categorias caíam na mesma cor e
// ficavam indistinguíveis no mesmo gráfico (ex.: Compras e Saúde, ambas rosa). A ordem de
// atribuição é alfabética, então o resultado é o mesmo no Dashboard, em Categorias e em
// Relatórios, qualquer que seja a ordenação de cada tela.
function assignColors(categories) {
  const cached = assignmentCache.get(categories);
  if (cached) return cached;

  const sorted = categories.filter((c) => c?.name).sort((a, b) => a.name.localeCompare(b.name));
  const assigned = new Map();
  const used = new Set();

  // Primeiro quem já tem cor da paleta cadastrada (a primeira de cada cor, em ordem alfabética).
  // Cores fora da paleta existem só em categorias antigas: a sincronização da Polp v1 gravava a
  // cor que a Polp mandava, e nenhuma tela deixa o usuário escolher cor, então ignorá-las não
  // descarta escolha de ninguém.
  for (const c of sorted) {
    const registered = c.color && (c.color.startsWith("#") ? c.color : `#${c.color}`);
    const key = registered?.toLowerCase();
    if (key && PALETTE.has(key) && !used.has(key)) {
      assigned.set(c.name, registered);
      used.add(key);
    }
  }

  for (const c of sorted) {
    if (assigned.has(c.name)) continue;

    const start = nameHash(c.name) % FALLBACK_COLORS.length;
    let color = FALLBACK_COLORS[start];
    for (let i = 0; i < FALLBACK_COLORS.length; i++) {
      const candidate = FALLBACK_COLORS[(start + i) % FALLBACK_COLORS.length];
      if (!used.has(candidate.toLowerCase())) {
        color = candidate;
        break;
      }
    }
    assigned.set(c.name, color);
    used.add(color.toLowerCase());
  }

  assignmentCache.set(categories, assigned);
  return assigned;
}

export function resolveCategoryColor(categoryName, categories = []) {
  if (!categoryName) return OTHERS_COLOR;

  const assigned = assignColors(categories).get(categoryName);
  if (assigned) return assigned;

  // Nome que não está na lista de categorias (ex.: agrupamento de relatório): só o hash.
  return FALLBACK_COLORS[nameHash(categoryName) % FALLBACK_COLORS.length];
}

const CONNECTOR_WORDS = new Set(["de", "da", "do", "das", "dos", "e"]);

// "Alimentação e bebidas" → "AB", "Lazer" → "L".
export function categoryInitials(categoryName) {
  if (!categoryName?.trim()) return "?";
  const words = categoryName.trim().split(/\s+/);
  const meaningful = words.filter((w) => !CONNECTOR_WORDS.has(w.toLowerCase()));
  return (meaningful.length > 0 ? meaningful : words)
    .slice(0, 2)
    .map((w) => w[0])
    .join("")
    .toUpperCase();
}
