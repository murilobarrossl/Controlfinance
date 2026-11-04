// Nomes exatos da lista da Polp (v2): prefixo puxava "Santander Corretora" e "Bradesco
// Financiamentos" pra frente da conta principal desses bancos.
const MAIN_BANKS = [
  "Nubank",
  "Itaú",
  "Itaú Empresas",
  "Banco do Brasil",
  "Bradesco Pessoa Física",
  "Bradesco Pessoa Jurídica",
  "Banco Santander Pessoa Física",
  "Banco Santander Pessoa Jurídica",
  "CAIXA",
  "Banco Inter PF",
  "Banco Inter PJ",
  "C6 Bank",
  "BTG Banking",
  "BTG Empresas",
  "Banco XP S.A.",
  "Mercado Pago",
  "PicPay",
  "PagBank",
  "Sicoob",
  "Sicredi",
];

function sortByPriority(list) {
  return [...list].sort((a, b) => {
    const diff = priorityIndex(a.name) - priorityIndex(b.name);
    return diff !== 0 ? diff : a.name.localeCompare(b.name, "pt-BR");
  });
}

// A Polp marca algumas instituições só de empresas como BOTH (Safra Empresas, Itaú BBA, Shopee
// Vendedor): o nome desempata antes do tipo.
const BUSINESS_NAME = /\b(empresas?|emps|pj|juridica|corporate|negocios|vendedor|bba)\b/;
const PERSONAL_NAME = /\b(pf|pessoa fisica)\b/;

function normalizeText(value) {
  return (value ?? "").normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
}

function priorityIndex(name) {
  const index = MAIN_BANKS.indexOf(name);
  return index === -1 ? MAIN_BANKS.length : index;
}

function audienceOf(connector) {
  const name = normalizeText(connector.name);
  if (BUSINESS_NAME.test(name)) return "BUSINESS";
  if (PERSONAL_NAME.test(name)) return "PERSONAL";
  return connector.type ?? "PERSONAL";
}

export function filterConnectorsByName(connectors, query) {
  const terms = normalizeText(query).split(/\s+/).filter(Boolean);
  if (terms.length === 0) return connectors;
  return connectors.filter((connector) => {
    const name = normalizeText(connector.name);
    return terms.every((term) => name.includes(term));
  });
}

export function groupConnectorsByType(connectors) {
  const personal = [];
  const business = [];

  for (const connector of connectors) {
    const audience = audienceOf(connector);
    if (audience !== "BUSINESS") personal.push(connector);
    if (audience !== "PERSONAL") business.push(connector);
  }

  return {
    personal: sortByPriority(personal),
    business: sortByPriority(business),
  };
}
