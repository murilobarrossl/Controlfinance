// Classificação de cada conta sincronizada a partir do "type"/"subtype" crus que a Polp já manda
// (e do nome, que é o que diferencia caixinha/porquinho dentro do mesmo banco). Nada aqui inventa
// conta ou saldo: só decide como apresentar uma conta que já existe. "isCreditCard" vem pronto do
// backend (CreditCardAccountDetector), pra cartão nunca ser tratado como conta bancária aqui e
// como cartão em Cartões.

function matches(value, pattern) {
  return typeof value === "string" && pattern.test(value);
}

function accountText(account) {
  return [account.accountType, account.accountSubtype].filter(Boolean).join(" ");
}

export function isCreditCardAccount(account) {
  return Boolean(account?.isCreditCard);
}

// Modalidades de dinheiro guardado. A ordem importa: caixinha/porquinho vêm pelo nome (o banco
// costuma mandar o mesmo tipo genérico pra elas), poupança e investimento pelo tipo da Polp.
export const INVESTMENT_MODALITIES = {
  caixinha: {
    key: "caixinha",
    label: "Caixinha",
    color: "#4ECDC4",
    description:
      "Um espaço separado dentro da sua conta pra guardar dinheiro com um objetivo. Rendimento, prazo de resgate e regras variam de banco para banco: confira no app do seu banco.",
  },
  porquinho: {
    key: "porquinho",
    label: "Porquinho / Cofrinho",
    color: "#B04A5E",
    description:
      "Recurso de bancos digitais pra separar uma parte do saldo. Funciona como uma caixinha, com outro nome; as regras são definidas por cada banco.",
  },
  poupanca: {
    key: "poupanca",
    label: "Poupança",
    color: "#C9923E",
    description:
      "Conta de reserva tradicional, com rendimento definido por regra legal. Conta com a garantia do FGC dentro dos limites e regras do próprio FGC (consulte fgc.org.br).",
  },
  investimento: {
    key: "investimento",
    label: "Investimento",
    color: "#8573A8",
    description:
      "Aplicações como CDB, LCI/LCA, Tesouro Direto ou fundos. Prazo de resgate, rendimento, riscos e garantias dependem de cada produto.",
  },
};

export function getInvestmentModality(account) {
  if (isCreditCardAccount(account)) return null;

  const name = account.name ?? "";
  const type = accountText(account);

  if (matches(name, /caixinha/i)) return INVESTMENT_MODALITIES.caixinha;
  if (matches(name, /porquinho|cofrinho|cofre/i)) return INVESTMENT_MODALITIES.porquinho;
  if (matches(type, /saving|poupan/i) || matches(name, /poupan/i)) return INVESTMENT_MODALITIES.poupanca;
  if (matches(type, /invest/i) || matches(name, /\b(cdb|lci|lca|tesouro|fundo|invest)/i)) {
    return INVESTMENT_MODALITIES.investimento;
  }
  return null;
}

export function getAccountKindLabel(account) {
  if (isCreditCardAccount(account)) return "Cartão de crédito";
  return getInvestmentModality(account)?.label ?? "Conta corrente";
}
