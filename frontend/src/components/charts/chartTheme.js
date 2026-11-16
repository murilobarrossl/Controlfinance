// SVG e tooltips resolvem as mesmas variáveis CSS da interface, sem duplicar hex.
export const CHART_COLORS = {
  income: "var(--color-income)",
  expense: "var(--color-expense)",
  neutral: "var(--color-neutral)",
  axis: "var(--color-text-tertiary)",
  grid: "var(--color-border)",
  cursor: "var(--color-border-strong)",
  label: "var(--color-text-secondary)",
};

export const TOOLTIP_STYLE = {
  backgroundColor: "var(--color-surface)",
  border: "1px solid var(--color-border)",
  borderRadius: 8,
  color: "var(--color-text-primary)",
};

// O nome e o valor identificam a série; texto neutro mantém o contraste do tooltip.
export const TOOLTIP_ITEM_STYLE = { color: "var(--color-text-primary)" };

export const DONUT_SHADES = Array.from({ length: 6 }, (_, i) => `var(--color-chart-expense-${i + 1})`);

// Quatro categorias principais do Dashboard; "Outros" mantém --color-neutral.
export const NEUTRAL_DONUT_SHADES = Array.from({ length: 4 }, (_, i) => `var(--color-chart-category-neutral-${i + 1})`);

// Identidade visual de categorias, sem atribuir resultado financeiro ao cadastro.
// Apenas apresentação: não exige alterar cores salvas nem a integração bancária.
export const FALLBACK_COLORS = Array.from({ length: 8 }, (_, i) => `var(--color-category-${i + 1})`);
