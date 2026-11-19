import { isoToBr, todayIso } from "./dates.js";

// Rótulos do cadastro de funcionários. Os valores são os nomes dos enums do backend.
export const EMPLOYMENT_TYPES = [
  { value: "Clt", label: "CLT" },
  { value: "Temporario", label: "Temporário" },
  { value: "Estagio", label: "Estágio" },
  { value: "Aprendiz", label: "Aprendiz" },
  { value: "Autonomo", label: "Autônomo" },
  { value: "PrestadorPj", label: "Prestador PJ" },
  { value: "Outro", label: "Outro" },
];

export const PIX_KEY_TYPES = [
  { value: "Cpf", label: "CPF" },
  { value: "Cnpj", label: "CNPJ" },
  { value: "Email", label: "E-mail" },
  { value: "Phone", label: "Celular" },
  { value: "Random", label: "Chave aleatória" },
];

export const PAYEE_ACCOUNT_TYPES = [
  { value: "Corrente", label: "Conta corrente" },
  { value: "Poupanca", label: "Poupança" },
  { value: "Pagamento", label: "Conta de pagamento" },
  { value: "Salario", label: "Conta salário" },
];

const labelOf = (list) => (value) => list.find((o) => o.value === value)?.label ?? value ?? "—";

export const employmentTypeLabel = labelOf(EMPLOYMENT_TYPES);
export const pixKeyTypeLabel = labelOf(PIX_KEY_TYPES);
export const payeeAccountTypeLabel = labelOf(PAYEE_ACCOUNT_TYPES);

export const EMPLOYEE_STATUS = {
  Active: { label: "Ativo", pill: "Neutral" },
  Terminated: { label: "Desligado", pill: "Neutral" },
};

// "AAAA-MM-DD" do backend (DateOnly) em dd/mm/aaaa; "—" quando não há data.
export function formatDateOnly(value) {
  return isoToBr(value) || "—";
}

export { todayIso };

export function paymentMethodLabel({ hasPix, hasBankAccount }) {
  if (hasPix && hasBankAccount) return "Pix e transferência (TED)";
  if (hasPix) return "Pix";
  if (hasBankAccount) return "Transferência (TED)";
  return "—";
}
