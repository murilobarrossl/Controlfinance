import { getCurrentUserId } from "./authToken.js";

// Leitura das metas que ficaram no navegador antes de irem para o servidor (api/planning.js importa
// uma vez e só apaga daqui as que o servidor confirmou como guardadas).
const LEGACY_KEY = "investmentGoals";
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

// Versão ainda mais antiga guardava tudo numa chave só, compartilhada entre qualquer conta
// logada no mesmo navegador: descarta esse resquício uma vez.
try {
  localStorage.removeItem(LEGACY_KEY);
} catch {
  // localStorage indisponível, nada a fazer
}

function storageKey() {
  return `${LEGACY_KEY}:${getCurrentUserId() ?? "anonymous"}`;
}

function readRaw() {
  try {
    const raw = localStorage.getItem(storageKey());
    const parsed = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function write(goals) {
  try {
    if (goals.length === 0) localStorage.removeItem(storageKey());
    else localStorage.setItem(storageKey(), JSON.stringify(goals));
  } catch {
    // localStorage indisponível: as metas continuam onde estavam
  }
}

export function readLegacyGoals() {
  const raw = readRaw();
  let changed = false;
  const goals = raw.map((goal) => {
    if (goal && typeof goal.id === "string" && UUID.test(goal.id)) return goal;
    changed = true;
    return { ...goal, id: crypto.randomUUID() };
  });
  // Um id estável é o que deixa a importação ser repetida sem duplicar meta.
  if (changed) write(goals);

  return goals.map((goal) => ({
    id: goal.id,
    name: typeof goal.name === "string" ? goal.name : "",
    targetAmount: Number(goal.targetAmount) || 0,
    currentAmount: Number(goal.currentAmount) || 0,
    deadline: typeof goal.deadline === "string" && ISO_DATE.test(goal.deadline) ? goal.deadline : null,
    createdAt: typeof goal.createdAt === "string" && !Number.isNaN(Date.parse(goal.createdAt)) ? goal.createdAt : null,
  }));
}

export function removeLegacyGoals(ids) {
  if (ids.length === 0) return;
  const stored = new Set(ids);
  write(readRaw().filter((goal) => !stored.has(goal.id)));
}
