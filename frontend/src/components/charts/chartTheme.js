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

export const FALLBACK_COLORS = [
  "#4988D4",
  "#DB5824",
  "#30A67D",
  "#BF8A22",
  "#CD517E",
  "#328532",
  "#7569D3",
  "#D45454",
];
