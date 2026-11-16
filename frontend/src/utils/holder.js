// Um login pertence a um único titular: o CPF ou o CNPJ do cadastro (o backend guarda só o hash e o
// tipo, devolvido em /auth/me como documentType). Toda diferença de tela ou de fluxo entre pessoa
// física e empresa deve partir daqui. Nulo quando o tipo não é conhecido: aí nada é restringido,
// o backend continua sendo quem confere.
export const HOLDER_CPF = "CPF";
export const HOLDER_CNPJ = "CNPJ";

export function getHolderType(user) {
  const type = user?.documentType;
  return type === HOLDER_CPF || type === HOLDER_CNPJ ? type : null;
}

export const HOLDER_DOCUMENT_LENGTH = { [HOLDER_CPF]: 11, [HOLDER_CNPJ]: 14 };

// Só um CNPJ confirmado abre o ambiente empresarial; tipo ausente cai na experiência de pessoa física.
export function isBusinessHolder(user) {
  return getHolderType(user) === HOLDER_CNPJ;
}
