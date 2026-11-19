import { BRAND_ICONS } from "../components/ui/MerchantLogo/brandIcons.js";
import { EXTRA_BRAND_ICONS } from "../components/ui/MerchantLogo/extraBrandIcons.js";
import { ADDITIONAL_BRAND_ICONS } from "../components/ui/MerchantLogo/additionalBrandIcons.js";

const icons = { ...BRAND_ICONS, ...EXTRA_BRAND_ICONS, ...ADDITIONAL_BRAND_ICONS };
const exact = new Map();
const byWord = new Map();

// Os gatilhos são palavras inteiras. Quando há expressão, ela confirma o contexto
// no nome completo. A ordem desempata candidatos, com os produtos antes das marcas-mãe.
const rules = [
  ["googlegemini", "GOOGLE GOOGLEGEMINI GEMINI GEMINIADVANCED GEMINIPRO GEMINIAI", [/\bGOOGLE ?GEMINI\b/, /^GEMINI ?(ADVANCED|PRO|AI)\b/]],
  ["perplexity", "PERPLEXITY"],
  ["amazon", "AMAZON AMZN"],
  ["mercadolivre", "MERCADO MERCADOLIVRE MERCADOLIBRE", [/\bMERCADO ?LIVRE\b/, /\bMERCADO ?LIBRE\b/]],
  ["shein", "SHEIN"],
  ["magalu", "MAGAZINE MAGAZINELUIZA MAGAZ MAGALU", [/\bMAGAZINE ?LUIZA\b/, /\bMAGAZ LUIZA\b/, /\bMAGALU\b(?! ?PAY\b)/]],
  ["ubereats", "UBER UBEREATS", [/\bUBER ?EATS\b/]],
  ["uber", "UBER"],
  ["ifood", "IFOOD IFD", [/\bIFOOD\b/, /^IFD\b/]],
  ["netflix", "NETFLIX"],
  ["spotify", "SPOTIFY"],
  ["youtubemusic", "YOUTUBE YOUTUBEMUSIC", [/\bYOUTUBE ?MUSIC\b/]],
  ["youtube", "YOUTUBE YOUTUBEPREMIUM"],
  ["googleplay", "GOOGLE GOOGLEPLAY", [/\bGOOGLE ?PLAY\b/]],
  ["google", "GOOGLE"],
  ["applemusic", "APPLE APPLEMUSIC", [/\bAPPLE ?MUSIC\b/]],
  ["appletv", "APPLE APPLETV", [/\bAPPLE ?TV\b/]],
  ["icloud", "ICLOUD"],
  ["apple", "APPLE ITUNES"],
  ["hbomax", "HBO HBOMAX", [/\bHBO ?MAX\b/]],
  ["hbo", "HBO"],
  ["paramountplus", "PARAMOUNT"],
  ["airbnb", "AIRBNB"],
  ["bookingdotcom", "BOOKING BOOKINGCOM", [/\bBOOKING ?COM\b/]],
  ["shopee", "SHOPEE", [/\bSHOPEE\b(?! ?PAY\b)/]],
  ["aliexpress", "ALIEXPRESS"],
  ["mcdonalds", "MC MCDONALD MCDONALDS ARCOS", [/\bMC ?DONALDS?\b/, /\bARCOS DOURADOS\b/]],
  ["burgerking", "BURGER BURGERKING BK", [/\bBURGER ?KING\b/, /\bBK BRASIL\b/]],
  ["starbucks", "STARBUCKS"],
  ["kfc", "KFC"],
  ["carrefour", "CARREFOUR"],
  ["ikea", "IKEA"],
  ["playstation", "PLAYSTATION"],
  ["steam", "STEAM STEAMGAMES STEAMPOWERED STEAMPURCHASE", [/\bSTEAM ?(GAMES|POWERED|PURCHASE)\b/, /^STEAM\b/]],
  ["epicgames", "EPIC EPICGAMES", [/\bEPIC ?GAMES\b/]],
  ["twitch", "TWITCH"],
  ["discord", "DISCORD"],
  ["openai", "OPENAI CHATGPT"],
  ["claude", "CLAUDE CLAUDEAI", [/\bCLAUDE ?AI\b/]],
  ["anthropic", "ANTHROPIC"],
  ["canva", "CANVA"],
  ["notion", "NOTION NOTIONSO NOTIONLABS", [/\bNOTION ?(SO|LABS)\b/, /^NOTION$/]],
  ["dropbox", "DROPBOX"],
  ["duolingo", "DUOLINGO"],
  ["tinder", "TINDER"],
  ["crunchyroll", "CRUNCHYROLL"],
  ["audible", "AUDIBLE"],
  ["patreon", "PATREON"],
  ["figma", "FIGMA"],
  ["slack", "SLACK SLACKTECHNOLOGIES SLACKCOM", [/\bSLACK ?(TECHNOLOGIES|COM)\b/, /^SLACK$/]],
  ["zoom", "ZOOM ZOOMUS ZOOMVIDEO ZOOMCOM", [/\bZOOM ?(US|VIDEO|COM)\b/]],
  ["github", "GITHUB"],
  ["shell", "SHELL"],
  ["nike", "NIKE"],
  ["adidas", "ADIDAS"],
  ["vivo", "TELEFONICA VIVO", [/\bTELEFONICA BRASIL\b/, /^VIVO( (FIXO|MOVEL|FIBRA|EASY|CONTROLE|POS|INTERNET|TV))?$/]],
];

for (const [priority, [id, words, patterns]] of rules.entries()) {
  const brand = icons[id];
  const matches = patterns ? (name) => patterns.some((pattern) => pattern.test(name)) : () => true;
  const rule = { priority, brand, matches };
  for (const word of words.split(" ")) {
    if (!byWord.has(word)) byWord.set(word, []);
    byWord.get(word).push(rule);
    if (!exact.has(word) && matches(word)) exact.set(word, brand);
  }
}

function normalize(text) {
  return String(text ?? "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/['\u2019`]/g, "")
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, " ")
    .trim();
}

const cache = new Map();
const PIX = /\bPIX\b/;

export function merchantBrand(name, { isTransfer = false } = {}) {
  const key = normalize(name);
  if (!key) return null;
  if (isTransfer) return PIX.test(key) ? icons.pix : null;
  if (exact.has(key)) return exact.get(key);
  if (cache.has(key)) return cache.get(key);

  let best;
  for (const word of key.split(" ")) {
    for (const rule of byWord.get(word) ?? []) {
      if ((!best || rule.priority < best.priority) && rule.matches(key)) best = rule;
    }
  }
  const brand = best?.brand ?? (PIX.test(key) ? icons.pix : null);
  if (cache.size >= 5000) cache.delete(cache.keys().next().value);
  cache.set(key, brand);
  return brand;
}
