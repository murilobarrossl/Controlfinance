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

// Modalidades de dinheiro guardado. A ordem importa: caixinha/porquinho/Tesouro Selic vêm pelo nome
// (o banco costuma mandar o mesmo tipo genérico pra elas), poupança e investimento pelo tipo da Polp.
export const INVESTMENT_MODALITIES = {
  caixinha: { key: "caixinha", label: "Caixinha", groupLabel: "Caixinhas", color: "var(--color-neutral)" },
  porquinho: {
    key: "porquinho",
    label: "Porquinho / Cofrinho",
    groupLabel: "Porquinhos e cofrinhos",
    color: "var(--color-neutral)",
    textColor: "var(--color-neutral)",
  },
  poupanca: { key: "poupanca", label: "Poupança", groupLabel: "Poupança", color: "var(--color-neutral)" },
  tesouroSelic: {
    key: "tesouroSelic",
    label: "Tesouro Selic",
    groupLabel: "Tesouro Selic",
    color: "var(--color-neutral)",
    textColor: "var(--color-neutral)",
  },
  investimento: {
    key: "investimento",
    label: "Investimento",
    groupLabel: "Outros investimentos",
    color: "var(--color-neutral)",
    textColor: "var(--color-neutral)",
  },
};

export const INVESTMENT_MODALITY_ORDER = ["caixinha", "porquinho", "poupanca", "tesouroSelic", "investimento"];

export function getInvestmentModality(account) {
  if (isCreditCardAccount(account)) return null;

  const name = account.name ?? "";
  const type = accountText(account);

  if (matches(name, /caixinha/i)) return INVESTMENT_MODALITIES.caixinha;
  if (matches(name, /porquinho|cofrinho|cofre/i)) return INVESTMENT_MODALITIES.porquinho;
  if (matches(name, /tesouro\s+selic|\blft\b/i)) return INVESTMENT_MODALITIES.tesouroSelic;
  if (matches(type, /saving|poupan/i) || matches(name, /poupan/i)) return INVESTMENT_MODALITIES.poupanca;
  if (matches(type, /invest/i) || matches(name, /\b(cdb|lci|lca|tesouro|fundo|invest)/i)) {
    return INVESTMENT_MODALITIES.investimento;
  }
  return null;
}

export function getAccountKindLabel(account) {
  if (isCreditCardAccount(account)) return "Cartão de crédito";
  if (matches(account.accountType, /^CONTA_PAGAMENTO/)) return "Conta de pagamento";
  return getInvestmentModality(account)?.label ?? "Conta corrente";
}
