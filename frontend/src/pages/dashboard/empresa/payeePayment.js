import { isValidCnpj, isValidCpf } from "../../../utils/authRules.js";
import { pixKeyTypeLabel } from "../../../utils/employees.js";

const digitsOf = (value) => value.replace(/\D/g, "");

export const EMPTY_PIX = { type: "Cpf", key: "", holder: "" };
export const EMPTY_BANK = { bankCode: "", branch: "", account: "", accountType: "Corrente", holder: "" };

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

// Chave CPF/CNPJ já é o documento de quem recebe; as outras pedem o titular.
export const needsPixHolder = (type) => type !== "Cpf" && type !== "Cnpj";

function holderProblem(holder) {
  const digits = digitsOf(holder ?? "");
  if (!digits) return "Informe o CPF ou CNPJ do titular que recebe.";
  if (digits.length === 11 ? isValidCpf(digits) : digits.length === 14 && isValidCnpj(digits)) return "";
  return "CPF ou CNPJ do titular inválido.";
}

export function bankProblem({ bankCode, branch, account, holder }) {
  if (!/^\d{3}$/.test(bankCode.trim())) return "Informe o código do banco com 3 dígitos.";
  if (!/^\d{1,4}(-[\dX])?$/i.test(branch.trim())) return "Agência inválida.";
  if (!/^\d{1,20}(-[\dX])?$/i.test(account.trim())) return "Número da conta inválido.";
  return holderProblem(holder);
}


export const PAYMENT_OPTIONS = [
  { value: "later", label: "Depois" },
  { value: "pix", label: "Pix" },
  { value: "bank", label: "Transferência (TED)" },
];

export function paymentProblem(payment, pix, bank) {
  if (payment === "pix") return pixProblem(pix) || (needsPixHolder(pix.type) ? holderProblem(pix.holder) : "");
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

export const PAYMENT_METHOD_OPTIONS = PAYMENT_OPTIONS.filter((o) => o.value !== "later");

export const paymentMethodPayload = (payment, { pix, bank }) =>
  payment === "pix"
    ? { kind: "Pix", pix: pixPayload(pix), holderDocument: needsPixHolder(pix.type) ? digitsOf(pix.holder) : null }
    : { kind: "Bank", bank: bankPayload(bank), holderDocument: digitsOf(bank.holder) };

export function paymentMethodRemoval(method, run) {
  return method.kind === "Pix"
    ? { title: "Remover chave Pix?", message: `${pixKeyTypeLabel(method.pix?.type)} ${method.pix?.keyMasked ?? ""}`, confirmLabel: "Remover", run }
    : {
        title: "Remover conta bancária?",
        message: `Banco ${method.bank?.bankCode} · Agência ${method.bank?.branch} · Conta ${method.bank?.accountMasked}`,
        confirmLabel: "Remover",
        run,
      };
}

export function savePaymentChoice(payment, { pix, bank }, save) {
  if (payment === "pix" || payment === "bank") return save(paymentMethodPayload(payment, { pix, bank }));
  return Promise.resolve(null);
}

export function paymentFailureMessage(subject, payment, err) {
  return `${subject} cadastrado, mas ${payment === "pix" ? "a chave Pix" : "a conta"} não foi salva: ${
    err.message || "tente de novo."
  } Corrija e salve, ou configure depois no detalhe.`;
}
