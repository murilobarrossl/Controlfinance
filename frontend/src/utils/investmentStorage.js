import { getCurrentUserId } from "./authToken.js";

const LEGACY_KEY = "investmentGoals";

// Versão antiga guardava tudo numa chave só, compartilhada entre qualquer conta
// logada no mesmo navegador — descarta esse resquício uma vez.
try {
  localStorage.removeItem(LEGACY_KEY);
} catch {
  // localStorage indisponível — nada a fazer
}

function storageKey() {
  return `${LEGACY_KEY}:${getCurrentUserId() ?? "anonymous"}`;
}

export function getGoals() {
  try {
    const raw = localStorage.getItem(storageKey());
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
}

function persist(goals) {
  localStorage.setItem(storageKey(), JSON.stringify(goals));
}

export function saveGoal({ name, targetAmount, currentAmount, deadline }) {
  const goals = getGoals();
  const goal = {
    id: crypto.randomUUID(),
    name,
    targetAmount,
    currentAmount,
    deadline: deadline || null,
    createdAt: new Date().toISOString(),
  };
  persist([goal, ...goals]);
  return goal;
}

export function updateGoalProgress(id, currentAmount) {
  const goals = getGoals().map((goal) =>
    goal.id === id ? { ...goal, currentAmount } : goal
  );
  persist(goals);
}

export function deleteGoal(id) {
  persist(getGoals().filter((goal) => goal.id !== id));
}
