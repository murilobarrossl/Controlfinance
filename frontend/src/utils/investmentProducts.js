// Referência de quanto cada banco paga nas caixinhas/cofrinhos/porquinhos. NÃO vem de nenhuma API:
// foi levantada em comparativos públicos e muda com frequência (e com o perfil do cliente), então
// a tela mostra a data abaixo e manda confirmar no app do banco. Pra atualizar, é só editar aqui.

export const REFERENCE_DATE_LABEL = "outubro de 2026";

// Selic meta em vigor na data da referência e o CDI que costuma andar ~0,10 ponto abaixo dela.
// O usuário pode trocar o CDI na tela; estes são só os valores iniciais.
export const REFERENCE_SELIC_PCT = 14;
export const REFERENCE_CDI_PCT = 13.9;

// pctCdi: percentual do CDI no caso base (sem condições). pctCdiMax: teto anunciado, que costuma
// exigir plano pago, movimentação mínima ou limite de valor.
export const BANK_PRODUCTS = [
  {
    key: "nubank",
    bank: "Nubank",
    color: "#8A05BE",
    match: /nubank|nu pagamentos/i,
    products: [
      { id: "nubank-caixinha", name: "Caixinha", pctCdi: 100, liquidity: "Imediata", note: "Rende 100% do CDI sem exigências." },
      { id: "nubank-turbo", name: "Caixinha Turbo", pctCdi: 100, pctCdiMax: 120, liquidity: "Imediata", note: "Chega a 115%–120% do CDI conforme plano e movimentação, com limite de valor." },
    ],
  },
  {
    key: "mercadopago",
    bank: "Mercado Pago",
    color: "#009EE3",
    match: /mercado ?pago/i,
    products: [
      { id: "mp-cofrinho", name: "Cofrinho", pctCdi: 100, liquidity: "Imediata", note: "100% do CDI na versão comum." },
      { id: "mp-turbo", name: "Cofrinho turbinado", pctCdi: 100, pctCdiMax: 120, liquidity: "Imediata", note: "Até 120% do CDI nos níveis com condições." },
    ],
  },
  {
    key: "picpay",
    bank: "PicPay",
    color: "#21C25E",
    match: /picpay/i,
    products: [
      { id: "picpay-cofrinho", name: "Cofrinho", pctCdi: 102, liquidity: "Imediata", note: "102% do CDI sem condições." },
      { id: "picpay-turbo", name: "Cofrinho turbinado", pctCdi: 102, pctCdiMax: 121, liquidity: "Imediata", note: "Até 121% do CDI pra quem tem PicPay+ ou movimenta a conta." },
    ],
  },
  {
    key: "inter",
    bank: "Inter",
    color: "#FF7A00",
    match: /\binter\b|banco inter/i,
    products: [
      { id: "inter-objetivo", name: "Porquinho por objetivo", pctCdi: 100, liquidity: "No vencimento do objetivo", note: "Até 100% do CDI." },
      { id: "inter-dia", name: "Porquinho dia a dia", pctCdi: 80, liquidity: "Imediata", note: "80% do CDI, com resgate livre." },
    ],
  },
  {
    key: "c6",
    bank: "C6 Bank",
    color: "#9B9B9B",
    match: /c6/i,
    products: [
      { id: "c6-cdb", name: "CDB liquidez diária", pctCdi: 102, liquidity: "Imediata", note: "Não há caixinha automática; o CDB de liquidez diária paga cerca de 102% do CDI." },
    ],
  },
  {
    key: "pagbank",
    bank: "PagBank",
    color: "#41B883",
    match: /pagbank|pagseguro/i,
    products: [
      { id: "pagbank-cdb", name: "CDB (novos clientes)", pctCdi: 100, pctCdiMax: 130, liquidity: "Conforme o CDB", note: "Anuncia até 130% do CDI pra novos clientes, com prazos e condições. O Cofrinho comum rende bem menos em resgates antes de 1 ano." },
    ],
  },
];

// Descobre qual banco de referência corresponde ao nome de uma instituição conectada.
export function findBankByName(name) {
  if (!name) return null;
  return BANK_PRODUCTS.find((bank) => bank.match.test(name)) ?? null;
}

// Todos os produtos numa lista plana (com o banco junto), pra montar comparativos e o seletor
// do simulador.
export function flattenProducts() {
  return BANK_PRODUCTS.flatMap((bank) =>
    bank.products.map((product) => ({ ...product, bank: bank.bank, bankKey: bank.key, color: bank.color })),
  );
}
