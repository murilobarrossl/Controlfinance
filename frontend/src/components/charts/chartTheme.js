export const TOOLTIP_STYLE = {
  backgroundColor: "#1a1a1a",
  border: "1px solid #2a2a2a",
  borderRadius: 8,
  color: "#ffffff",
};

// Recharts colora cada linha do tooltip com a cor da série/célula por padrão, e cai pra preto
// quando não consegue resolver isso (como no nosso caso, que usa <Cell> com cores por item) -
// força branco pra sempre ficar legível no fundo escuro.
export const TOOLTIP_ITEM_STYLE = {
  color: "#ffffff",
};

// Paleta das categorias e dos gráficos. Parte das duas cores da marca (laranja-avermelhado do
// site, #ED4A31, e um verde-água um pouco mais fechado que o de receita, #4ECDC4, pra uma fatia
// de despesa não parecer receita) e completa com tons quentes e frios de mesma saturação e luz.
// Os 8 matizes seguem bem separados entre si (distinguíveis também por daltonismo); o cinza de
// "Outros" (OTHERS_COLOR em utils/categoryColors.js) fica fora daqui de propósito.
//
// Precisa ficar igual a: PolpSyncService.FallbackPalette e às cores das categorias padrão (backend),
// e à migration RetuneCategoryPaletteToBrand, que converteu as categorias já salvas.
export const FALLBACK_COLORS = [
  "#ED4A31", // laranja-avermelhado (destaque do site)
  "#F2A33A", // âmbar
  "#3FB8AF", // verde-água
  "#D9628A", // rosa
  "#6C93C9", // azul
  "#9A86D9", // violeta
  "#A6BE5C", // verde-oliva
  "#B07A5B", // argila
];
