import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

export const BANK_BRAND_COLORS_PATH = fileURLToPath(new URL("../../../backend/Services/BankBrandColors.cs", import.meta.url));

// Cores de instituição na ordem da tabela de BankBrandColors.cs (Word("nome|alias"), "#RRGGBB").
export function readInstitutions() {
  return [...readFileSync(BANK_BRAND_COLORS_PATH, "utf8").matchAll(/Word\("([^"]+)"\), "(#[0-9A-Fa-f]{6})"/g)].map(([, name, hex]) => ({
    name: name.split("|")[0],
    hex: hex.toUpperCase(),
  }));
}
