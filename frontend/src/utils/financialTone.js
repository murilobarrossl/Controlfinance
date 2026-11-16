import { roundMoney } from "./financeMath.js";

// Somente apresentação: acompanha o sinal do valor exibido, sem modificar o dado.
export function financialTone(value) {
  if (typeof value !== "number" || !Number.isFinite(value)) return "default";
  const displayed = roundMoney(value);
  return displayed > 0 ? "income" : displayed < 0 ? "expense" : "default";
}

// O chamador fornece a classificação existente; transferências ficam neutras.
export function transactionTone(type, amount, isTransfer = false) {
  if (isTransfer || !["Income", "Expense"].includes(type) || financialTone(amount) === "default") return "default";
  return type === "Income" ? "income" : "expense";
}
