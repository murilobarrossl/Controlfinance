import { isValidCnpj, isValidCpf } from "../../../utils/authRules.js";
import { pixKeyTypeLabel } from "../../../utils/employees.js";

const digitsOf = (value) => value.replace(/\D/g, "");

export const EMPTY_PIX = { type: "Cpf", key: "" };
export const EMPTY_BANK = { bankCode: "", branch: "", account: "", accountType: "Corrente" };

export function pixProblem({ type, key }) {
  const value = key.trim();
  if (!value) return "Informe a chave Pix.";
  const digits = digitsOf(value);
  switch (type) {
    case "Cpf":
      return isValidCpf(digits) ? "" : "Chave Pix: CPF inválido.";
    case "Cnpj":
      return isValidCnpj(digits) ? "" : "Chave Pix: CNPJ inválido.";
    case "Email":
      return /^[^\s@]+@[^\s@]+\.[a-z]{2,}$/i.test(value) && value.length <= 77 ? "" : "Chave Pix: e-mail inválido.";
    case "Phone": {
      const local = digits.length >= 12 && digits.startsWith("55") ? digits.slice(2) : digits;
      return local.length === 10 || local.length === 11 ? "" : "Chave Pix: informe o celular com DDD.";
    }
    default:
      return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) ? "" : "Chave Pix aleatória inválida.";
  }
}

export function bankProblem({ bankCode, branch, account }) {
  if (!/^\d{3}$/.test(bankCode.trim())) return "Informe o código do banco com 3 dígitos.";
  if (!/^\d{1,4}(-[\dX])?$/i.test(branch.trim())) return "Agência inválida.";
  if (!/^\d{1,20}(-[\dX])?$/i.test(account.trim())) return "Número da conta inválido.";
  return "";
}


export const PAYMENT_OPTIONS = [
  { value: "later", label: "Depois" },
  { value: "pix", label: "Pix" },
  { value: "bank", label: "Transferência" },
];

export function paymentProblem(payment, pix, bank) {
  if (payment === "pix") return pixProblem(pix);
  if (payment === "bank") return bankProblem(bank);
  return "";
}

export const pixPayload = (pix) => ({ type: pix.type, key: pix.key.trim() });

export const bankPayload = (bank) => ({
  bankCode: bank.bankCode.trim(),
  branch: bank.branch.trim(),
  account: bank.account.trim(),
  accountType: bank.accountType,
});

export function savePaymentChoice(payment, { pix, bank }, { savePix, saveBank }) {
  if (payment === "pix") return savePix(pixPayload(pix));
  if (payment === "bank") return saveBank(bankPayload(bank));
  return Promise.resolve(null);
}

export function paymentFailureMessage(subject, payment, err) {
  return `${subject} cadastrado, mas ${payment === "pix" ? "a chave Pix" : "a conta"} não foi salva: ${
    err.message || "tente de novo."
  } Corrija e salve, ou configure depois no detalhe.`;
}

export function paymentRemovalConfirmations(payee, { removePix, removeBank }) {
  return {
    removePix: {
      title: "Remover chave Pix?",
      message: `${pixKeyTypeLabel(payee.pix?.type)} ${payee.pix?.keyMasked ?? ""}`,
      confirmLabel: "Remover",
      run: removePix,
    },
    removeBank: {
      title: "Remover conta para transferência?",
      message: `Banco ${payee.bank?.bankCode} · Agência ${payee.bank?.branch} · Conta ${payee.bank?.accountMasked}`,
      confirmLabel: "Remover",
      run: removeBank,
    },
  };
}
