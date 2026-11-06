export function formatDueDay(date) {
  const [, month, day] = String(date).slice(0, 10).split("-");
  return `Venceu em ${day}/${month}`;
}

export function formatDataAge(syncedAt) {
  if (!syncedAt || Number.isNaN(new Date(syncedAt).getTime())) return null;
  const synced = new Date(syncedAt).toLocaleString("pt-BR", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
  return `dado de ${synced}`;
}

export function formatPastDue(date, syncedAt) {
  return [formatDueDay(date), formatDataAge(syncedAt)].filter(Boolean).join(" · ");
}
