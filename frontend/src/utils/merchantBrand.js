import { BRAND_ICONS } from "../components/ui/MerchantLogo/brandIcons.js";
import { EXTRA_BRAND_ICONS } from "../components/ui/MerchantLogo/extraBrandIcons.js";

const ICONS = { ...BRAND_ICONS, ...EXTRA_BRAND_ICONS };

// Bancos, carteiras e subadquirentes (PicPay, Nubank, Mercado Pago, PayPal, PagSeguro, MagaluPay,
// Luizacred...) ficam de fora: num PIX ou numa compra intermediada eles aparecem como instituição de
// quem recebe, não como o estabelecimento. Nomes que também são nomes de pessoa (Claude, Zara)
// exigem complemento.
const RULES = [
  ["magalu", [/\bMAGAZINE ?LUIZA\b/, /\bMAGAZ LUIZA\b/, /\bMAGALU\b(?! ?PAY\b)/]],
  ["ubereats", [/\bUBER ?EATS\b/]],
  ["uber", [/\bUBER\b/]],
  ["ifood", [/\bIFOOD\b/, /^IFD\b/]],
  ["netflix", [/\bNETFLIX\b/]],
  ["spotify", [/\bSPOTIFY\b/]],
  ["youtubemusic", [/\bYOUTUBE ?MUSIC\b/]],
  ["youtube", [/\bYOUTUBE\b/, /\bYOUTUBEPREMIUM\b/]],
  ["googleplay", [/\bGOOGLE ?PLAY\b/]],
  ["google", [/\bGOOGLE\b/]],
  ["applemusic", [/\bAPPLE ?MUSIC\b/]],
  ["appletv", [/\bAPPLE ?TV\b/]],
  ["icloud", [/\bICLOUD\b/]],
  ["apple", [/\bAPPLE\b/, /\bITUNES\b/]],
  ["hbomax", [/\bHBO ?MAX\b/]],
  ["hbo", [/\bHBO\b/]],
  ["paramountplus", [/\bPARAMOUNT\b/]],
  ["airbnb", [/\bAIRBNB\b/]],
  ["bookingdotcom", [/\bBOOKING ?COM\b/]],
  ["shopee", [/\bSHOPEE\b/]],
  ["aliexpress", [/\bALIEXPRESS\b/]],
  ["mcdonalds", [/\bMC ?DONALDS?\b/, /\bARCOS DOURADOS\b/]],
  ["burgerking", [/\bBURGER ?KING\b/, /\bBK BRASIL\b/]],
  ["starbucks", [/\bSTARBUCKS\b/]],
  ["kfc", [/\bKFC\b/]],
  ["carrefour", [/\bCARREFOUR\b/]],
  ["ikea", [/\bIKEA\b/]],
  ["playstation", [/\bPLAYSTATION\b/]],
  ["steam", [/\bSTEAM ?(GAMES|POWERED|PURCHASE)\b/, /^STEAM\b/]],
  ["epicgames", [/\bEPIC ?GAMES\b/]],
  ["twitch", [/\bTWITCH\b/]],
  ["discord", [/\bDISCORD\b/]],
  ["openai", [/\bOPENAI\b/, /\bCHATGPT\b/]],
  ["claude", [/\bCLAUDE ?AI\b/]],
  ["anthropic", [/\bANTHROPIC\b/]],
  ["canva", [/\bCANVA\b/]],
  ["notion", [/\bNOTION ?(SO|LABS)\b/, /^NOTION$/]],
  ["dropbox", [/\bDROPBOX\b/]],
  ["duolingo", [/\bDUOLINGO\b/]],
  ["tinder", [/\bTINDER\b/]],
  ["crunchyroll", [/\bCRUNCHYROLL\b/]],
  ["audible", [/\bAUDIBLE\b/]],
  ["patreon", [/\bPATREON\b/]],
  ["figma", [/\bFIGMA\b/]],
  ["slack", [/\bSLACK ?(TECHNOLOGIES|COM)\b/, /^SLACK$/]],
  ["zoom", [/\bZOOM ?(US|VIDEO|COM)\b/]],
  ["github", [/\bGITHUB\b/]],
  ["shell", [/\bSHELL\b/]],
  ["nike", [/\bNIKE\b/]],
  ["adidas", [/\bADIDAS\b/]],
  ["vivo", [/\bTELEFONICA BRASIL\b/, /^VIVO( (FIXO|MOVEL|FIBRA|EASY|CONTROLE|POS|INTERNET|TV))?$/]],
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

// Estabelecimento identificado vence o Pix: "PIX ENVIADO NETFLIX" mostra a Netflix; "PIX RECEBIDO
// CARLOS RODRIGUES" mostra o Pix. Transferência entre contas próprias nunca ganha marca de empresa.
const PIX = /\bPIX\b/;

const cache = new Map();

export function merchantBrand(name, { isTransfer = false } = {}) {
  const key = normalize(name);
  if (!key) return null;
  const cacheKey = `${isTransfer ? 1 : 0}${key}`;
  if (cache.has(cacheKey)) return cache.get(cacheKey);
  const rule = isTransfer ? null : RULES.find(([, patterns]) => patterns.some((p) => p.test(key)));
  const brand = rule ? ICONS[rule[0]] : PIX.test(key) ? ICONS.pix : null;
  if (cache.size > 5000) cache.clear();
  cache.set(cacheKey, brand);
  return brand;
}
