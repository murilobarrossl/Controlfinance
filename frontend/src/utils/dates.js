// Datas digitadas no padrão brasileiro (dd/mm/aaaa) e guardadas como "AAAA-MM-DD", o formato que o
// backend recebe (DateOnly). Converte pela string, sem new Date(): meia-noite UTC vira o dia
// anterior no fuso do Brasil.

export function isoToBr(iso) {
  if (!iso || !/^\d{4}-\d{2}-\d{2}/.test(iso)) return "";
  const [year, month, day] = iso.slice(0, 10).split("-");
  return `${day}/${month}/${year}`;
}

// "AAAA-MM-DD" de uma data completa e que existe no calendário (31/02 não passa), ou null.
export function brToIso(text) {
  const match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(text ?? "");
  if (!match) return null;
  const [, day, month, year] = match.map(Number);
  if (year < 1900 || month < 1 || month > 12 || day < 1) return null;
  const daysInMonth = new Date(Date.UTC(year, month, 0)).getUTCDate();
  if (day > daysInMonth) return null;
  return `${match[3]}-${match[2]}-${match[1]}`;
}

export function todayIso() {
  const now = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}
