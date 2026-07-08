// Bancos cujo logo já vem com fundo transparente/adequado ao tema escuro,
// dispensando o fundo branco padrão aplicado aos ícones.
const TRANSPARENT_LOGOS = [
  "Nubank",
];

// Bancos cujo logo fica pequeno demais no tamanho padrão (28px).
const CUSTOM_SIZES = {
  "Banco Inter": 36,
};

export function getLogoStyle(name) {
  const transparent = TRANSPARENT_LOGOS.some((bank) => name.startsWith(bank));
  const size = Object.entries(CUSTOM_SIZES).find(([bank]) => name.startsWith(bank))?.[1];

  return { transparent, size };
}
