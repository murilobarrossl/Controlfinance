import { BRAND_ICONS } from "../../../components/ui/MerchantLogo/brandIcons.js";
import { RECURRING_BRAND_ICONS } from "./recurringBrandIcons.js";

const fromShared = (key, title) => {
  const { color, path, fillRule } = BRAND_ICONS[key];
  return { title: title ?? BRAND_ICONS[key].title, color, paths: [{ d: path, fillRule }] };
};

const BRANDS = {
  netflix: fromShared("netflix"),
  spotify: fromShared("spotify"),
  youtube: fromShared("youtube"),
  chatgpt: fromShared("openai", "ChatGPT"),
  claude: fromShared("claude"),
  steam: fromShared("steam"),
  shopee: fromShared("shopee"),
  aliexpress: { ...fromShared("aliexpress"), viewBox: "0 9.096 24.003 5.808", wide: true },
  ...RECURRING_BRAND_ICONS,
};

// Catálogo curto de propósito. Bancos e carteiras (Mercado Pago, ShopeePay...) não ganham marca.
const BRAND_RULES = [
  ["netflix", [/\bNETFLIX\b/]],
  ["spotify", [/\bSPOTIFY\b/]],
  ["youtube", [/\bYOUTUBE\b/, /\bYOUTUBE ?PREMIUM\b/]],
  ["chatgpt", [/\bCHATGPT\b/, /\bOPENAI\b/]],
  ["claude", [/\bCLAUDE ?AI\b/, /\bANTHROPIC\b/]],
  ["googlegemini", [/\bGOOGLE ?GEMINI\b/, /^GEMINI ?(ADVANCED|PRO|AI)\b/]],
  ["perplexity", [/\bPERPLEXITY\b/]],
  ["amazon", [/\bAMAZON\b/, /\bAMZN\b/]],
  ["mercadolivre", [/\bMERCADO ?LIVRE\b/, /\bMERCADO ?LIBRE\b/]],
  ["steam", [/\bSTEAM ?(GAMES|POWERED|PURCHASE)\b/, /^STEAM\b/]],
  ["shopee", [/\bSHOPEE\b(?! ?PAY\b)/]],
  ["shein", [/\bSHEIN\b/]],
  ["aliexpress", [/\bALIEXPRESS\b/]],
];

// Só nomes inequívocos; na dúvida fica o ícone neutro. A ordem importa (UBER EATS é comida).
const CATEGORY_RULES = [
  ["food", [/\bIFOOD\b/, /^IFD\b/, /\b99 ?FOOD\b/, /\bUBER ?EATS\b/, /\bRAPPI\b/, /\bRESTAURANTE\b/, /\bLANCHONETE\b/,
    /\bPIZZARIA\b/, /\bHAMBURGUERIA\b/, /\bPADARIA\b/, /\bMC ?DONALDS?\b/, /\bBURGER ?KING\b/, /\bOUTBACK\b/, /\bSTARBUCKS\b/,
    /\bSUBWAY\b/, /\bPIZZA ?HUT\b/, /\bDOMINOS\b/, /\bHABIBS\b/, /\bBOBS\b/, /\bKFC\b/]],
  ["transport", [/\bUBER\b/, /\b99 ?(APP|POP|TAXIS?|TECNOLOGIA)\b/, /\bCABIFY\b/, /\bESTACIONAMENTO\b/, /\bESTAPAR\b/,
    /\bSEM ?PARAR\b/, /\bVELOE\b/, /\bCONECTCAR\b/, /\bPEDAGIO\b/]],
  ["fitness", [/\bACADEMIA\b/, /\bFITNESS\b/, /\bSMART ?FIT\b/, /\bBLUEFIT\b/, /\bBODYTECH\b/, /\bSELFIT\b/, /\bGYMPASS\b/,
    /\bWELLHUB\b/, /\bTOTALPASS\b/, /\bCROSSFIT\b/, /\bPILATES\b/]],
  ["fuel", [/\bAUTO ?POSTO\b/, /^POSTO\b/, /\bCOMBUSTIVE(L|IS)\b/, /\bSHELL\b/, /\bIPIRANGA\b/]],
  ["media", [/\bNETFLIX\b/, /\bSPOTIFY\b/, /\bDEEZER\b/, /\bDISNEY ?PLUS\b/, /\bPRIME ?VIDEO\b/, /\bHBO ?MAX\b/,
    /\bGLOBOPLAY\b/, /\bPARAMOUNT\b/, /\bCRUNCHYROLL\b/, /\bAPPLE ?(TV|MUSIC)\b/, /\bTWITCH\b/]],
  ["shopping", [/\bMAGAZINE ?LUIZA\b/, /\bMAGALU\b(?! ?PAY\b)/, /\bAMERICANAS\b/, /\bCASAS ?BAHIA\b/, /\bRENNER\b/,
    /\bRIACHUELO\b/, /\bNETSHOES\b/, /\bKABUM\b/, /\bTEMU\b/, /\bMARKETPLACE\b/]],
  ["digital", [/\bMICROSOFT\b/, /\bOFFICE ?365\b/, /\bADOBE\b/, /\bGOOGLE ?(ONE|WORKSPACE|STORAGE|CLOUD)\b/, /\bICLOUD\b/,
    /\bDROPBOX\b/, /\bNOTION ?(SO|LABS)\b/, /\bCANVA\b/, /\bGITHUB\b/, /\bFIGMA\b/, /\bMIDJOURNEY\b/, /\bCOPILOT\b/]],
];

function normalize(text) {
  return String(text ?? "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/['\u2019`]/g, "")
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, " ")
    .trim();
}

const find = (rules, key) => rules.find(([, patterns]) => patterns.some((p) => p.test(key)))?.[0] ?? null;

export function recurringLogo(name) {
  const key = normalize(name);
  if (!key) return null;
  const brand = find(BRAND_RULES, key);
  if (brand) return { brand: BRANDS[brand] };
  const category = find(CATEGORY_RULES, key);
  return category ? { category } : null;
}
