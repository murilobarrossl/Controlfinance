import { apiFetch } from "./client.js";

export const getRecurrences = () => apiFetch("/recurrences");

// status/assumedFrequency/reminderRequested são independentes: omitir um deles não mexe no valor
// já gravado (update parcial). Ver RecurrencesController no backend.
export const setRecurrenceDecision = (payload) =>
  apiFetch("/recurrences/decision", {
    method: "POST",
    body: JSON.stringify(payload),
  });
