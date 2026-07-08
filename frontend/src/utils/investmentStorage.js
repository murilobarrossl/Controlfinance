const STORAGE_KEY = "investmentGoals";

export function getGoals() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
}

function persist(goals) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(goals));
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
