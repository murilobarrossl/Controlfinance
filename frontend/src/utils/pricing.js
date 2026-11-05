// Fonte única da precificação exibida no site (Planos e FAQ). Antes cada valor era digitado à
// mão em mais de um lugar e eles já tinham divergido entre si (o anual mostrava R$49,90/mês mas
// cobrava R$478,80/ano, que dá R$39,90/mês). Tudo aqui deriva do preço base do plano mensal e do
// desconto de cada plano, então mudar um número atualiza todos os textos de uma vez.
const BASE_MONTHLY_PRICE = 49.9;
export const FREE_TRIAL_DAYS = 14;

// Preço "terminado em ,90": arredonda o mensal com desconto pra baixo até o inteiro e soma 0,90
// (49,90 × 0,90 = 44,91 → 44,90; 49,90 × 0,80 = 39,92 → 39,90).
function charmPrice(value) {
  return Math.floor(value) + 0.9;
}

function roundCents(value) {
  return Math.round(value * 100) / 100;
}

function buildPlan({ id, name, months, discount }) {
  const monthlyPrice = discount > 0 ? charmPrice(BASE_MONTHLY_PRICE * (1 - discount)) : BASE_MONTHLY_PRICE;
  const totalPrice = roundCents(monthlyPrice * months);
  const fullPrice = roundCents(BASE_MONTHLY_PRICE * months);

  return {
    id,
    name,
    months,
    monthlyPrice,
    totalPrice,
    // Economia efetiva (sobre o preço final), não o desconto nominal, pra o percentual exibido
    // nunca prometer mais do que a conta realmente entrega.
    discountPercent: Math.round((1 - monthlyPrice / BASE_MONTHLY_PRICE) * 100),
    savingsPerPeriod: roundCents(fullPrice - totalPrice),
  };
}

export const PLANS = {
  monthly: buildPlan({ id: "monthly", name: "Mensal", months: 1, discount: 0 }),
  quarterly: buildPlan({ id: "quarterly", name: "Trimestral", months: 3, discount: 0.1 }),
  annual: buildPlan({ id: "annual", name: "Anual", months: 12, discount: 0.2 }),
};

export function formatPrice(value) {
  return value.toLocaleString("pt-BR", { style: "currency", currency: "BRL" }).replace(/\s/g, "\u00a0");
}

// Divide o preço em "R$ 49" e ",90" pro destaque visual dos cards de plano.
export function splitPrice(value) {
  const [integer, cents] = formatPrice(value).split(",");
  return { integer, cents: `,${cents}` };
}
