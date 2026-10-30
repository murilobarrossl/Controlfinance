import { FALLBACK_COLORS } from "../components/charts/chartTheme.js";

export const OTHERS_COLOR = "#808080";

// Cor estável por nome de categoria: usa a cor cadastrada na categoria quando existe (a mesma do
// gráfico de Categorias) e, sem ela, deriva uma cor da paleta a partir do nome. Antes a cor
// faltante era escolhida pela posição na lista, então a mesma categoria mudava de cor entre o
// Dashboard, Categorias e Relatórios conforme a ordenação de cada tela.
export function resolveCategoryColor(categoryName, categories = []) {
  if (!categoryName) return OTHERS_COLOR;

  const registered = categories.find((c) => c.name === categoryName)?.color;
  if (registered) return registered.startsWith("#") ? registered : `#${registered}`;

  let hash = 0;
  for (const char of categoryName) hash = (hash * 31 + char.codePointAt(0)) >>> 0;
  return FALLBACK_COLORS[hash % FALLBACK_COLORS.length];
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
