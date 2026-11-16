// Apenas textos de erro para apresentação. Não modifica respostas, dados ou identificadores.
export function publicIntegrationMessage(message) {
  if (typeof message !== "string") return "";

  return message
    .replace(
      /A Polp não aceitou esses dados para esse banco\./gi,
      "Não foi possível validar os dados para a conexão com esta instituição financeira.",
    )
    .replace(/\bPolp(?: Tecnologia(?: LTDA\.?)?)?\b/gi, "integração bancária");
}
