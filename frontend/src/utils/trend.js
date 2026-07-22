// direction segue o sinal literal da variação (pra a seta do badge bater com o número);
// tone é quem decide a cor, e pra despesas essa leitura é invertida (crescer é ruim).
export function computeTrend(current, previous, { invertTone = false, label = "vs mês anterior" } = {}) {
  if (!previous) return null;
  const change = ((current - previous) / previous) * 100;
  const direction = change >= 0 ? "up" : "down";
  const isGood = invertTone ? change < 0 : change >= 0;
  return {
    direction,
    tone: isGood ? "positive" : "negative",
    text: `${change >= 0 ? "+" : ""}${change.toFixed(1)}% ${label}`,
  };
}
