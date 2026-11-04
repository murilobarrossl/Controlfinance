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

// Paleta das categorias e dos gráficos, sóbria de propósito: parte das duas cores da marca
// (laranja-avermelhado do site, #ED4A31, e um verde-água um pouco mais fechado que o de receita,
// #4ECDC4, pra uma fatia de despesa não parecer receita) e completa com tons terrosos e
// acinzentados, sem rosa nem roxo vivo. Cada cor tem pelo menos 3,3:1 de contraste sobre o fundo
// dos painéis e os 8 tons ficam a uma distância perceptual mínima de 20 (CIELAB) entre si; o
// cinza de "Outros" (OTHERS_COLOR em utils/categoryColors.js) fica fora daqui de propósito.
//
// Precisa ficar igual a: PolpSyncService.FallbackPalette e às cores das categorias padrão
// (backend). Categorias já salvas com cores de paletas anteriores não precisam de migration:
// resolveCategoryColor só usa a cor do banco se ela for desta paleta e deriva uma daqui nas demais.
// Roscas usam só tons do laranja da marca (#ED4A31), em vez da paleta multicolor acima: o maior
// segmento fica com o laranja puro e os seguintes alternam entre tons mais claros e mais escuros,
// pra fatias vizinhas não se confundirem. O mais escuro para em ~3:1 de contraste sobre o fundo
// dos painéis; abaixo disso a fatia some. Passou de 6 fatias, a sequência recomeça.
export const DONUT_SHADES = [
  "#ED4A31", // laranja da marca
  "#F7A798", // tom claro
  "#C23A24", // tom escuro
  "#F4806E", // tom médio-claro (--color-accent-soft)
  "#FBD0C7", // tom muito claro
  "#B5432F", // tom escuro acinzentado
];

export const FALLBACK_COLORS = [
  "#ED4A31", // laranja-avermelhado (destaque do site)
  "#C9923E", // ocre
  "#3FA9A1", // verde-água (da cor de receita, um pouco mais fechado)
  "#B04A5E", // vinho
  "#5B86B5", // azul-aço
  "#8573A8", // ameixa
  "#8E9B5A", // oliva
  "#B98B73", // terracota
];
