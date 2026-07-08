const MAIN_BANKS = [
  "Nubank",
  "Itaú",
  "Banco do Brasil",
  "Bradesco",
  "Santander",
  "Caixa Econômica Federal",
  "Banco Inter",
  "C6 Bank",
  "XP Banking",
  "Mercado Pago"
];

function priorityIndex(name) {
  const index = MAIN_BANKS.findIndex((main) => name.startsWith(main));
  return index === -1 ? MAIN_BANKS.length : index;
}

function sortByPriority(list) {
  return [...list].sort((a, b) => {
    const diff = priorityIndex(a.name) - priorityIndex(b.name);
    return diff !== 0 ? diff : a.name.localeCompare(b.name, "pt-BR");
  });
}

export function groupConnectorsByType(connectors) {
  const personal = [];
  const business = [];

  for (const connector of connectors) {
    const isBusiness =
      connector.type === "BUSINESS_BANK" || /empresas$/i.test(connector.name);
    (isBusiness ? business : personal).push(connector);
  }

  return {
    personal: sortByPriority(personal),
    business: sortByPriority(business),
  };
}
