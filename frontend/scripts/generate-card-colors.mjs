// Gera src/utils/cardColors.generated.js: npm run generate:card-colors
// Pré-calcula, com a própria cardPalette de cardColors.js, cada combinação de cor de banco
// (BankBrandColors.cs) com os acabamentos, e as regras que não dependem de banco.
import { existsSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { readInstitutions } from "./lib/bank-brand-colors.mjs";

const target = fileURLToPath(new URL("../src/utils/cardColors.generated.js", import.meta.url));

// cardColors.js importa o módulo gerado: na primeira geração ele ainda não existe.
if (!existsSync(target)) writeFileSync(target, "export const PRECOMPUTED_PALETTES = {};\n");

const { cardPalette, precomputedCombinations, paletteKey } = await import("../src/utils/cardColors.js");

const institutions = readInstitutions();
const entries = precomputedCombinations(institutions.map((i) => i.hex)).map(({ base, finish, accent }) => [
  paletteKey(base, finish, accent),
  cardPalette(base, finish, accent),
]);

const body = entries.map(([key, palette]) => `  ${JSON.stringify(key)}: ${JSON.stringify(palette)},`).join("\n");
writeFileSync(
  target,
  `// ARQUIVO GERADO por \`npm run generate:card-colors\` (scripts/generate-card-colors.mjs). NÃO EDITE À MÃO.
// Fonte: backend/Services/BankBrandColors.cs + cardPalette() de src/utils/cardColors.js. Ao mudar qualquer
// um dos dois, gere de novo; \`npm run verify:card-colors\` falha se este arquivo estiver desatualizado.
// Chave: "base|acabamento|cor do banco no Black". ${entries.length} combinações de ${institutions.length} instituições.
export const PRECOMPUTED_PALETTES = {
${body}
};
`,
);
console.log(`${entries.length} combinações gravadas em src/utils/cardColors.generated.js`);
